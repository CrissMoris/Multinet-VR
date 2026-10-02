using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MultiTravel.Core.Backend;
using MultiTravel.Core.Leaderboard;
using MultiTravel.Core.Session;

namespace MultiTravel.Tests.PlayMode
{
    /// <summary>In-memory backend for assembled-game tests: accepts everything and records what it was sent.</summary>
    public sealed class RecordingBackend : IBackendClient
    {
        public List<SubmissionPayload> Submissions { get; } = new List<SubmissionPayload>();

        public int Registrations { get; private set; }

        public Task<BackendResult<RegisterReceipt>> RegisterAsync(ParticipantSession session, CancellationToken cancellationToken = default)
        {
            Registrations++;
            return Task.FromResult(BackendResult<RegisterReceipt>.Success(new RegisterReceipt { ParticipantId = Guid.NewGuid(), Created = true }));
        }

        public Task<BackendResult<SubmissionReceipt>> SubmitResultAsync(SubmissionPayload payload, CancellationToken cancellationToken = default)
        {
            Submissions.Add(payload);
            return Task.FromResult(BackendResult<SubmissionReceipt>.Success(new SubmissionReceipt
            {
                ResultId = Guid.NewGuid(),
                ParticipantId = Guid.NewGuid(),
                Created = true,
                Rank = Submissions.Count
            }));
        }

        public Task<BackendResult<LeaderboardEntry[]>> GetLeaderboardAsync(int limit, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(BackendResult<LeaderboardEntry[]>.Success(Array.Empty<LeaderboardEntry>()));
        }

        public Task<BackendResult<PingReceipt>> PingAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(BackendResult<PingReceipt>.Success(new PingReceipt { Ok = true, EventName = "Test", ServerTime = DateTime.UtcNow }));
        }
    }
}
