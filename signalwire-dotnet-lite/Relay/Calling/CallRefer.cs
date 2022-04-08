using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace SignalWire.Relay.Calling
{
    public sealed class CallRefer
    {
        [JsonProperty("params", Required = Required.Always)]
        public object Parameters { get; set; }

        public T ParametersAs<T>() { return Parameters == null ? default(T) : ((JObject)Parameters).ToObject<T>(); }
    }
}
