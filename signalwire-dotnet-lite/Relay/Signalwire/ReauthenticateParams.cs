using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace SignalWire.Relay.Signalwire
{
    public sealed class ReauthenticateParams
    {
        [JsonProperty("authentication", Required = Required.Always)]
        public JObject Authentication { get; set; }
    }
}
