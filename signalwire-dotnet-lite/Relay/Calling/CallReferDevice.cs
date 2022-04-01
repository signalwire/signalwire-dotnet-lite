using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace SignalWire.Relay.Calling
{
    public sealed class CallReferDevice
    {
        [JsonProperty("type", Required = Required.Always)]
        public string Type { get; set; } = "sip";

        public sealed class Params
        {
            [JsonProperty("to", Required = Required.Always)]
            public string To { get; set; }

            [JsonProperty("headers", Required = Required.Always)]
            public string Headers { get; set; }
        }

       // public T ParametersAs<T>() { return Parameters == null ? default(T) : (Parameters as JObject).ToObject<T>(); }
    }
}
