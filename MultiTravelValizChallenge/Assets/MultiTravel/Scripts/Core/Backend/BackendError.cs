using System;

namespace MultiTravel.Core.Backend
{
    /// <summary>
    /// Backend failure with a stable <see cref="Code"/>, a Turkish <see cref="Message"/> and a transient flag
    /// that drives the outbox retry policy (ARCHITECTURE.md §4).
    /// </summary>
    public sealed class BackendError
    {
        public const string CodeEventAccessDenied = "EVENT_ACCESS_DENIED";
        public const string CodeEventInactive = "EVENT_INACTIVE";
        public const string CodeValidationFailedPrefix = "VALIDATION_FAILED";
        public const string CodeParticipantNotFound = "PARTICIPANT_NOT_FOUND";
        public const string CodeTransport = "TRANSPORT_ERROR";
        public const string CodeTimeout = "TIMEOUT";
        public const string CodeHttp = "HTTP_ERROR";
        public const string CodeInvalidResponse = "INVALID_RESPONSE";
        public const string CodeConfigurationMissing = "CONFIG_MISSING";
        public const string CodeCancelled = "CANCELLED";
        public const string CodeOutboxCorrupt = "OUTBOX_CORRUPT";

        public BackendError(string code, string message, int httpStatus, bool isTransient, string detail = null)
        {
            Code = string.IsNullOrWhiteSpace(code) ? CodeHttp : code.Trim();
            Message = string.IsNullOrWhiteSpace(message) ? BackendErrorMessages.ServerUnreachable : message;
            HttpStatus = httpStatus;
            IsTransient = isTransient;
            Detail = detail ?? string.Empty;
        }

        /// <summary>Server code (EVENT_ACCESS_DENIED, VALIDATION_FAILED:phone, ...) or a client-side code.</summary>
        public string Code { get; }

        /// <summary>Turkish message for the operator screen.</summary>
        public string Message { get; }

        /// <summary>HTTP status; 0 for transport-level failures.</summary>
        public int HttpStatus { get; }

        /// <summary>True when a retry may succeed (timeouts, network errors, 5xx, 408, 429).</summary>
        public bool IsTransient { get; }

        /// <summary>Technical detail for logs (English / raw server text). Never shown as-is to participants.</summary>
        public string Detail { get; }

        public bool IsValidationFailure => Code.StartsWith(CodeValidationFailedPrefix, StringComparison.Ordinal);

        /// <summary>Field name of a VALIDATION_FAILED:&lt;field&gt; code, or null.</summary>
        public string ValidationField => IsValidationFailure ? ValidationFieldOf(Code) : null;

        /// <summary>Extracts the field from "VALIDATION_FAILED:&lt;field&gt;"; empty when absent.</summary>
        public static string ValidationFieldOf(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return string.Empty;
            }

            int colon = code.IndexOf(':');
            return colon >= 0 && colon < code.Length - 1 ? code.Substring(colon + 1).Trim() : string.Empty;
        }

        /// <summary>True for status codes where a retry is reasonable.</summary>
        public static bool IsTransientStatus(int httpStatus)
        {
            return httpStatus >= 500 || httpStatus == 408 || httpStatus == 429;
        }

        /// <summary>Maps a PostgREST error (HTTP status + <c>message</c> code) to a user-facing error.</summary>
        public static BackendError FromServer(int httpStatus, string serverCode, string detail)
        {
            var code = string.IsNullOrWhiteSpace(serverCode) ? CodeHttp : serverCode.Trim();
            var known = BackendErrorMessages.ForKnownCode(code);
            if (known != null)
            {
                return new BackendError(code, known, httpStatus, false, detail);
            }

            var message = httpStatus == 401 || httpStatus == 403
                ? BackendErrorMessages.Unauthorized
                : BackendErrorMessages.ServerUnreachable;
            return new BackendError(code, message, httpStatus, IsTransientStatus(httpStatus), detail);
        }

        public static BackendError Transport(string detail)
        {
            return new BackendError(CodeTransport, BackendErrorMessages.ServerUnreachable, 0, true, detail);
        }

        public static BackendError Timeout(string detail)
        {
            return new BackendError(CodeTimeout, BackendErrorMessages.ServerUnreachable, 0, true, detail);
        }

        public static BackendError Cancelled()
        {
            return new BackendError(CodeCancelled, BackendErrorMessages.ServerUnreachable, 0, true, "request cancelled");
        }

        public static BackendError InvalidResponse(int httpStatus, string detail)
        {
            return new BackendError(CodeInvalidResponse, BackendErrorMessages.ServerUnreachable, httpStatus, false, detail);
        }

        public static BackendError ConfigurationMissing()
        {
            return new BackendError(CodeConfigurationMissing, BackendErrorMessages.ConfigurationMissing, 0, false, "backend configuration incomplete");
        }

        public static BackendError OutboxCorrupt(string detail)
        {
            return new BackendError(CodeOutboxCorrupt, BackendErrorMessages.OutboxCorrupt, 0, false, detail);
        }

        public override string ToString()
        {
            var text = HttpStatus > 0 ? $"{Code} (HTTP {HttpStatus}): {Message}" : $"{Code}: {Message}";
            return Detail.Length > 0 ? text + " [" + Detail + "]" : text;
        }
    }
}
