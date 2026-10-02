using System;
using Newtonsoft.Json;

namespace MultiTravel.Core.Backend
{
    /// <summary>Response of <c>ping_event</c>: <c>{ "ok": true, "event_name": text, "server_time": timestamptz }</c>.</summary>
    public sealed class PingReceipt
    {
        [JsonProperty("ok")]
        public bool Ok { get; set; }

        [JsonProperty("event_name")]
        public string EventName { get; set; }

        [JsonProperty("server_time")]
        public DateTime? ServerTime { get; set; }
    }
}
