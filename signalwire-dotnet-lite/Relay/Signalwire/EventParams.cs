using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace SignalWire.Relay.Signalwire
{
    public sealed class EventParams
    {
        [JsonProperty("protocol", Required = Required.Always)]
        public string Protocol { get; set; }
        [JsonProperty("type", Required = Required.Always)]
        public string Type { get; set; }
        [JsonProperty("params", NullValueHandling = NullValueHandling.Ignore)]
        public object Parameters { get; set; }

        public T ParametersAs<T>() { return Parameters == null ? default(T) : (Parameters as JObject).ToObject<T>(); }
    }
}
