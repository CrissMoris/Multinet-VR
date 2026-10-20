using System;
using System.Threading;
using System.Threading.Tasks;
using MultiTravel.Core.Leaderboard;
using MultiTravel.Core.Session;

namespace MultiTravel.Core.Backend
{
    /// <summary>
    /// Asynchronous, non-throwing backend API (ARCHITECTURE.md §4). Every call completes on the main thread and
    /// reports failures through <see cref="BackendResult{T}.Error"/>; implementations never throw for server or
    /// transport errors. Production implementation: <see cref="SupabaseBackendClient"/>.
    /// </summary>
    public interface IBackendClient
    {
        /// <summary>Registers (or re-registers, idempotently) the participant of a session whose gender was selected.</summary>
        Task<BackendResult<RegisterReceipt>> RegisterAsync(ParticipantSession session, CancellationToken cancellationToken = default);

        /// <summary>Submits a finalised result; a duplicate submission id is acknowledged with <c>created=false</c>.</summary>
        Task<BackendResult<SubmissionReceipt>> SubmitResultAsync(SubmissionPayload payload, CancellationToken cancellationToken = default);

        /// <summary>Fetches the top entries of the event leaderboard (no access code required).</summary>
        Task<BackendResult<LeaderboardEntry[]>> GetLeaderboardAsync(int limit, CancellationToken cancellationToken = default);

        /// <summary>Checks connectivity and event validity for the operator status bar.</summary>
        Task<BackendResult<PingReceipt>> PingAsync(CancellationToken cancellationToken = default);

        // ----- web registration queue (participants register on the registration web page) -----
        // Default implementations report "not configured" so test doubles need not implement the queue.

        /// <summary>Participants waiting in the web registration queue, oldest first (no contact data).</summary>
        Task<BackendResult<WaitingParticipant[]>> ListWaitingAsync(int limit, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(BackendResult<WaitingParticipant[]>.Failure(BackendError.ConfigurationMissing()));
        }

        /// <summary>Takes a waiting participant for this station (fails with QUEUE_NOT_WAITING when already taken).</summary>
        Task<BackendResult<ClaimedParticipant>> ClaimParticipantAsync(Guid participantId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(BackendResult<ClaimedParticipant>.Failure(BackendError.ConfigurationMissing()));
        }

        /// <summary>Puts a claimed participant back into the queue (cancelled before playing).</summary>
        Task<BackendResult<QueueAck>> ReleaseParticipantAsync(Guid participantId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(BackendResult<QueueAck>.Failure(BackendError.ConfigurationMissing()));
        }

        /// <summary>Removes a waiting participant from the queue (left / registered twice).</summary>
        Task<BackendResult<QueueAck>> RemoveFromQueueAsync(Guid participantId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(BackendResult<QueueAck>.Failure(BackendError.ConfigurationMissing()));
        }
    }
}
