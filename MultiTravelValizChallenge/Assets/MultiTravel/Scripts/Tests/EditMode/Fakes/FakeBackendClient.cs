using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MultiTravel.Core.Backend;
using MultiTravel.Core.Leaderboard;
using MultiTravel.Core.Session;

namespace MultiTravel.Tests.EditMode.Fakes
{
    /// <summary>Scripted <see cref="IBackendClient"/> that completes synchronously.</summary>
    public sealed class FakeBackendClient : IBackendClient
    {
        private readonly Queue<BackendResult<SubmissionReceipt>> submitResults = new Queue<BackendResult<SubmissionReceipt>>();

        public List<SubmissionPayload> SubmittedPayloads { get; } = new List<SubmissionPayload>();

        public int RegisterCalls { get; private set; }

        /// <summary>Invoked at call time, before the scripted result is returned (lets tests inspect the store state).</summary>
        public Action<SubmissionPayload> OnSubmit { get; set; }

        /// <summary>Used when the submit queue is empty.</summary>
        public BackendResult<SubmissionReceipt> DefaultSubmitResult { get; set; } = SuccessReceipt(true, 1);

        public BackendResult<RegisterReceipt> RegisterResult { get; set; } =
            BackendResult<RegisterReceipt>.Success(new RegisterReceipt { ParticipantId = Guid.NewGuid(), Created = true });

        public BackendResult<LeaderboardEntry[]> LeaderboardResult { get; set; } =
            BackendResult<LeaderboardEntry[]>.Success(Array.Empty<LeaderboardEntry>());

        public BackendResult<PingReceipt> PingResult { get; set; } =
            BackendResult<PingReceipt>.Success(new PingReceipt { Ok = true, EventName = "Test" });

        public static BackendResult<SubmissionReceipt> SuccessReceipt(bool created, int? rank)
        {
            return BackendResult<SubmissionReceipt>.Success(new SubmissionReceipt
            {
                ResultId = Guid.NewGuid(),
                ParticipantId = Guid.NewGuid(),
                Created = created,
                Rank = rank
            });
        }

        public static BackendError TransientError()
        {
            return BackendError.FromServer(503, null, "service unavailable");
        }

        public static BackendError PermanentError()
        {
            return BackendError.FromServer(400, BackendError.CodeEventAccessDenied, "bad code");
        }

        public void EnqueueSubmit(BackendResult<SubmissionReceipt> result)
        {
            submitResults.Enqueue(result);
        }

        public void EnqueueSubmitSuccess(bool created = true, int? rank = 1)
        {
            submitResults.Enqueue(SuccessReceipt(created, rank));
        }

        public void EnqueueSubmitFailure(BackendError error)
        {
            submitResults.Enqueue(BackendResult<SubmissionReceipt>.Failure(error));
        }

        public Task<BackendResult<RegisterReceipt>> RegisterAsync(ParticipantSession session, CancellationToken cancellationToken = default)
        {
            RegisterCalls++;
            return Task.FromResult(RegisterResult);
        }

        public Task<BackendResult<SubmissionReceipt>> SubmitResultAsync(SubmissionPayload payload, CancellationToken cancellationToken = default)
        {
            SubmittedPayloads.Add(payload);
            OnSubmit?.Invoke(payload);
            var result = submitResults.Count > 0 ? submitResults.Dequeue() : DefaultSubmitResult;
            return Task.FromResult(result);
        }

        public Task<BackendResult<LeaderboardEntry[]>> GetLeaderboardAsync(int limit, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(LeaderboardResult);
        }

        public Task<BackendResult<PingReceipt>> PingAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(PingResult);
        }
    }
}
