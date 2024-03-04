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

        [JsonProperty("params", Required = Required.Always)]
        public CallReferParams Params { get; set; }
        
        public sealed class CallReferParams
        {
            [JsonProperty("to", Required = Required.Always)]
            public string To { get; set; }

            [JsonProperty("headers", NullValueHandling = NullValueHandling.Ignore)]
            public string Headers { get; set; }
        }

       // public T ParametersAs<T>() { return Parameters == null ? default(T) : (Parameters as JObject).ToObject<T>(); }
    }
}
