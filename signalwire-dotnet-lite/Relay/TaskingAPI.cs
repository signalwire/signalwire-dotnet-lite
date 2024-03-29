﻿using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SignalWire.Relay.Signalwire;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SignalWire.Relay
{
    public sealed class TaskingAPI
    {
        public delegate void TaskReceivedCallback(TaskingAPI api, RelayTask eventParams);

        private readonly ILogger mLogger = null;
        private SignalwireAPI mAPI = null;

        public event TaskReceivedCallback OnTaskReceived;

        internal TaskingAPI(SignalwireAPI api)
        {
            mLogger = SignalWireLogging.CreateLogger<Client>();
            mAPI = api;
            mAPI.OnEvent += OnEvent;
        }

        internal SignalwireAPI API { get { return mAPI; } }

        internal void Reset()
        {
        }

        private void OnEvent(Client client, Request request)
        {
            EventParams taskingEventParams = null;
            try { taskingEventParams = request.ParametersAs<EventParams>(); }
            catch (Exception exc)
            {
                Log(LogLevel.Warning, exc, "Failed to parse TaskingEventParams");
                return;
            }

            if (taskingEventParams.EventType != "queuing.relay.tasks") return;

            RelayTask relayTask = null;
            try { relayTask = taskingEventParams.ParametersAs<RelayTask>(); }
            catch (Exception exc)
            {
                Log(LogLevel.Warning, exc, "Failed to parse RelayTask");
                return;
            }

            Log(LogLevel.Debug, "TaskingAPI OnNotification");

            OnTaskReceived?.Invoke(this, relayTask);
        }

        public bool Deliver(string context, JObject message)
        {
            bool successful = true;
            try
            {
                RelayTask.Deliver(API.Client.Host, API.Client.ProjectID, API.Client.Token, context, message);
            }
            catch (Exception exc)
            {
                Log(LogLevel.Warning, exc, "Failed task delivery");
                successful = false;
            }
            return successful;
        }

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
