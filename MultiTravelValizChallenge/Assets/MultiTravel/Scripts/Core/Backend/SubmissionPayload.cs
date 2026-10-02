using System;
using MultiTravel.Core.Session;
using Newtonsoft.Json;

namespace MultiTravel.Core.Backend
{
    /// <summary>
    /// Everything needed to (re)submit one result (ARCHITECTURE.md §4). Persisted by <c>SubmissionOutbox</c> to
    /// <c>persistentDataPath/outbox/{submissionId}.json</c> before the first network attempt, so it is self-contained:
    /// the participant data is included because <c>submit_result</c> upserts the participant when missing.
    /// </summary>
    public sealed class SubmissionPayload
    {
        [JsonProperty("submission_id")]
        public Guid SubmissionId { get; set; }

        [JsonProperty("client_session_id")]
        public Guid ClientSessionId { get; set; }

        [JsonProperty("station_id")]
        public string StationId { get; set; }

        [JsonProperty("participant")]
        public ParticipantPayload Participant { get; set; }

        [JsonProperty("score")]
        public int Score { get; set; }

        [JsonProperty("completion_ms")]
        public long CompletionMs { get; set; }

        [JsonProperty("correct_count")]
        public int CorrectCount { get; set; }

        [JsonProperty("incorrect_count")]
        public int IncorrectCount { get; set; }

        [JsonProperty("required_total")]
        public int RequiredTotal { get; set; }

        [JsonProperty("placed_product_ids")]
        public string[] PlacedProductIds { get; set; }

        /// <summary>"female" / "male".</summary>
        [JsonProperty("gender")]
        public string Gender { get; set; }

        /// <summary>"completed" / "abandoned".</summary>
        [JsonProperty("status")]
        public string Status { get; set; }

        /// <summary>snake_case completion reason.</summary>
        [JsonProperty("completion_reason")]
        public string CompletionReason { get; set; }

        [JsonProperty("completed_at")]
        public DateTime CompletedAtUtc { get; set; }

        [JsonProperty("client_version")]
        public string ClientVersion { get; set; }

        [JsonProperty("created_at")]
        public DateTime CreatedAtUtc { get; set; }

        /// <summary>Builds the payload from a completed session (requires <see cref="ParticipantSession.Result"/> and <see cref="ParticipantSession.SubmissionId"/>).</summary>
        public static SubmissionPayload FromSession(ParticipantSession session, string stationId, string clientVersion)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            if (session.Result == null || !session.SubmissionId.HasValue)
            {
                throw new InvalidOperationException("The session has no finalised result.");
            }

            var result = session.Result;
            return new SubmissionPayload
            {
                SubmissionId = session.SubmissionId.Value,
                ClientSessionId = session.ClientSessionId,
                StationId = stationId ?? string.Empty,
                Participant = ParticipantPayload.FromSession(session, stationId),
                Score = result.Score,
                CompletionMs = result.CompletionMs,
                CorrectCount = result.CorrectCount,
                IncorrectCount = result.IncorrectCount,
                RequiredTotal = result.RequiredTotal,
                PlacedProductIds = (string[])result.PlacedProductIds.Clone(),
                Gender = WireFormats.GenderToWire(session.Gender),
                Status = WireFormats.StatusCompleted,
                CompletionReason = WireFormats.CompletionReasonToWire(result.Reason),
                CompletedAtUtc = result.CompletedAtUtc,
                ClientVersion = clientVersion ?? string.Empty,
                CreatedAtUtc = DateTime.UtcNow
            };
        }
    }
}
