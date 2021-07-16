using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SignalWire.Relay.Signalwire;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SignalWire.Relay
{
    public sealed class Session : IDisposable
    {
        public sealed class SessionOptions
        {
            public Uri Bootstrap { get; set; }
            public string Authentication { get; set; }
            public string Agent { get; set; }
            public TimeSpan ConnectDelay { get; set; } = TimeSpan.FromSeconds(5);
            public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);
            public TimeSpan CloseTimeout { get; set; } = TimeSpan.FromSeconds(5);
        }

        public enum SessionState
        {
            Offline,
            Connecting,
            Running,
            Closing,
            Closed,
            Shutdown,
        }

        public delegate void SessionCallback(Session session);
        public delegate void SessionRequestCallback(Session session, Request request);
        public delegate void SessionEventCallback(Session session, Request request, EventParams eventParams);
        public delegate void SessionRequestResponseCallback(Session session, Request request, Response response);

        private readonly ILogger mLogger = null;

        private readonly SessionOptions mOptions = null;
        internal SessionOptions Options { get { return mOptions; } }

        private bool mDisposed = false;

        private SessionState mState = SessionState.Offline;
        private bool mShutdown = false;
        private DateTime mConnectAt = DateTime.Now;
        private bool mRemoteDisconnect = false;
        private Thread mTaskThread = null;
        private List<Task> mTasks = new List<Task>();

        private ClientWebSocket mSocket = null;

        private byte[] mReceiveBuffer = new byte[1024 << 10];
        private byte[] mSendBuffer = new byte[1024 << 10];

        private ConcurrentQueue<string> mSendQueue = new ConcurrentQueue<string>();
        private int mSending = 0;

        private ConcurrentDictionary<string, Request> mRequests = new ConcurrentDictionary<string, Request>();

        public event SessionCallback OnStateChanged;
        public event SessionCallback OnReady;
        public event SessionCallback OnDisconnected;
        public event SessionEventCallback OnEvent;

        public Session(SessionOptions options)
        {
            mLogger = SignalWireLogging.CreateLogger<Session>();
            mOptions = options ?? throw new ArgumentNullException("options");
            if (options.Bootstrap == null) throw new ArgumentNullException("Options.Bootstrap");
            
            mTaskThread = new Thread(TaskWorker);
        }

        public void Start()
        {
            if (mTaskThread.ThreadState == ThreadState.Unstarted) mTaskThread.Start();
        }

        public void Disconnect()
        {
            Close(WebSocketCloseStatus.NormalClosure, "Disconnect requested");
        }

        #region Disposable
        ~Session()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!mDisposed)
            {
                if (disposing)
                {
                    if (!mShutdown)
                    {
                        mShutdown = true;
                        Close(WebSocketCloseStatus.EndpointUnavailable, "Client is shutting down");

                        if (mTaskThread != null) mTaskThread.Join();
                    }
                }
                mDisposed = true;
            }
        }
        #endregion

        public SessionState State
        {
            get { return mState; }
            private set
            {
                if (mState != value)
                {
                    Log(LogLevel.Information, string.Format("Session state changing from '{0}' to '{1}'", mState, value));
                    mState = value;
                    OnStateChanged?.Invoke(this);
                }
            }
        }

        public string Identity { get; private set; }

        public string Protocol { get; private set; }

        public List<IceServer> IceServers { get; private set; } = new List<IceServer>();

        public object UserData { get; set; }

        private ConcurrentDictionary<int, string> mTaskNames = new ConcurrentDictionary<int, string>();
        private void AddTask(string name, Task task)
        {
            lock (mTasks)
            {
                mTaskNames.TryAdd(task.Id, name);
                mTasks.Add(task);
            }
        }
        private void RemoveTask(Task task)
        {
            lock (mTasks)
            {
                mTaskNames.TryRemove(task.Id, out string _);
                mTasks.Remove(task);
            }
        }

        private void TaskWorker()
        {
            Task[] tasks = null;
            Log(LogLevel.Debug, "TaskWorker Started");
            try
            {
                while (State != SessionState.Shutdown)
                {
                    if (State == SessionState.Offline)
                    {
                        if (mShutdown)
                        {
                            State = SessionState.Shutdown;
                            break;
                        }

                        if (DateTime.Now >= mConnectAt)
                        {
                            Log(LogLevel.Information, "Connecting to " + mOptions.Bootstrap);

                            mSocket = new ClientWebSocket();

#if NETCOREAPP2_1 || NETSTANDARD2_1
                            mSocket.Options.RemoteCertificateValidationCallback += (s, c, ch, e) => true;
#endif
                            mSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(5); // 5 second ping/pong check
                            mSocket.Options.SetBuffer(64 * 1024, 64 * 1024); // 64kb buffers before continuation is used, per .NET Framework limit


                            try
                            {
                                State = SessionState.Connecting;
                                AddTask("ConnectAsync", mSocket.ConnectAsync(mOptions.Bootstrap, new CancellationTokenSource(mOptions.ConnectTimeout).Token).ContinueWith(OnConnect));
                            }
                            catch (Exception exc)
                            {
                                Log(LogLevel.Error, exc, "ConnectAsync Exception");
                                State = SessionState.Closed;
                            }
                        }
                    }
                    else if (State == SessionState.Closed)
                    {
                        Log(LogLevel.Information, "Closed");

                        lock (mTasks)
                        {
                            tasks = mTasks.ToArray();
                        }
                        Task.WaitAll(tasks, 10000);

                        List<Task> unfinished = mTasks.FindAll(t => t.Status != TaskStatus.RanToCompletion);
                        if (unfinished.Count > 0)
                        {
                            foreach (Task t in unfinished) Log(LogLevel.Error, string.Format("Unfinished task: {0}, {1}", t.Id, mTaskNames[t.Id]));
                            System.Diagnostics.Debug.Assert(false, "Tasks did not finish, asserted to check what remains instead of hanging indefinately");
                        }

                        lock (mTasks)
                        {
                            mTasks.Clear();
                        }

                        Identity = null;

                        mSocket.Dispose();
                        mSocket = null;

                        // Fuzzy reconnection logic by +- 10% of total delay
                        mConnectAt = DateTime.Now.Add(TimeSpan.FromTicks(mOptions.ConnectDelay.Ticks + (mOptions.ConnectDelay.Ticks * (new Random().Next(-10, 10) / 100))));

                        State = SessionState.Offline;

                        OnDisconnected?.Invoke(this);
                    }

                    lock (mTasks)
                    {
                        tasks = mTasks.ToArray();
                    }

                    int completedIndex = Task.WaitAny(tasks);
                    if (completedIndex < 0) continue;
                    RemoveTask(mTasks[completedIndex]);
                }
            }
            catch (Exception exc)
            {
                Log(LogLevel.Error, exc, "Critical issue!!! Caught exception that would have caused the task worker to be terminated for the upstream session");
                mSocket.Abort();
                State = SessionState.Closed;
            }
        }

        private void Close(WebSocketCloseStatus status, string description)
        {
            if (State == SessionState.Connecting || State == SessionState.Running)
            {
                State = SessionState.Closing;

                try
                {
                    if (mSocket.State == WebSocketState.Open || mSocket.State == WebSocketState.CloseReceived)
                    {
                        Log(LogLevel.Information, "Closing");
                        AddTask("CloseAsync", mSocket.CloseAsync(status, description, new CancellationTokenSource(mOptions.CloseTimeout).Token).ContinueWith(t => State = SessionState.Closed));
                    }
                    else State = SessionState.Closed;
                }
                catch (Exception exc)
                {
                    Log(LogLevel.Error, exc, "CloseAsync Exception");
                    State = SessionState.Closed;
                }
            }
        }

        private void OnConnect(Task task)
        {
            if (task.Status == TaskStatus.Faulted)
            {
                Log(LogLevel.Warning, task.Exception, "Connect failed");
                State = SessionState.Closed;
                return;
            }

            Log(LogLevel.Information, "Connected");

            try
            {
                AddTask("ReceiveAsync", mSocket.ReceiveAsync(new ArraySegment<byte>(mReceiveBuffer), CancellationToken.None).ContinueWith(OnReceived));
            }
            catch (Exception exc)
            {
                Log(LogLevel.Error, exc, "ReceiveAsync Exception");
                Close(WebSocketCloseStatus.InternalServerError, "Server dropped connection ungracefully");
                return;
            }
            AddTask("OnConnect OnPulse Delay", Task.Delay(TimeSpan.FromSeconds(1)).ContinueWith(OnPulse));

            Version version = GetType().Assembly.GetName().Version;
            string agent = string.Format(".NET SDK/{0}.{1}.{2}", version.Major, version.Minor, version.Build);
            if (!string.IsNullOrWhiteSpace(mOptions.Agent)) agent += "/" + mOptions.Agent;

            Request request = Request.Create("signalwire.connect", out ConnectParams param, OnSignalwireConnectResponse);
            param.Authentication = JsonConvert.DeserializeObject(mOptions.Authentication);
            param.Agent = agent;
            Send(request, true);
        }

        private void OnSignalwireConnectResponse(Session session, Request request, Response response)
        {
            if (response.IsError)
            {
                Log(LogLevel.Error, string.Format("Error occurred during blade.connect: {0}, {1}", response.Error.Code, response.Error.Message));
                Close(WebSocketCloseStatus.NormalClosure, "Error occurred during blade.connect");
                return;
            }

            ConnectResult result = response.ResultAs<ConnectResult>();

            Identity = result.Identity;

            mRequests.Clear();
            while (mSendQueue.TryDequeue(out _));

            session.Protocol = result.Protocol;

            if (result.IceServers != null)
            {
                IceServers.Clear();
                IceServers.AddRange(result.IceServers);
            }

            mRemoteDisconnect = false;
            State = SessionState.Running;

            OnReady?.Invoke(this);

            // continue sending if we restored and had stuff queued up, and haven't already triggered sending again
            if (Interlocked.CompareExchange(ref mSending, 1, 0) == 1) return;

            InternalSend();
        }

        private void OnPulse(Task task)
        {
            // This is called once per second while the session is connecting or running
            foreach (var kv in mRequests)
            {
                if (DateTime.Now < kv.Value.ResponseTimeout) continue;

                if (mRequests.TryRemove(kv.Key, out Request request))
                {
                    Log(LogLevel.Information, string.Format("Pending request removed due to timeout: {0}, {1}", request.ID, request.Method));
                    request.Callback?.Invoke(this, request, Response.CreateError(request, -32000, "Timeout", null, null));
                }
            }
            if (State == SessionState.Connecting || State == SessionState.Running)
            {
                AddTask("OnPulse Delay", Task.Delay(TimeSpan.FromSeconds(1)).ContinueWith(OnPulse));
            }
        }

        public bool Send(Request request) { return Send(request, false); }
        private bool Send(Request request, bool immediate)
        {
            if (State != SessionState.Connecting && State != SessionState.Running)
            {
                Log(LogLevel.Debug, "Send request failed, session is inactive");
                return false;
            }

            string json = request.ToJSON();

            if (json.Length > mSendBuffer.Length) throw new IndexOutOfRangeException("Request is too large");

            Log(LogLevel.Debug, string.Format("Sending Request Frame: {0} for {1}", request.ID, request.Method));
            Log(LogLevel.Debug, request.ToJSON(Formatting.Indented));

            if (request.ResponseExpected && !mRequests.TryAdd(request.ID, request))
            {
                Log(LogLevel.Error, string.Format("Request already exists in pending requests: {0}", request.ID));
                return false;
            }

            try
            {
                if (!immediate) mSendQueue.Enqueue(json);

                if (Interlocked.CompareExchange(ref mSending, 1, 0) == 1)
                {
                    // if we are already sending, then we're done
                    return true;
                }

                // kick off an internal send from the queue
                if (!immediate) InternalSend();
                else
                {
                    int length = Encoding.UTF8.GetBytes(json, 0, json.Length, mSendBuffer, 0);

                    // This is exclusively for association of the log output to the message id being sent
                    string id = null;
                    try
                    {
                        JObject jobj = JObject.Parse(json);
                        id = jobj.Value<string>("id");
                    }
                    catch (Exception exc)
                    {
                        Log(LogLevel.Error, exc, "Unable to parse websocket frame prior to sending");
                    }

                    // output directly from the buffer back to a string to know exactly what we will be sending
                    Log(LogLevel.Debug, string.Format("Sending WebSocket Frame: {0}, {1}", id, length));

                    InternalSendImmediate(new ArraySegment<byte>(mSendBuffer, 0, length), id);
                }
            }
            catch (Exception exc)
            {
                Log(LogLevel.Error, exc, string.Format("Exception occurred while sending request frame: {0}", request.ID));
                return false;
            }

            return true;
        }
        public bool Send(Response response)
        {
            if (State != SessionState.Connecting && State != SessionState.Running)
            {
                Log(LogLevel.Error, "Send response failed, session is inactive");
                return false;
            }

            string json = response.ToJSON();

            if (json.Length > mSendBuffer.Length)
            {
                Log(LogLevel.Error, "Response is too large");
                return false;
            }

            Log(LogLevel.Debug, string.Format("Sending Response Frame: {0}", response.ID));
            Log(LogLevel.Debug, response.ToJSON(Formatting.Indented));

            try
            {
                mSendQueue.Enqueue(json);

                if (Interlocked.CompareExchange(ref mSending, 1, 0) == 1)
                {
                    // if we are already sending, then we're done
                    return true;
                }

                // kick off an internal send from the queue
                InternalSend();
            }
            catch (Exception exc)
            {
                Log(LogLevel.Error, exc, string.Format("Exception occurred while sending response frame: {0}", response.ID));
                return false;
            }

            return true;
        }

        private void InternalSend()
        {
            if (State != SessionState.Running)
            {
                mSending = 0;
                return;
            }

            // if the queue still has more to send then we grab the oldest in FIFO order but only if
            // we haven't received a remote disconnect request indicating we should stop sending until
            // the connection drops and the session gets restored again
            if (mRemoteDisconnect || !mSendQueue.TryDequeue(out string json))
            {
                mSending = 0;
                return;
            }

            // stuff whatever is next into the send buffer
            int length = Encoding.UTF8.GetBytes(json, 0, json.Length, mSendBuffer, 0);

            // This is exclusively for association of the log output to the message id being sent
            string id = null;
            try
            {
                JObject jobj = JObject.Parse(json);
                id = jobj.Value<string>("id");
            }
            catch (Exception exc)
            {
                Log(LogLevel.Error, exc, "Unable to parse websocket frame prior to sending");
            }

            // output directly from the buffer back to a string to know exactly what we will be sending
            Log(LogLevel.Debug, string.Format("Sending WebSocket Frame: {0}, {1}", id, length));

            InternalSendImmediate(new ArraySegment<byte>(mSendBuffer, 0, length), id);
        }

        private void InternalSendImmediate(ArraySegment<byte> segment, string id)
        {
            try
            {
                Log(LogLevel.Debug, string.Format("SendAsync Task Starting for message {0}", id));
                Task task = mSocket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None).ContinueWith(t => { Log(LogLevel.Debug, string.Format("SendAsync Task Finished {0}, {1} for message {2}", t.Status, t.Id, id)); InternalSend(); });
                Log(LogLevel.Debug, string.Format("SendAsync Task Started {0} for message {1}", task.Id, id));
                AddTask("SendAsync", task);
            }
            catch (Exception exc)
            {
                Log(LogLevel.Error, exc, string.Format("SendAsync Exception for message {0}", id));
                mSending = 0;
                Close(WebSocketCloseStatus.InternalServerError, "Server dropped connection ungracefully");
            }
        }

        private void OnReceived(Task<WebSocketReceiveResult> task)
        {
            WebSocketReceiveResult wsrr = null;

            if (task.Status == TaskStatus.Faulted)
            {
                Close(WebSocketCloseStatus.InternalServerError, "Server dropped connection ungracefully");
                return;
            }

            try
            {
                wsrr = task.Result;
            }
            catch (Exception exc)
            {
                Log(LogLevel.Error, exc, "ReceiveAsync Exception");
                Close(WebSocketCloseStatus.InternalServerError, "Server dropped connection ungracefully");
                return;
            }

            switch (wsrr.MessageType)
            {
                case WebSocketMessageType.Text:
                    {
                        // @todo check !wsrr.EndOfMessage, resize buffer by double upto a max size (128MB?) then disconnect if larger?
                        // resize buffer back to preferred size (1MB?) when finished with larger frames?
                        if (!wsrr.EndOfMessage) throw new NotSupportedException("Not yet supporting continuation frames");
                        string frame = Encoding.UTF8.GetString(mReceiveBuffer, 0, wsrr.Count);
                        Log(LogLevel.Debug, string.Format("Received WebSocket Frame: {0}/{1}", wsrr.Count, mReceiveBuffer.Length));

                        //AddTask("OnFrame", Task.Run(() => OnFrame(frame)));
                        try
                        {
                            OnFrame(frame);
                        }
                        catch (Exception exc)
                        {
                            Log(LogLevel.Error, exc, "OnFrame exception");
                        }
                        break;
                    }
                case WebSocketMessageType.Close:
                    Log(LogLevel.Warning, string.Format("WebSocket closed by remote host: {0}, {1}, {2}", wsrr.CloseStatus, wsrr.CloseStatusDescription, mSocket.State));
                    Close(WebSocketCloseStatus.NormalClosure, "Closing due to server initiated close");
                    break;
                default:
                    Log(LogLevel.Error, string.Format("Unhandled MessageType '{0}' from ReceiveAsync", wsrr.MessageType));
                    break;
            }

            if (State == SessionState.Connecting || State == SessionState.Running)
            {
                try
                {
                    AddTask("ReceiveAsync", mSocket.ReceiveAsync(new ArraySegment<byte>(mReceiveBuffer), CancellationToken.None).ContinueWith(OnReceived));
                }
                catch (Exception exc)
                {
                    Log(LogLevel.Error, exc, "ReceiveAsync Exception");
                    Close(WebSocketCloseStatus.InternalServerError, "Server dropped connection ungracefully");
                }
            }
        }

        private void OnFrame(string frame)
        {
            // here we are called from the thread pool, so we can safely block
            JObject obj = JObject.Parse(frame);
            if (obj.ContainsKey("method"))
            {
                // this is a request, halt them until connecting is finished
                while (State == SessionState.Connecting) Thread.Sleep(1);

                Request request = Request.Parse(obj);
                Log(LogLevel.Debug, string.Format("Received Request Frame: {0} for {1}", request.ID, request.Method));
                Log(LogLevel.Debug, obj.ToString(Formatting.None));

                switch (request.Method.ToLower())
                {
                    case "signalwire.ping":
                        OnSignalwirePingRequest(this, request);
                        break;
                    case "signalwire.disconnect":
                        OnSignalwireDisconnectRequest(this, request);
                        break;
                    case "signalwire.event":
                        OnSignalwireEventRequest(this, request);
                        break;
                    default:
                        Log(LogLevel.Warning, string.Format("Unhandled inbound request method '{0}'", request.Method));
                        break;
                }
            }
            else
            {
                Response response = Response.Parse(obj);
                Log(LogLevel.Debug, string.Format("Received Response Frame: {0}", response.ID));
                Log(LogLevel.Debug, obj.ToString(Formatting.None));

                if (!mRequests.TryRemove(response.ID, out Request request))
                {
                    Log(LogLevel.Warning, string.Format("Ignoring response for unexpected id '{0}'", response.ID));
                }
                else
                {
                    Log(LogLevel.Information, string.Format("Pending request removed due to received response: {0}, {1}", request.ID, request.Method));
                    request.Callback?.Invoke(this, request, response);
                }
            }
        }

        private void OnSignalwirePingRequest(Session session, Request request)
        {
            PingParams pingParams = null;
            try
            {
                pingParams = request.ParametersAs<PingParams>();
            }
            catch (Exception exc)
            {
                Log(LogLevel.Error, exc, "Invalid PingParams");
                return;
            }

            Log(LogLevel.Debug, "Ping");

            Response response = Response.Create(request.ID, out PingResult pingResult);
            pingResult.Timestamp = pingParams.Timestamp;
            pingResult.Payload = pingParams.Payload;

            session.Send(response);
        }

        private void OnSignalwireDisconnectRequest(Session session, Request request)
        {
            Log(LogLevel.Information, "Disconnect requested by peer");
            mRemoteDisconnect = true;

            session.Send(Response.Create(request.ID));
        }

        private void OnSignalwireEventRequest(Session session, Request request)
        {
            EventParams eventParams = null;
            try
            {
                eventParams = request.ParametersAs<EventParams>();
            }
            catch (Exception exc)
            {
                Log(LogLevel.Error, exc, "Invalid EventParams");
                return;
            }

            Log(LogLevel.Information, "Event {0} received", eventParams.Type);

            OnEvent(session, request, eventParams);
        }

        #region "Reauthenticate Management"
        public async Task<ResponseTaskResult<ReauthenticateResult>> ReauthenticateAsync(JObject authentication)
        {
            TaskCompletionSource<ResponseTaskResult<ReauthenticateResult>> tcs = new TaskCompletionSource<ResponseTaskResult<ReauthenticateResult>>();
            Request request = Request.Create("signalwire.reauthenticate", out ReauthenticateParams reauthenticateParameters, (s, req, res) =>
            {
                if (res.IsError) tcs.SetException(new InvalidOperationException(res.Error.Message));
                else tcs.SetResult(new ResponseTaskResult<ReauthenticateResult>(req, res, res.ResultAs<ReauthenticateResult>()));
            });

            reauthenticateParameters.Authentication = authentication;

            Send(request);

            return await tcs.Task;
        }
#endregion

#region "Execute Method"
        public async Task<ResponseTaskResult<T>> ExecuteAsync<T>(string method, object parameters)
        {
            return await ExecuteAsync<T>(method, parameters, TimeSpan.FromSeconds(Request.DEFAULT_RESPONSE_TIMEOUT_SECONDS));
        }
        public async Task<ResponseTaskResult<T>> ExecuteAsync<T>(string method, object parameters, TimeSpan ttl)
        {
            TaskCompletionSource<ResponseTaskResult<T>> tcs = new TaskCompletionSource<ResponseTaskResult<T>>();
            Request request = Request.Create(method, (s, req, res) =>
            {
                if (res.IsError)
                {
                    if (res.Error.Code == -32000) tcs.SetException(new TimeoutException(res.Error.Message));
                    else if (res.Error.Code == -32602) tcs.SetException(new ArgumentException(res.Error.Message));
                    else tcs.SetException(new InvalidOperationException(res.Error.Message));
                }
                else tcs.SetResult(new ResponseTaskResult<T>(req, res, res.ResultAs<T>()));
            });
            request.ResponseTimeout = DateTime.Now.Add(ttl);
            request.Parameters = parameters;

            Send(request);
            
            return await tcs.Task;
        }
#endregion

        public void Log(LogLevel level, string message,
            [CallerMemberName] string callerName = "", [CallerFilePath] string callerFile = "", [CallerLineNumber] int lineNumber = 0)
        {
            JObject logParamsObj = new JObject();
            logParamsObj["calling-file"] = Path.GetFileName(callerFile);
            logParamsObj["calling-method"] = callerName;
            logParamsObj["calling-line-number"] = lineNumber.ToString();

            logParamsObj["message"] = message;

            mLogger.Log(level, new EventId(), logParamsObj, null, SignalWireLogging.DefaultLogStateFormatter);
        }

        public void Log(LogLevel level, Exception exception, string message,
            [CallerMemberName] string callerName = "", [CallerFilePath] string callerFile = "", [CallerLineNumber] int lineNumber = 0)
        {
            JObject logParamsObj = new JObject();
            logParamsObj["calling-file"] = Path.GetFileName(callerFile);
            logParamsObj["calling-method"] = callerName;
            logParamsObj["calling-line-number"] = lineNumber.ToString();

            logParamsObj["message"] = message;

            mLogger.Log(level, new EventId(), logParamsObj, exception, SignalWireLogging.DefaultLogStateFormatter);
        }
    }
}
