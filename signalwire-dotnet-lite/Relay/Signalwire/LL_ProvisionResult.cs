using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace SignalWire.Relay.Signalwire
{
    public sealed class LL_ProvisionResult
    {
        [JsonProperty("configuration", Required = Required.Always)]
        public JObject Configuration { get; set; }
    }
}
