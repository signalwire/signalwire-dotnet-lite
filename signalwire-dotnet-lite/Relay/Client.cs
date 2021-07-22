using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SignalWire.Relay.Signalwire;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SignalWire.Relay
{
    public sealed class Client : IDisposable
    {
        public static string CreateAuthentication(string project, string token)
        {
            return new JObject
            {
                ["project"] = project,
                ["token"] = token
            }.ToString(Formatting.None);
        }

        public static string CreateJWTAuthentication(string project, string jwt_token)
        {
            return new JObject
            {
                ["project"] = project,
                ["jwt_token"] = jwt_token
            }.ToString(Formatting.None);
        }

        public delegate void ClientCallback(Client client);

        private bool mDisposed = false;

        private Session.SessionOptions mOptions = null;

        private string mHost = null;
        private string mProjectID = null;
        private string mToken = null;

        private SignalwireAPI mSignalwireAPI = null;
        private CallingAPI mCallingAPI = null;
        private TaskingAPI mTaskingAPI = null;
        private MessagingAPI mMessagingAPI = null;

        public Client(
            string project,
            string token,
            string host = null,
            string agent = null,
            string[] contexts = null,
            bool jwt = false,
            TimeSpan? connectDelay = null,
            TimeSpan? connectTimeout = null,
            TimeSpan? closeTimeout = null)
        {
            if (string.IsNullOrWhiteSpace(project)) throw new ArgumentNullException("Must provide a project");
            if (string.IsNullOrWhiteSpace(token)) throw new ArgumentNullException("Must provide a token");
            if (string.IsNullOrWhiteSpace(host)) host = "relay.signalwire.com";

            mHost = host;
            mProjectID = project;
            mToken = token;

            string authentication = null;
            if (!jwt) authentication = CreateAuthentication(project, token);
            else authentication = CreateJWTAuthentication(project, token);

            mOptions = new Session.SessionOptions()
            {
                Bootstrap = new Uri("wss://" + host),
                Authentication = authentication,
                Agent = agent,
                Contexts = contexts,
            };
            if (connectDelay.HasValue) mOptions.ConnectDelay = connectDelay.Value;
            if (connectTimeout.HasValue) mOptions.ConnectTimeout = connectTimeout.Value;
            if (closeTimeout.HasValue) mOptions.CloseTimeout = closeTimeout.Value;

            Session = new Session(mOptions);

            mSignalwireAPI = new SignalwireAPI(this);
            mCallingAPI = new CallingAPI(mSignalwireAPI);
            mTaskingAPI = new TaskingAPI(mSignalwireAPI);
            mMessagingAPI = new MessagingAPI(mSignalwireAPI);

            Session.OnReady += s =>
            {
                mSignalwireAPI.Protocol = mOptions.Protocol = s.Protocol;
                s.OnEvent += (s2, r, p) => mSignalwireAPI.ExecuteEventCallback(r);
                OnReady?.Invoke(this);
            };
            Session.OnDisconnected += s => OnDisconnected?.Invoke(this);
        }

        public Session Session { get; private set; }

        public string Host { get { return mHost; } }
        public string ProjectID { get { return mProjectID; } }
        public string Token { get { return mToken; } }

        public SignalwireAPI Signalwire {  get { return mSignalwireAPI; } }

        public CallingAPI Calling { get { return mCallingAPI; } }

        public TaskingAPI Tasking {  get { return mTaskingAPI; } }

        public MessagingAPI Messaging { get { return mMessagingAPI; } }

        public object UserData { get; set; }


        public event ClientCallback OnReady;
        public event ClientCallback OnDisconnected;

        #region Disposable
        ~Client()
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
                    Session.Dispose();
                }
                mDisposed = true;
            }
        }
        #endregion

        public void Reset()
        {
            mOptions.Protocol = null;
            mSignalwireAPI.Reset();
            mCallingAPI.Reset();
            mTaskingAPI.Reset();
            mMessagingAPI.Reset();
        }

        public void Connect()
        {
            Session.Start();
        }

        public void Disconnect()
        {
            Session.Disconnect();
        }
    }
}
