using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SignalWire.Relay.Signalwire;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("JWT")]

namespace SignalWire.Relay
{
    public class SignalwireAPI
    {
        public delegate void EventCallback(Client client, Request request);

        private readonly ILogger mLogger = null;

        private readonly Client mClient = null;

        internal SignalwireAPI(Client client)
        {
            mLogger = SignalWireLogging.CreateLogger<Client>();
            mClient = client;
        }

        protected ILogger Logger { get { return mLogger; } }

        public Client Client { get { return mClient; } }
        public string Protocol { get; internal set; }

        public event EventCallback OnEvent;

        internal void ExecuteEventCallback(Request request) { OnEvent?.Invoke(mClient, request); }

        internal void Reset()
        {
            Protocol = null;
        }

        internal async Task<TResult> ExecuteAsync<TParams, TResult>(string method, TParams parameters, int ttl = Request.DEFAULT_RESPONSE_TIMEOUT_SECONDS)
        {
            TaskCompletionSource<TResult> tcs = new TaskCompletionSource<TResult>();
            if (Protocol == null)
            {
                string message = "Setup has not been performed";
                tcs.SetException(new KeyNotFoundException(message));
                Log(LogLevel.Error, message);
                return await tcs.Task;
            }
            if (ttl < 1) ttl = Request.DEFAULT_RESPONSE_TIMEOUT_SECONDS;

            Task<ResponseTaskResult<TResult>> task = mClient.Session.ExecuteAsync<TResult>(method, parameters, TimeSpan.FromSeconds(ttl));
            ResponseTaskResult<TResult> result = await task;

            if (task.IsFaulted) tcs.SetException(task.Exception);
            else tcs.SetResult(result.Result);

            return await tcs.Task;
        }

        // Utility
        internal void ThrowIfError(string code, string message)
        {
            if (code == "200") return;

            Log(LogLevel.Warning, message);
            switch (code)
            {
                // @TODO: Convert error codes to appropriate exception types
                default: throw new InvalidOperationException(message);
            }
        }

        // Inbound Contexts

        public void Receive(params string[] contexts)
        {
            ReceiveAsync(contexts).Wait();
        }

        public async Task ReceiveAsync(params string[] contexts)
        {
            Task<LL_ReceiveResult> taskSignalwireReceiveResult = LL_ReceiveAsync(new LL_ReceiveParams()
            {
                Contexts = new List<string>(contexts),
            });
            // The use of await ensures that exceptions are rethrown, or OperationCancelledException is thrown
            LL_ReceiveResult signalwireReceiveResult = await taskSignalwireReceiveResult;

            ThrowIfError(signalwireReceiveResult.Code, signalwireReceiveResult.Message);
        }

        public Task<LL_ReceiveResult> LL_ReceiveAsync(LL_ReceiveParams parameters)
        {
            return ExecuteAsync<LL_ReceiveParams, LL_ReceiveResult>("signalwire.receive", parameters);
        }

        // Configuration Provisioning

        public async Task<JObject> ProvisionAsync(string target, string localEndPoint, string externalEndPoint)
        {
            Task<LL_ProvisionResult> taskSignalwireProvisionResult = LL_ProvisionAsync(new LL_ProvisionParams()
            {
                Target = target,
                LocalEndpoint = localEndPoint,
                ExternalEndpoint = externalEndPoint,
            });
            // The use of await ensures that exceptions are rethrown, or OperationCancelledException is thrown
            LL_ProvisionResult signalwireProvisionResult = await taskSignalwireProvisionResult;
            return signalwireProvisionResult.Configuration;
        }

        public async Task<LL_ProvisionResult> LL_ProvisionAsync(LL_ProvisionParams parameters)
        {
            return await ExecuteAsync<LL_ProvisionParams, LL_ProvisionResult>("signalwire.provision", parameters);
        }

        // Logging

        internal void Log(LogLevel level, string message,
            [CallerMemberName] string callerName = "", [CallerFilePath] string callerFile = "", [CallerLineNumber] int lineNumber = 0)
        {
            JObject logParamsObj = new JObject();
            logParamsObj["calling-file"] = System.IO.Path.GetFileName(callerFile);
            logParamsObj["calling-method"] = callerName;
            logParamsObj["calling-line-number"] = lineNumber.ToString();

            logParamsObj["message"] = message;

            mLogger.Log(level, new EventId(), logParamsObj, null, SignalWireLogging.DefaultLogStateFormatter);
        }

        internal void Log(LogLevel level, Exception exception, string message,
            [CallerMemberName] string callerName = "", [CallerFilePath] string callerFile = "", [CallerLineNumber] int lineNumber = 0)
        {
            JObject logParamsObj = new JObject();
            logParamsObj["calling-file"] = System.IO.Path.GetFileName(callerFile);
            logParamsObj["calling-method"] = callerName;
            logParamsObj["calling-line-number"] = lineNumber.ToString();

            logParamsObj["message"] = message;

            mLogger.Log(level, new EventId(), logParamsObj, exception, SignalWireLogging.DefaultLogStateFormatter);
        }
    }
}
