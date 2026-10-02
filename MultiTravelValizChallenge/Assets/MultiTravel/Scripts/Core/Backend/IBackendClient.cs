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
    }
}
