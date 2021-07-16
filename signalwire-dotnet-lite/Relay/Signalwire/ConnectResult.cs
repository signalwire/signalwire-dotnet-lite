using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace SignalWire.Relay.Signalwire
{
    public sealed class ConnectResult
    {
        [JsonProperty("identity", Required = Required.Always)]
        public string Identity { get; set; }
        [JsonProperty("authorization", NullValueHandling = NullValueHandling.Ignore)]
        public object Authorization { get; set; }

        [JsonProperty("protocol", Required = Required.Always)]
        public string Protocol { get; set; }

        [JsonProperty("ice_servers", NullValueHandling = NullValueHandling.Ignore)]
        public List<IceServer> IceServers { get; set; }
    }
}
