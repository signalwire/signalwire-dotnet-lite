using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace SignalWire.Relay.Signalwire
{
    public sealed class PingParams
    {
        [JsonProperty("timestamp", NullValueHandling = NullValueHandling.Ignore)]
        public double? Timestamp { get; set; }

        [JsonProperty("payload", NullValueHandling = NullValueHandling.Ignore)]
        public string Payload { get; set; }
    }
}
