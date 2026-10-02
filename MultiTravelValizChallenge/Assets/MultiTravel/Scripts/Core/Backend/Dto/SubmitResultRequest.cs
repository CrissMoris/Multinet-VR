using System;
using Newtonsoft.Json;

namespace MultiTravel.Core.Backend
{
    /// <summary>Body of <c>POST /rest/v1/rpc/submit_result</c>; property names are the RPC parameter names.</summary>
    public sealed class SubmitResultRequest
    {
        [JsonProperty("p_event_slug")]
        public string EventSlug { get; set; }

        [JsonProperty("p_access_code")]
        public string AccessCode { get; set; }

        [JsonProperty("p_station_id")]
        public string StationId { get; set; }

        [JsonProperty("p_submission_id")]
        public Guid SubmissionId { get; set; }

        [JsonProperty("p_client_session_id")]
        public Guid ClientSessionId { get; set; }

        [JsonProperty("p_participant")]
        public ParticipantPayload Participant { get; set; }

        [JsonProperty("p_score")]
        public int Score { get; set; }

        [JsonProperty("p_completion_ms")]
        public long CompletionMs { get; set; }

        [JsonProperty("p_correct_count")]
        public int CorrectCount { get; set; }

        [JsonProperty("p_incorrect_count")]
        public int IncorrectCount { get; set; }

        [JsonProperty("p_required_total")]
        public int RequiredTotal { get; set; }

        [JsonProperty("p_placed_product_ids")]
        public string[] PlacedProductIds { get; set; }

        [JsonProperty("p_gender")]
        public string Gender { get; set; }

        [JsonProperty("p_status")]
        public string Status { get; set; }

        [JsonProperty("p_completion_reason")]
        public string CompletionReason { get; set; }

        [JsonProperty("p_completed_at")]
        public DateTime CompletedAt { get; set; }

        [JsonProperty("p_client_version")]
        public string ClientVersion { get; set; }
    }
}
