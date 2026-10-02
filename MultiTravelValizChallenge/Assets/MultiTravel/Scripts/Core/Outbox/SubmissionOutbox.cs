using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MultiTravel.Core.Backend;
using MultiTravel.Core.Config;
using Newtonsoft.Json;
using UnityEngine;

namespace MultiTravel.Core.Outbox
{
    /// <summary>
    /// Idempotent, persisted retry queue for result submissions (ARCHITECTURE.md §4).
    /// <list type="bullet">
    /// <item><see cref="Enqueue"/> writes <c>outbox/{submissionId}.json</c> and a PII-free CSV line <b>before</b> any network attempt.</item>
    /// <item><see cref="TrySubmitAsync"/> retries transient failures with backoff 2, 4, 8, 16, 30 s (cap) up to <c>MaxAutoRetries</c> times.</item>
    /// <item><see cref="RetryPendingAsync"/> makes one attempt per file found on disk (app start and periodic background sweeps).</item>
    /// <item>A file is deleted only after the server acknowledged the submission (<c>created</c> true or duplicate).</item>
    /// </list>
    /// Main-thread only; concurrent runs for the same id share one task.
    /// </summary>
    public sealed class SubmissionOutbox
    {
        /// <summary>Delay before retry n (0-based); the last value repeats (cap).</summary>
        public static readonly int[] BackoffScheduleSeconds = { 2, 4, 8, 16, 30 };

        private readonly ILocalStore store;
        private readonly IBackendClient backend;
        private readonly RuntimeConfig config;
        private readonly IDelay delay;
        private readonly HashSet<Guid> pendingIds = new HashSet<Guid>();
        private readonly Dictionary<Guid, SubmissionPayload> payloadCache = new Dictionary<Guid, SubmissionPayload>();
        private readonly Dictionary<Guid, Task<SubmissionAttemptResult>> inFlight = new Dictionary<Guid, Task<SubmissionAttemptResult>>();

        public SubmissionOutbox(ILocalStore store, IBackendClient backend, RuntimeConfig config, IDelay delay = null)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.delay = delay ?? TaskDelay.Instance;
            RefreshPendingFromStore();
        }

        /// <summary>Number of submissions not yet acknowledged by the server (shown in the operator status bar).</summary>
        public int PendingCount => pendingIds.Count;

        /// <summary>Ids of the pending submissions.</summary>
        public IReadOnlyCollection<Guid> PendingIds => pendingIds;

        /// <summary>Automatic retries after the first attempt (from configuration).</summary>
        public int MaxAutoRetries => Math.Max(0, config.Backend.MaxAutoRetries);

        /// <summary>Raised on the main thread after a server acknowledgement (file already deleted).</summary>
        public event Action<Guid, SubmissionReceipt> SubmissionSucceeded;

        /// <summary>Raised on the main thread when a run gives up (file kept for later retries).</summary>
        public event Action<Guid, BackendError> SubmissionFailed;

        /// <summary>Backoff in seconds before the given 0-based retry.</summary>
        public static int BackoffSecondsForRetry(int retryIndex)
        {
            if (retryIndex < 0)
            {
                retryIndex = 0;
            }

            return retryIndex < BackoffScheduleSeconds.Length
                ? BackoffScheduleSeconds[retryIndex]
                : BackoffScheduleSeconds[BackoffScheduleSeconds.Length - 1];
        }

        public bool IsPending(Guid submissionId)
        {
            return pendingIds.Contains(submissionId);
        }

        public bool IsInFlight(Guid submissionId)
        {
            return inFlight.ContainsKey(submissionId);
        }

        /// <summary>
        /// Persists the payload before any network attempt. Returns false (and writes nothing) when the submission id is
        /// already pending, so duplicate enqueues never produce a second file or log line.
        /// </summary>
        public bool Enqueue(SubmissionPayload payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if (payload.SubmissionId == Guid.Empty)
            {
                throw new ArgumentException("Submission id must not be empty.", nameof(payload));
            }

            var id = payload.SubmissionId;
            if (pendingIds.Contains(id))
            {
                return false;
            }

            try
            {
                store.SaveOutboxEntry(id, BackendJson.Serialize(payload));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MultiTravel] Outbox: could not persist submission {id}: {ex.Message}");
            }

            pendingIds.Add(id);
            payloadCache[id] = payload;
            AppendLogSafe(payload, false);
            return true;
        }

        /// <summary>Submits with backoff retries (1 + MaxAutoRetries attempts). Shares the task of a run already in flight.</summary>
        public Task<SubmissionAttemptResult> TrySubmitAsync(Guid submissionId, CancellationToken cancellationToken = default)
        {
            return RunTracked(submissionId, 1 + MaxAutoRetries, cancellationToken);
        }

        /// <summary>One attempt for every pending file (sequential). Returns the number of acknowledged submissions.</summary>
        public async Task<int> RetryPendingAsync(CancellationToken cancellationToken = default)
        {
            RefreshPendingFromStore();
            var ids = new List<Guid>(pendingIds);
            int succeeded = 0;

            for (int i = 0; i < ids.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var id = ids[i];
                if (inFlight.ContainsKey(id))
                {
                    continue;
                }

                var result = await RunTracked(id, 1, cancellationToken);
                if (result.Succeeded)
                {
                    succeeded++;
                }
            }

            return succeeded;
        }

        /// <summary>Adds every entry found on disk to the pending set.</summary>
        public void RefreshPendingFromStore()
        {
            try
            {
                var ids = store.ListOutboxEntries();
                for (int i = 0; i < ids.Count; i++)
                {
                    pendingIds.Add(ids[i]);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[MultiTravel] Outbox: could not list pending submissions: " + ex.Message);
            }
        }

        private Task<SubmissionAttemptResult> RunTracked(Guid id, int maxAttempts, CancellationToken cancellationToken)
        {
            if (inFlight.TryGetValue(id, out var existing))
            {
                return existing;
            }

            var task = SubmitCoreAsync(id, maxAttempts, cancellationToken);
            if (!task.IsCompleted)
            {
                inFlight[id] = task;
            }

            return task;
        }

        private async Task<SubmissionAttemptResult> SubmitCoreAsync(Guid id, int maxAttempts, CancellationToken cancellationToken)
        {
            try
            {
                if (!TryGetPayload(id, out var payload, out var loadError))
                {
                    SubmissionFailed?.Invoke(id, loadError);
                    return SubmissionAttemptResult.Failed(loadError, 0);
                }

                int attempts = 0;
                BackendError lastError = null;

                for (int attempt = 0; attempt < Math.Max(1, maxAttempts); attempt++)
                {
                    if (attempt > 0)
                    {
                        try
                        {
                            await delay.Delay(TimeSpan.FromSeconds(BackoffSecondsForRetry(attempt - 1)), cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            lastError = BackendError.Cancelled();
                            break;
                        }
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        lastError = BackendError.Cancelled();
                        break;
                    }

                    attempts++;
                    BackendResult<SubmissionReceipt> result;
                    try
                    {
                        result = await backend.SubmitResultAsync(payload, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        lastError = BackendError.Cancelled();
                        break;
                    }
                    catch (Exception ex)
                    {
                        result = BackendResult<SubmissionReceipt>.Failure(BackendError.Transport(ex.GetType().Name + ": " + ex.Message));
                    }

                    if (result.Ok)
                    {
                        Acknowledge(id, payload, result.Value);
                        return SubmissionAttemptResult.Success(result.Value, attempts);
                    }

                    lastError = result.Error;
                    if (!lastError.IsTransient)
                    {
                        break;
                    }
                }

                if (lastError == null)
                {
                    lastError = BackendError.Transport("no attempt made");
                }

                Debug.LogWarning($"[MultiTravel] Outbox: submission {id} failed after {attempts} attempt(s): {lastError}");
                SubmissionFailed?.Invoke(id, lastError);
                return SubmissionAttemptResult.Failed(lastError, attempts);
            }
            finally
            {
                inFlight.Remove(id);
            }
        }

        private void Acknowledge(Guid id, SubmissionPayload payload, SubmissionReceipt receipt)
        {
            try
            {
                store.DeleteOutboxEntry(id);
            }
            catch (Exception ex)
            {
                // The file stays; the next sweep resubmits and receives a duplicate acknowledgement.
                Debug.LogWarning($"[MultiTravel] Outbox: could not delete acknowledged submission {id}: {ex.Message}");
            }

            pendingIds.Remove(id);
            payloadCache.Remove(id);
            AppendLogSafe(payload, true);
            SubmissionSucceeded?.Invoke(id, receipt);
        }

        private bool TryGetPayload(Guid id, out SubmissionPayload payload, out BackendError error)
        {
            error = null;
            if (payloadCache.TryGetValue(id, out payload))
            {
                return true;
            }

            string json;
            try
            {
                if (!store.TryLoadOutboxEntry(id, out json))
                {
                    pendingIds.Remove(id);
                    error = BackendError.OutboxCorrupt("entry not found: " + id);
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = BackendError.OutboxCorrupt("read failed: " + ex.Message);
                return false;
            }

            try
            {
                payload = BackendJson.Deserialize<SubmissionPayload>(json);
            }
            catch (JsonException ex)
            {
                payload = null;
                error = BackendError.OutboxCorrupt("invalid JSON: " + ex.Message);
                return false;
            }

            if (payload == null || payload.SubmissionId != id)
            {
                payload = null;
                error = BackendError.OutboxCorrupt("entry content does not match id " + id);
                return false;
            }

            payloadCache[id] = payload;
            return true;
        }

        private void AppendLogSafe(SubmissionPayload payload, bool submitted)
        {
            try
            {
                store.AppendResultLog(new ResultLogLine
                {
                    LoggedAtUtc = DateTime.UtcNow,
                    ClientSessionId = payload.ClientSessionId,
                    SubmissionId = payload.SubmissionId,
                    Score = payload.Score,
                    CompletionMs = payload.CompletionMs,
                    Gender = payload.Gender ?? string.Empty,
                    Status = payload.Status ?? WireFormats.StatusCompleted,
                    CompletionReason = payload.CompletionReason ?? string.Empty,
                    Submitted = submitted
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[MultiTravel] Outbox: could not append results log: " + ex.Message);
            }
        }
    }
}
