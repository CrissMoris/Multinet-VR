using Newtonsoft.Json;

namespace MultiTravel.Core.Backend
{
    /// <summary>Body of <c>POST /rest/v1/rpc/ping_event</c>.</summary>
    public sealed class PingRequest
    {
        [JsonProperty("p_event_slug")]
        public string EventSlug { get; set; }

        [JsonProperty("p_access_code")]
        public string AccessCode { get; set; }
    }
}
