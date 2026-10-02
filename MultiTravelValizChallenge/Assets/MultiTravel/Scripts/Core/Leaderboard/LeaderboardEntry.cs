using System;
using Newtonsoft.Json;

namespace MultiTravel.Core.Leaderboard
{
    /// <summary>One row of <c>get_leaderboard</c> (ARCHITECTURE.md §4). Never contains phone or e-mail.</summary>
    public sealed class LeaderboardEntry
    {
        [JsonProperty("rank")]
        public int Rank { get; set; }

        /// <summary>Display name already shaped by <c>events.name_display_mode</c> on the server.</summary>
        [JsonProperty("display_name")]
        public string DisplayName { get; set; }

        [JsonProperty("score")]
        public int Score { get; set; }

        [JsonProperty("completion_ms")]
        public long CompletionMs { get; set; }

        /// <summary>"female" / "male".</summary>
        [JsonProperty("gender")]
        public string Gender { get; set; }

        [JsonProperty("completed_at")]
        public DateTime? CompletedAt { get; set; }

        public override string ToString()
        {
            return $"#{Rank} {DisplayName} {Score} {CompletionMs}ms";
        }
    }
}
