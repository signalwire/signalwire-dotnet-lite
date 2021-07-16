using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Text;

namespace SignalWire.Relay.Signalwire
{
    public sealed class ConnectParams
    {
        public sealed class VersionParam
        {
            public const int MAJOR = 3;
            public const int MINOR = 0;
            public const int REVISION = 0;

            [JsonProperty("major", Required = Required.Always)]
            public int Major { get; set; } = MAJOR;
            [JsonProperty("minor", Required = Required.Always)]
            public int Minor { get; set; } = MINOR;
            [JsonProperty("revision", Required = Required.Always)]
            public int Revision { get; set; } = REVISION;
        }

        [JsonProperty("version", Required = Required.Always)]
        public VersionParam Version { get; set; } = new VersionParam();
        [JsonProperty("authentication", NullValueHandling = NullValueHandling.Ignore)]
        public object Authentication { get; set; }

        [JsonProperty("agent", NullValueHandling = NullValueHandling.Ignore)]
        public string Agent { get; set; }
        [JsonProperty("identity", NullValueHandling = NullValueHandling.Ignore)]
        public string Identity { get; set; }

        [JsonProperty("protocol", NullValueHandling = NullValueHandling.Ignore)]
        public string Protocol { get; set; }

        [JsonProperty("contexts", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Contexts { get; set; }
    }
}
