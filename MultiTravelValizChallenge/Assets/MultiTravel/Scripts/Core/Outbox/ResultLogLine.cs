using System;
using System.Globalization;
using System.Text;

namespace MultiTravel.Core.Outbox
{
    /// <summary>
    /// One PII-free line of <c>persistentDataPath/results-log.csv</c> (ARCHITECTURE.md §4).
    /// Contains identifiers, score, time, gender and status only: never names, phone numbers or e-mail addresses.
    /// </summary>
    public sealed class ResultLogLine
    {
        public const string Header =
            "logged_at_utc,client_session_id,submission_id,score,completion_ms,gender,status,completion_reason,submitted";

        public DateTime LoggedAtUtc { get; set; } = DateTime.UtcNow;

        public Guid ClientSessionId { get; set; }

        /// <summary>Null when the session never produced a result (abandoned before completion).</summary>
        public Guid? SubmissionId { get; set; }

        public int Score { get; set; }

        public long CompletionMs { get; set; }

        /// <summary>Wire gender ("female" / "male") or empty when not selected.</summary>
        public string Gender { get; set; } = string.Empty;

        /// <summary>"completed" or "abandoned".</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Wire completion reason or empty.</summary>
        public string CompletionReason { get; set; } = string.Empty;

        /// <summary>True once the backend acknowledged the submission.</summary>
        public bool Submitted { get; set; }

        /// <summary>Serialises the line without a trailing newline.</summary>
        public string ToCsv()
        {
            var builder = new StringBuilder(160);
            builder.Append(LoggedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)).Append(',');
            builder.Append(ClientSessionId.ToString("D")).Append(',');
            builder.Append(SubmissionId.HasValue ? SubmissionId.Value.ToString("D") : string.Empty).Append(',');
            builder.Append(Score.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(CompletionMs.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(Escape(Gender)).Append(',');
            builder.Append(Escape(Status)).Append(',');
            builder.Append(Escape(CompletionReason)).Append(',');
            builder.Append(Submitted ? "true" : "false");
            return builder.ToString();
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (value.IndexOf(',') < 0 && value.IndexOf('"') < 0 && value.IndexOf('\n') < 0 && value.IndexOf('\r') < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
