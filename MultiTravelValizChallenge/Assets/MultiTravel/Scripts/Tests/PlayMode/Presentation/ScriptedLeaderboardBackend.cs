using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MultiTravel.Core.Backend;
using MultiTravel.Core.Leaderboard;
using MultiTravel.Core.Session;

namespace MultiTravel.Tests.PlayMode.Presentation
{
    /// <summary>
    /// Backend fake whose leaderboard requests stay pending until the test completes them, so stale-response handling
    /// can be exercised deterministically. Every other call succeeds immediately.
    /// </summary>
    public sealed class ScriptedLeaderboardBackend : IBackendClient
    {
        private readonly List<TaskCompletionSource<BackendResult<LeaderboardEntry[]>>> requests =
            new List<TaskCompletionSource<BackendResult<LeaderboardEntry[]>>>();

        /// <summary>Number of leaderboard requests received so far.</summary>
        public int RequestCount => requests.Count;

        /// <summary>Limit passed with the last leaderboard request.</summary>
        public int LastLimit { get; private set; }

        /// <summary>Completes request <paramref name="index"/> (0-based, in arrival order) with entries.</summary>
        public void Complete(int index, LeaderboardEntry[] entries)
        {
            requests[index].TrySetResult(BackendResult<LeaderboardEntry[]>.Success(entries));
        }

        /// <summary>Fails request <paramref name="index"/> with a transport error.</summary>
        public void Fail(int index)
        {
            requests[index].TrySetResult(BackendResult<LeaderboardEntry[]>.Failure(BackendError.Transport("scripted failure")));
        }

        public static LeaderboardEntry Entry(int rank, string name, int score, long ms)
        {
            return new LeaderboardEntry { Rank = rank, DisplayName = name, Score = score, CompletionMs = ms, Gender = "female", CompletedAt = DateTime.UtcNow };
        }

        public Task<BackendResult<RegisterReceipt>> RegisterAsync(ParticipantSession session, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(BackendResult<RegisterReceipt>.Success(new RegisterReceipt { ParticipantId = Guid.NewGuid(), Created = true }));
        }

        public Task<BackendResult<SubmissionReceipt>> SubmitResultAsync(SubmissionPayload payload, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(BackendResult<SubmissionReceipt>.Success(new SubmissionReceipt
            {
                ResultId = Guid.NewGuid(),
                ParticipantId = Guid.NewGuid(),
                Created = true,
                Rank = 1
            }));
        }

        public Task<BackendResult<LeaderboardEntry[]>> GetLeaderboardAsync(int limit, CancellationToken cancellationToken = default)
        {
            LastLimit = limit;
            var source = new TaskCompletionSource<BackendResult<LeaderboardEntry[]>>();
            requests.Add(source);
            return source.Task;
        }

        public Task<BackendResult<PingReceipt>> PingAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(BackendResult<PingReceipt>.Success(new PingReceipt { Ok = true, EventName = "Test", ServerTime = DateTime.UtcNow }));
        }
    }
}
