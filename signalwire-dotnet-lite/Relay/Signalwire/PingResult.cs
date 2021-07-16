using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace SignalWire.Relay.Signalwire
{
    public sealed class PingResult
    {
        [JsonProperty("timestamp", NullValueHandling = NullValueHandling.Ignore)]
        public double? Timestamp { get; set; }

        [JsonProperty("payload", NullValueHandling = NullValueHandling.Ignore)]
        public string Payload { get; set; }
    }
}
