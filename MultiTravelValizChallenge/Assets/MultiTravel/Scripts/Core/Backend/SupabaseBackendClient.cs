using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MultiTravel.Core.Config;
using MultiTravel.Core.Leaderboard;
using MultiTravel.Core.Session;
using Newtonsoft.Json;

namespace MultiTravel.Core.Backend
{
    /// <summary>
    /// PostgREST RPC client for the Supabase backend (ARCHITECTURE.md §4): <c>POST {SupabaseUrl}/rest/v1/rpc/{fn}</c>
    /// with <c>apikey</c> / <c>Authorization: Bearer</c> headers carrying the client-safe anon key.
    /// Transport is injectable (<see cref="IHttpTransport"/>) so the mapping logic is unit-testable.
    /// </summary>
    public sealed class SupabaseBackendClient : IBackendClient
    {
        public const string RegisterFunction = "register_participant";
        public const string SubmitResultFunction = "submit_result";
        public const string LeaderboardFunction = "get_leaderboard";
        public const string PingFunction = "ping_event";

        public const int DefaultLeaderboardLimit = 100;
        public const int MaxLeaderboardLimit = 1000;
        private const int MaxDetailLength = 400;

        private readonly RuntimeConfig config;
        private readonly IHttpTransport transport;
        private readonly Dictionary<string, string> headers;

        public SupabaseBackendClient(RuntimeConfig config, IHttpTransport transport = null)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.transport = transport ?? new UnityWebRequestTransport();
            headers = BuildHeaders(config.Backend.SupabaseAnonKey);
        }

        /// <summary>Headers sent with every RPC call.</summary>
        public static Dictionary<string, string> BuildHeaders(string anonKey)
        {
            var key = anonKey ?? string.Empty;
            return new Dictionary<string, string>(4)
            {
                { "apikey", key },
                { "Authorization", "Bearer " + key },
                { "Content-Type", "application/json" },
                { "Accept", "application/json" }
            };
        }

        /// <summary>Builds the RPC URL for a function name.</summary>
        public string RpcUrl(string functionName)
        {
            return config.Backend.RpcUrl(functionName);
        }

        public Task<BackendResult<RegisterReceipt>> RegisterAsync(ParticipantSession session, CancellationToken cancellationToken = default)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            if (!session.GenderSelected)
            {
                return Task.FromResult(BackendResult<RegisterReceipt>.Failure(
                    BackendError.FromServer(0, BackendError.CodeValidationFailedPrefix + ":gender", "gender not selected yet")));
            }

            if (!config.Backend.IsConfigured)
            {
                return Task.FromResult(BackendResult<RegisterReceipt>.Failure(BackendError.ConfigurationMissing()));
            }

            return CallRpcAsync<RegisterReceipt>(RegisterFunction, BuildRegisterRequest(config, session), cancellationToken);
        }

        public Task<BackendResult<SubmissionReceipt>> SubmitResultAsync(SubmissionPayload payload, CancellationToken cancellationToken = default)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if (!config.Backend.IsConfigured)
            {
                return Task.FromResult(BackendResult<SubmissionReceipt>.Failure(BackendError.ConfigurationMissing()));
            }

            return CallRpcAsync<SubmissionReceipt>(SubmitResultFunction, BuildSubmitRequest(config, payload), cancellationToken);
        }

        public Task<BackendResult<LeaderboardEntry[]>> GetLeaderboardAsync(int limit, CancellationToken cancellationToken = default)
        {
            if (!config.Backend.HasEndpoint)
            {
                return Task.FromResult(BackendResult<LeaderboardEntry[]>.Failure(BackendError.ConfigurationMissing()));
            }

            var request = new GetLeaderboardRequest
            {
                EventSlug = config.Backend.EventSlug,
                Limit = Math.Max(1, Math.Min(MaxLeaderboardLimit, limit <= 0 ? DefaultLeaderboardLimit : limit))
            };

            return CallRpcAsync<LeaderboardEntry[]>(LeaderboardFunction, request, cancellationToken);
        }

        public Task<BackendResult<PingReceipt>> PingAsync(CancellationToken cancellationToken = default)
        {
            if (!config.Backend.IsConfigured)
            {
                return Task.FromResult(BackendResult<PingReceipt>.Failure(BackendError.ConfigurationMissing()));
            }

            var request = new PingRequest
            {
                EventSlug = config.Backend.EventSlug,
                AccessCode = config.Backend.EventAccessCode
            };

            return CallRpcAsync<PingReceipt>(PingFunction, request, cancellationToken);
        }

        /// <summary>Builds the <c>register_participant</c> body from config + session (pure; used by tests).</summary>
        public static RegisterRequest BuildRegisterRequest(RuntimeConfig config, ParticipantSession session)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            var input = session.Input;
            return new RegisterRequest
            {
                EventSlug = config.Backend.EventSlug,
                AccessCode = config.Backend.EventAccessCode,
                StationId = config.Backend.StationId,
                ClientSessionId = session.ClientSessionId,
                FirstName = input.FirstName,
                LastName = input.LastName,
                Phone = input.Phone,
                Email = input.Email,
                Gender = WireFormats.GenderToWire(session.Gender),
                ConsentAccepted = input.ConsentAccepted,
                ConsentVersion = input.ConsentVersion
            };
        }

        /// <summary>Builds the <c>submit_result</c> body from config + outbox payload (pure; used by tests).</summary>
        public static SubmitResultRequest BuildSubmitRequest(RuntimeConfig config, SubmissionPayload payload)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            var stationId = string.IsNullOrEmpty(payload.StationId) ? config.Backend.StationId : payload.StationId;
            var participant = payload.Participant;
            if (participant != null && string.IsNullOrEmpty(participant.StationId))
            {
                participant.StationId = stationId;
            }

            return new SubmitResultRequest
            {
                EventSlug = config.Backend.EventSlug,
                AccessCode = config.Backend.EventAccessCode,
                StationId = stationId,
                SubmissionId = payload.SubmissionId,
                ClientSessionId = payload.ClientSessionId,
                Participant = participant,
                Score = payload.Score,
                CompletionMs = payload.CompletionMs,
                CorrectCount = payload.CorrectCount,
                IncorrectCount = payload.IncorrectCount,
                RequiredTotal = payload.RequiredTotal,
                PlacedProductIds = payload.PlacedProductIds ?? Array.Empty<string>(),
                Gender = payload.Gender,
                Status = payload.Status,
                CompletionReason = payload.CompletionReason,
                CompletedAt = payload.CompletedAtUtc,
                ClientVersion = string.IsNullOrEmpty(payload.ClientVersion) ? config.ClientVersion : payload.ClientVersion
            };
        }

        private async Task<BackendResult<T>> CallRpcAsync<T>(string functionName, object body, CancellationToken cancellationToken)
        {
            var url = config.Backend.RpcUrl(functionName);
            var json = BackendJson.Serialize(body);

            HttpResponse response;
            try
            {
                response = await transport.PostJsonAsync(url, json, headers, config.Backend.RequestTimeoutSeconds, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return BackendResult<T>.Failure(BackendError.Cancelled());
            }
            catch (Exception ex)
            {
                return BackendResult<T>.Failure(BackendError.Transport(ex.GetType().Name + ": " + ex.Message));
            }

            return MapResponse<T>(response);
        }

        /// <summary>Maps a raw HTTP response to a typed result (pure; used by tests).</summary>
        internal static BackendResult<T> MapResponse<T>(HttpResponse response)
        {
            if (response == null)
            {
                return BackendResult<T>.Failure(BackendError.Transport("no response"));
            }

            if (response.IsCancelled)
            {
                return BackendResult<T>.Failure(BackendError.Cancelled());
            }

            if (response.IsTimeout)
            {
                return BackendResult<T>.Failure(BackendError.Timeout(response.ErrorDetail));
            }

            if (response.IsNetworkError)
            {
                return BackendResult<T>.Failure(BackendError.Transport(response.ErrorDetail));
            }

            if (response.StatusCode >= 200 && response.StatusCode < 300)
            {
                if (string.IsNullOrWhiteSpace(response.Body))
                {
                    return BackendResult<T>.Failure(BackendError.InvalidResponse(response.StatusCode, "empty body"));
                }

                try
                {
                    var value = BackendJson.Deserialize<T>(response.Body);
                    if (value == null)
                    {
                        return BackendResult<T>.Failure(BackendError.InvalidResponse(response.StatusCode, "null body"));
                    }

                    return BackendResult<T>.Success(value);
                }
                catch (JsonException ex)
                {
                    return BackendResult<T>.Failure(BackendError.InvalidResponse(response.StatusCode, ex.Message));
                }
            }

            var serverError = TryParseError(response.Body);
            var serverCode = serverError != null ? serverError.Message : null;
            var detail = serverError != null && !string.IsNullOrEmpty(serverError.Details)
                ? serverError.Details
                : Truncate(response.Body);

            if (serverError != null && !string.IsNullOrEmpty(serverError.Code))
            {
                detail = serverError.Code + ": " + detail;
            }

            return BackendResult<T>.Failure(BackendError.FromServer(response.StatusCode, serverCode, detail));
        }

        private static PostgrestError TryParseError(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            try
            {
                return BackendJson.Deserialize<PostgrestError>(body);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string Truncate(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text.Length <= MaxDetailLength ? text : text.Substring(0, MaxDetailLength) + "...";
        }
    }
}
