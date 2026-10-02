using Newtonsoft.Json;

namespace MultiTravel.Core.Backend
{
    /// <summary>Body of <c>POST /rest/v1/rpc/get_leaderboard</c> (no access code: public display).</summary>
    public sealed class GetLeaderboardRequest
    {
        [JsonProperty("p_event_slug")]
        public string EventSlug { get; set; }

        [JsonProperty("p_limit")]
        public int Limit { get; set; }
    }
}
