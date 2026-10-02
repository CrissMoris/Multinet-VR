using System;
using Newtonsoft.Json;

namespace MultiTravel.Core.Backend
{
    /// <summary>Response of <c>submit_result</c>: <c>{ "result_id", "participant_id", "created", "rank" }</c>.</summary>
    public sealed class SubmissionReceipt
    {
        [JsonProperty("result_id")]
        public Guid ResultId { get; set; }

        [JsonProperty("participant_id")]
        public Guid ParticipantId { get; set; }

        /// <summary>False when the submission id already existed (duplicate acknowledgement).</summary>
        [JsonProperty("created")]
        public bool Created { get; set; }

        /// <summary>Leaderboard rank; null for non-completed results or when the server did not compute it.</summary>
        [JsonProperty("rank")]
        public int? Rank { get; set; }
    }
}
