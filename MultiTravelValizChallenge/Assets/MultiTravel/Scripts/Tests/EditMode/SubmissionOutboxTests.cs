using System;
using System.Collections.Generic;
using System.Threading;
using MultiTravel.Core.Backend;
using MultiTravel.Core.Outbox;
using MultiTravel.Tests.EditMode.Fakes;
using NUnit.Framework;

namespace MultiTravel.Tests.EditMode
{
    /// <summary>
    /// All fakes complete synchronously, so every outbox run finishes inside the call; <see cref="SyncTask"/> fails
    /// (instead of blocking) if that ever stops being true.
    /// </summary>
    public sealed class SubmissionOutboxTests
    {
        private InMemoryLocalStore store;
        private FakeBackendClient backend;
        private ImmediateDelay delay;

        [SetUp]
        public void SetUp()
        {
            store = new InMemoryLocalStore();
            backend = new FakeBackendClient();
            delay = new ImmediateDelay();
        }

        private SubmissionOutbox CreateOutbox(int maxAutoRetries = 5)
        {
            return new SubmissionOutbox(store, backend, TestData.Config(maxAutoRetries: maxAutoRetries), delay);
        }

        [Test]
        public void Enqueue_PersistsPayloadAndLogLine_BeforeFirstNetworkAttempt()
        {
            var outbox = CreateOutbox();
            var payload = TestData.Payload();
            bool fileExistedAtCall = false;
            int logLinesAtCall = -1;
            backend.OnSubmit = p =>
            {
                fileExistedAtCall = store.Entries.ContainsKey(p.SubmissionId);
                logLinesAtCall = store.LogLines.Count;
            };

            Assert.IsTrue(outbox.Enqueue(payload));
            Assert.IsTrue(store.Entries.ContainsKey(payload.SubmissionId), "file written by Enqueue");
            Assert.AreEqual(0, backend.SubmittedPayloads.Count, "Enqueue alone never hits the network");
            Assert.AreEqual(1, store.LogLines.Count);
            Assert.IsFalse(store.LogLines[0].Submitted);

            var result = SyncTask.Completed(outbox.TrySubmitAsync(payload.SubmissionId));

            Assert.IsTrue(result.Succeeded);
            Assert.IsTrue(fileExistedAtCall, "payload must be on disk before the first HTTP attempt");
            Assert.AreEqual(1, logLinesAtCall);
        }

        [Test]
        public void PersistedJson_RoundTripsThePayload()
        {
            var outbox = CreateOutbox();
            var payload = TestData.Payload(score: 42, completionMs: 9876);
            outbox.Enqueue(payload);

            var restored = BackendJson.Deserialize<SubmissionPayload>(store.Entries[payload.SubmissionId]);

            Assert.AreEqual(payload.SubmissionId, restored.SubmissionId);
            Assert.AreEqual(payload.ClientSessionId, restored.ClientSessionId);
            Assert.AreEqual(42, restored.Score);
            Assert.AreEqual(9876, restored.CompletionMs);
            CollectionAssert.AreEqual(payload.PlacedProductIds, restored.PlacedProductIds);
            Assert.AreEqual(TestData.PiiPhone, restored.Participant.Phone, "participant data is kept for the upsert in submit_result");
            Assert.AreEqual(payload.CompletedAtUtc, restored.CompletedAtUtc);
        }

        [Test]
        public void TransientFailures_RetryWithBackoff_ThenSuccessDeletesFile()
        {
            var outbox = CreateOutbox(maxAutoRetries: 5);
            var payload = TestData.Payload();
            backend.EnqueueSubmitFailure(FakeBackendClient.TransientError());
            backend.EnqueueSubmitFailure(BackendError.Timeout("timeout"));
            backend.EnqueueSubmitSuccess(created: true, rank: 3);
            Guid succeededId = Guid.Empty;
            outbox.SubmissionSucceeded += (id, _) => succeededId = id;

            outbox.Enqueue(payload);
            var result = SyncTask.Completed(outbox.TrySubmitAsync(payload.SubmissionId));

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(3, result.Attempts);
            Assert.AreEqual(3, result.Receipt.Rank);
            CollectionAssert.AreEqual(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) }, delay.Delays);
            Assert.IsFalse(store.Entries.ContainsKey(payload.SubmissionId), "file deleted after acknowledgement");
            Assert.AreEqual(0, outbox.PendingCount);
            Assert.AreEqual(payload.SubmissionId, succeededId);
            Assert.IsFalse(outbox.IsInFlight(payload.SubmissionId));
            Assert.IsTrue(store.LogLines[store.LogLines.Count - 1].Submitted, "acknowledgement logged");
        }

        [Test]
        public void PersistentTransientFailure_StopsAfterMaxAutoRetries_AndKeepsFile()
        {
            var outbox = CreateOutbox(maxAutoRetries: 3);
            backend.DefaultSubmitResult = BackendResult<SubmissionReceipt>.Failure(FakeBackendClient.TransientError());
            var payload = TestData.Payload();
            BackendError failedError = null;
            outbox.SubmissionFailed += (_, e) => failedError = e;

            outbox.Enqueue(payload);
            var result = SyncTask.Completed(outbox.TrySubmitAsync(payload.SubmissionId));

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(4, result.Attempts, "1 initial attempt + MaxAutoRetries");
            Assert.AreEqual(4, backend.SubmittedPayloads.Count);
            Assert.AreEqual(3, delay.Delays.Count);
            Assert.IsTrue(result.Error.IsTransient);
            Assert.IsNotNull(failedError);
            Assert.IsTrue(store.Entries.ContainsKey(payload.SubmissionId), "file kept for later retries");
            Assert.AreEqual(1, outbox.PendingCount);
            Assert.IsFalse(outbox.IsInFlight(payload.SubmissionId));
        }

        [Test]
        public void ZeroMaxAutoRetries_MakesExactlyOneAttempt_WithoutDelay()
        {
            var outbox = CreateOutbox(maxAutoRetries: 0);
            backend.DefaultSubmitResult = BackendResult<SubmissionReceipt>.Failure(FakeBackendClient.TransientError());
            var payload = TestData.Payload();

            outbox.Enqueue(payload);
            var result = SyncTask.Completed(outbox.TrySubmitAsync(payload.SubmissionId));

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(1, result.Attempts);
            Assert.AreEqual(0, delay.Delays.Count);
        }

        [Test]
        public void Backoff_FollowsScheduleAndCapsAt30Seconds()
        {
            var outbox = CreateOutbox(maxAutoRetries: 7);
            backend.DefaultSubmitResult = BackendResult<SubmissionReceipt>.Failure(BackendError.Transport("offline"));
            var payload = TestData.Payload();

            outbox.Enqueue(payload);
            var result = SyncTask.Completed(outbox.TrySubmitAsync(payload.SubmissionId));

            Assert.AreEqual(8, result.Attempts);
            var expected = new List<TimeSpan>();
            foreach (var seconds in new[] { 2, 4, 8, 16, 30, 30, 30 })
            {
                expected.Add(TimeSpan.FromSeconds(seconds));
            }

            CollectionAssert.AreEqual(expected, delay.Delays);
            Assert.AreEqual(30, SubmissionOutbox.BackoffSecondsForRetry(100));
            Assert.AreEqual(2, SubmissionOutbox.BackoffSecondsForRetry(-1));
        }

        [Test]
        public void NonTransientFailure_StopsImmediately_WithoutDeletingFile()
        {
            var outbox = CreateOutbox(maxAutoRetries: 5);
            backend.EnqueueSubmitFailure(FakeBackendClient.PermanentError());
            var payload = TestData.Payload();

            outbox.Enqueue(payload);
            var result = SyncTask.Completed(outbox.TrySubmitAsync(payload.SubmissionId));

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(1, result.Attempts);
            Assert.AreEqual(BackendError.CodeEventAccessDenied, result.Error.Code);
            Assert.IsFalse(result.Error.IsTransient);
            Assert.AreEqual(0, delay.Delays.Count);
            Assert.IsTrue(store.Entries.ContainsKey(payload.SubmissionId));
            Assert.AreEqual(0, store.DeleteCount);
            Assert.AreEqual(1, outbox.PendingCount);
        }

        [Test]
        public void DuplicateAcknowledgement_IsTreatedAsSuccess()
        {
            var outbox = CreateOutbox();
            backend.EnqueueSubmitSuccess(created: false, rank: 7);
            var payload = TestData.Payload();

            outbox.Enqueue(payload);
            var result = SyncTask.Completed(outbox.TrySubmitAsync(payload.SubmissionId));

            Assert.IsTrue(result.Succeeded);
            Assert.IsFalse(result.Receipt.Created);
            Assert.IsFalse(store.Entries.ContainsKey(payload.SubmissionId));
            Assert.AreEqual(0, outbox.PendingCount);
        }

        [Test]
        public void Enqueue_SameSubmissionIdTwice_WritesOnce()
        {
            var outbox = CreateOutbox();
            var payload = TestData.Payload();

            Assert.IsTrue(outbox.Enqueue(payload));
            Assert.IsFalse(outbox.Enqueue(payload));

            Assert.AreEqual(1, store.SaveCount);
            Assert.AreEqual(1, store.Entries.Count);
            Assert.AreEqual(1, store.LogLines.Count);
            Assert.AreEqual(1, outbox.PendingCount);
        }

        [Test]
        public void PendingCount_TracksEnqueueAndAcknowledgement()
        {
            var outbox = CreateOutbox(maxAutoRetries: 0);
            Assert.AreEqual(0, outbox.PendingCount);

            var first = TestData.Payload();
            var second = TestData.Payload();
            outbox.Enqueue(first);
            outbox.Enqueue(second);
            Assert.AreEqual(2, outbox.PendingCount);
            Assert.IsTrue(outbox.IsPending(first.SubmissionId));

            SyncTask.Completed(outbox.TrySubmitAsync(first.SubmissionId));
            Assert.AreEqual(1, outbox.PendingCount);
            Assert.IsFalse(outbox.IsPending(first.SubmissionId));
            Assert.IsTrue(outbox.IsPending(second.SubmissionId));
        }

        [Test]
        public void Constructor_PicksUpFilesAlreadyInStore()
        {
            var payload = TestData.Payload();
            store.SaveOutboxEntry(payload.SubmissionId, BackendJson.Serialize(payload));

            var outbox = CreateOutbox();

            Assert.AreEqual(1, outbox.PendingCount);
            Assert.IsTrue(outbox.IsPending(payload.SubmissionId));
        }

        [Test]
        public void RetryPendingAsync_SubmitsEveryFileFoundInStore_OneAttemptEach()
        {
            var outbox = CreateOutbox(maxAutoRetries: 5);
            var a = TestData.Payload(score: 1);
            var b = TestData.Payload(score: 2);
            var c = TestData.Payload(score: 3);

            // Written by a previous app run (not through this outbox instance's Enqueue).
            store.SaveOutboxEntry(a.SubmissionId, BackendJson.Serialize(a));
            store.SaveOutboxEntry(b.SubmissionId, BackendJson.Serialize(b));
            store.SaveOutboxEntry(c.SubmissionId, BackendJson.Serialize(c));

            backend.DefaultSubmitResult = FakeBackendClient.SuccessReceipt(true, 1);
            backend.OnSubmit = p =>
            {
                if (p.SubmissionId == b.SubmissionId)
                {
                    backend.EnqueueSubmitFailure(FakeBackendClient.TransientError());
                }
            };

            int succeeded = SyncTask.Completed(outbox.RetryPendingAsync());

            Assert.AreEqual(2, succeeded);
            Assert.AreEqual(3, backend.SubmittedPayloads.Count, "exactly one attempt per pending file");
            Assert.AreEqual(0, delay.Delays.Count, "background sweeps do not back off");
            Assert.AreEqual(1, outbox.PendingCount);
            Assert.IsTrue(outbox.IsPending(b.SubmissionId));
            Assert.IsTrue(store.Entries.ContainsKey(b.SubmissionId));
            Assert.IsFalse(store.Entries.ContainsKey(a.SubmissionId));
            Assert.IsFalse(store.Entries.ContainsKey(c.SubmissionId));

            var restoredScores = new List<int>();
            foreach (var submitted in backend.SubmittedPayloads)
            {
                restoredScores.Add(submitted.Score);
            }

            CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, restoredScores, "payload restored from the stored JSON");
        }

        [Test]
        public void RetryPendingAsync_CorruptFile_FailsWithoutNetworkCall_AndKeepsFile()
        {
            var id = Guid.NewGuid();
            store.SaveOutboxEntry(id, "{ not json");
            var outbox = CreateOutbox();
            BackendError failure = null;
            outbox.SubmissionFailed += (_, e) => failure = e;

            int succeeded = SyncTask.Completed(outbox.RetryPendingAsync());

            Assert.AreEqual(0, succeeded);
            Assert.AreEqual(0, backend.SubmittedPayloads.Count);
            Assert.IsNotNull(failure);
            Assert.AreEqual(BackendError.CodeOutboxCorrupt, failure.Code);
            Assert.IsTrue(store.Entries.ContainsKey(id), "corrupt files are never deleted silently");
        }

        [Test]
        public void TrySubmitAsync_UnknownId_FailsWithoutNetworkCall()
        {
            var outbox = CreateOutbox();

            var result = SyncTask.Completed(outbox.TrySubmitAsync(Guid.NewGuid()));

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(0, result.Attempts);
            Assert.AreEqual(BackendError.CodeOutboxCorrupt, result.Error.Code);
            Assert.AreEqual(0, backend.SubmittedPayloads.Count);
        }

        [Test]
        public void TrySubmitAsync_AlreadyCancelled_MakesNoAttempt_AndKeepsFile()
        {
            var outbox = CreateOutbox();
            var payload = TestData.Payload();
            outbox.Enqueue(payload);

            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                var result = SyncTask.Completed(outbox.TrySubmitAsync(payload.SubmissionId, cts.Token));

                Assert.IsFalse(result.Succeeded);
                Assert.AreEqual(BackendError.CodeCancelled, result.Error.Code);
                Assert.AreEqual(0, result.Attempts);
            }

            Assert.AreEqual(0, backend.SubmittedPayloads.Count);
            Assert.IsTrue(store.Entries.ContainsKey(payload.SubmissionId));
        }

        [Test]
        public void BackendThrowing_IsMappedToTransientError_AndLoopTerminates()
        {
            var throwing = new ThrowingBackendClient();
            var outbox = new SubmissionOutbox(store, throwing, TestData.Config(maxAutoRetries: 2), delay);
            var payload = TestData.Payload();

            outbox.Enqueue(payload);
            var result = SyncTask.Completed(outbox.TrySubmitAsync(payload.SubmissionId));

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(3, result.Attempts);
            Assert.AreEqual(3, throwing.Calls);
            Assert.AreEqual(BackendError.CodeTransport, result.Error.Code);
        }

        [Test]
        public void Enqueue_StoreThrows_StillPendingInMemory()
        {
            store.ThrowOnSave = true;
            var outbox = CreateOutbox();
            var payload = TestData.Payload();

            Assert.IsTrue(outbox.Enqueue(payload));
            Assert.AreEqual(1, outbox.PendingCount);

            var result = SyncTask.Completed(outbox.TrySubmitAsync(payload.SubmissionId));
            Assert.IsTrue(result.Succeeded, "cached payload is still submitted");
        }

        [Test]
        public void ResultsLog_ContainsNoPersonalData()
        {
            var outbox = CreateOutbox();
            var payload = TestData.Payload();
            outbox.Enqueue(payload);
            SyncTask.Completed(outbox.TrySubmitAsync(payload.SubmissionId));

            Assert.AreEqual(2, store.LogLines.Count, "one line on enqueue, one on acknowledgement");
            foreach (var line in store.LogLines)
            {
                var csv = line.ToCsv();
                AssertNoPii(csv);
                StringAssert.Contains(payload.SubmissionId.ToString("D"), csv);
                StringAssert.Contains(payload.ClientSessionId.ToString("D"), csv);
                StringAssert.Contains(",15,12345,female,completed,required_items_placed,", csv);
            }

            AssertNoPii(ResultLogLine.Header);
            StringAssert.EndsWith(",true", store.LogLines[1].ToCsv());
        }

        internal static void AssertNoPii(string text)
        {
            StringAssert.DoesNotContain(TestData.PiiPhone, text);
            StringAssert.DoesNotContain("5321234567", text);
            StringAssert.DoesNotContain(TestData.PiiEmail, text);
            StringAssert.DoesNotContain("@", text);
            StringAssert.DoesNotContain(TestData.PiiFirstName, text);
            StringAssert.DoesNotContain(TestData.PiiLastName, text);
            StringAssert.DoesNotContain("phone", text);
            StringAssert.DoesNotContain("email", text);
        }

        private sealed class ThrowingBackendClient : IBackendClient
        {
            public int Calls { get; private set; }

            public System.Threading.Tasks.Task<BackendResult<RegisterReceipt>> RegisterAsync(
                MultiTravel.Core.Session.ParticipantSession session, CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("not used");
            }

            public System.Threading.Tasks.Task<BackendResult<SubmissionReceipt>> SubmitResultAsync(
                SubmissionPayload payload, CancellationToken cancellationToken = default)
            {
                Calls++;
                throw new System.IO.IOException("simulated socket failure");
            }

            public System.Threading.Tasks.Task<BackendResult<MultiTravel.Core.Leaderboard.LeaderboardEntry[]>> GetLeaderboardAsync(
                int limit, CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("not used");
            }

            public System.Threading.Tasks.Task<BackendResult<PingReceipt>> PingAsync(CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("not used");
            }
        }
    }
}
