using System;
using System.Collections.Generic;
using MultiTravel.Core.Backend;
using MultiTravel.Core.Completion;
using MultiTravel.Core.Config;
using MultiTravel.Core.Outbox;
using MultiTravel.Core.Products;
using MultiTravel.Core.Scoring;
using MultiTravel.Core.Session;
using MultiTravel.Core.Timing;
using MultiTravel.Tests.EditMode.Fakes;
using NUnit.Framework;

namespace MultiTravel.Tests.EditMode
{
    /// <summary>
    /// State machine tests (ARCHITECTURE.md §2.3). Outbox submissions run against synchronous fakes, so they complete
    /// inside <see cref="SessionController.CompleteGame"/> / <see cref="SessionController.RetrySubmission"/>.
    /// </summary>
    public sealed class SessionControllerTests
    {
        private ScriptableObjectFactory so;
        private ProductSet set;

        private ManualClock clock;
        private GameTimer timer;
        private ScoreService score;
        private CompletionEvaluator evaluator;
        private InMemoryLocalStore store;
        private FakeBackendClient backend;
        private ImmediateDelay delay;
        private SubmissionOutbox outbox;
        private SessionController controller;
        private List<string> events;

        [SetUp]
        public void SetUp()
        {
            so = new ScriptableObjectFactory();
            set = so.Set(
                Gender.Female,
                so.Product("laptop", true),
                so.Product("pen", true),
                so.Product("beach-towel", false));
        }

        [TearDown]
        public void TearDown()
        {
            so.DestroyAll();
        }

        /// <summary>Builds a fresh controller. <paramref name="withOutbox"/> wires store + outbox + backend.</summary>
        private SessionController Create(
            bool withOutbox = false,
            int countdownSeconds = 3,
            string consentText = "",
            string consentVersion = "1.0",
            int maxAutoRetries = 5,
            CompletionMode mode = CompletionMode.RequiredItemsPlaced,
            int timeLimitSeconds = 0)
        {
            var config = TestData.Config(
                maxAutoRetries: maxAutoRetries,
                countdownSeconds: countdownSeconds,
                consentText: consentText,
                consentVersion: consentVersion,
                completionMode: mode,
                timeLimitSeconds: timeLimitSeconds);

            clock = new ManualClock();
            timer = new GameTimer(clock);
            score = new ScoreService(config.Gameplay.RevertScoreOnRemoval);
            evaluator = new CompletionEvaluator(set, mode, timeLimitSeconds);
            store = new InMemoryLocalStore();
            backend = new FakeBackendClient();
            delay = new ImmediateDelay();
            outbox = withOutbox ? new SubmissionOutbox(store, backend, config, delay) : null;

            controller = withOutbox
                ? new SessionController(config, score, timer, evaluator, outbox, store, backend)
                : new SessionController(config, score, timer, evaluator);

            events = new List<string>();
            controller.StateChanged += (from, to) => events.Add(from + "->" + to);
            controller.SessionResetRequested += () => events.Add("reset-requested");
            return controller;
        }

        /// <summary>Drives a controller created by <see cref="Create"/> from Welcome to <paramref name="target"/>.</summary>
        private void DriveTo(SessionState target)
        {
            if (target == SessionState.Welcome)
            {
                return;
            }

            if (target == SessionState.Fatal)
            {
                DriveTo(SessionState.Playing);
                controller.ReportFatal(FatalReason.Unexpected, "test");
                return;
            }

            if (target == SessionState.Finished)
            {
                DriveTo(SessionState.Submitting);
                controller.OnSubmissionSucceeded(Receipt());
                return;
            }

            if (target == SessionState.SubmissionFailed)
            {
                DriveTo(SessionState.Submitting);
                controller.OnSubmissionFailed(FakeBackendClient.PermanentError());
                return;
            }

            controller.BeginRegistration();
            if (target == SessionState.Registration)
            {
                return;
            }

            Assert.IsTrue(controller.SubmitRegistration(TestData.ValidInput()).IsValid);
            if (target == SessionState.GenderSelection)
            {
                return;
            }

            controller.SelectGender(Gender.Female);
            if (target == SessionState.Instructions)
            {
                return;
            }

            controller.StartGame();
            if (target == SessionState.Loading)
            {
                return;
            }

            controller.LoadingFinished();
            if (target == SessionState.Countdown)
            {
                Assert.AreEqual(SessionState.Countdown, controller.State, "DriveTo(Countdown) needs CountdownSeconds > 0");
                return;
            }

            if (controller.State == SessionState.Countdown)
            {
                controller.CountdownFinished();
            }

            if (target == SessionState.Playing)
            {
                return;
            }

            controller.CompleteGame(CompletionReason.RequiredItemsPlaced);
            Assert.AreEqual(target, controller.State, "DriveTo could not reach " + target);
        }

        private static SubmissionReceipt Receipt(int? rank = 1)
        {
            return new SubmissionReceipt { ResultId = Guid.NewGuid(), ParticipantId = Guid.NewGuid(), Created = true, Rank = rank };
        }

        // ----- legal transitions -----

        [Test]
        public void HappyPath_AllLegalTransitionsInOrder()
        {
            Create(countdownSeconds: 3);
            Assert.AreEqual(SessionState.Welcome, controller.State);
            Assert.IsNull(controller.Current);

            controller.BeginRegistration();
            var validation = controller.SubmitRegistration(TestData.ValidInput());
            Assert.IsTrue(validation.IsValid);
            Assert.IsNotNull(controller.Current);
            Assert.AreNotEqual(Guid.Empty, controller.Current.ClientSessionId);
            Assert.AreEqual("Ayşe", controller.Current.Input.FirstName, "normalised input stored");
            Assert.AreEqual("+905321234567", controller.Current.Input.Phone);

            controller.SelectGender(Gender.Male);
            Assert.AreEqual(Gender.Male, controller.Current.Gender);
            Assert.IsTrue(controller.Current.GenderSelected);

            controller.StartGame();
            controller.LoadingFinished();
            controller.CountdownFinished();
            Assert.IsNotNull(controller.Current.StartedAtUtc);

            controller.CompleteGame(CompletionReason.ManualConfirm);
            Assert.AreEqual(SessionState.Submitting, controller.State, "without an outbox the host reports the outcome");

            controller.OnSubmissionFailed(FakeBackendClient.TransientError());
            controller.RetrySubmission();
            var receipt = Receipt(rank: 2);
            controller.OnSubmissionSucceeded(receipt);
            Assert.AreSame(receipt, controller.Current.Receipt);
            Assert.AreEqual(receipt.ParticipantId, controller.Current.ParticipantId);
            Assert.IsNull(controller.Current.LastSubmissionError);

            controller.ResetForNextParticipant();

            CollectionAssert.AreEqual(new[]
            {
                "Welcome->Registration",
                "Registration->GenderSelection",
                "GenderSelection->Instructions",
                "Instructions->Loading",
                "Loading->Countdown",
                "Countdown->Playing",
                "Playing->Completed",
                "Completed->Submitting",
                "Submitting->SubmissionFailed",
                "SubmissionFailed->Submitting",
                "Submitting->Finished",
                "reset-requested",
                "Finished->Welcome"
            }, events);
        }

        // ----- illegal transitions -----

        private static readonly string[] Operations =
        {
            "BeginRegistration", "SubmitRegistration", "SelectGender", "StartGame", "LoadingFinished",
            "CountdownFinished", "CompleteGame", "OnSubmissionSucceeded", "OnSubmissionFailed", "RetrySubmission",
            "AbandonSession", "ResetForNextParticipant"
        };

        private static bool IsLegal(string operation, SessionState state)
        {
            switch (operation)
            {
                case "BeginRegistration": return state == SessionState.Welcome;
                case "SubmitRegistration": return state == SessionState.Registration;
                case "SelectGender": return state == SessionState.GenderSelection;
                case "StartGame": return state == SessionState.Instructions;
                case "LoadingFinished": return state == SessionState.Loading;
                case "CountdownFinished": return state == SessionState.Countdown;
                case "CompleteGame": return state == SessionState.Playing;
                case "OnSubmissionSucceeded":
                case "OnSubmissionFailed": return state == SessionState.Submitting;
                case "RetrySubmission": return state == SessionState.SubmissionFailed;
                case "AbandonSession": return state != SessionState.Welcome && state != SessionState.Finished;
                case "ResetForNextParticipant":
                    return state == SessionState.Finished || state == SessionState.SubmissionFailed || state == SessionState.Fatal;
                default: throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
            }
        }

        private void Invoke(string operation)
        {
            switch (operation)
            {
                case "BeginRegistration": controller.BeginRegistration(); break;
                case "SubmitRegistration": controller.SubmitRegistration(TestData.ValidInput()); break;
                case "SelectGender": controller.SelectGender(Gender.Female); break;
                case "StartGame": controller.StartGame(); break;
                case "LoadingFinished": controller.LoadingFinished(); break;
                case "CountdownFinished": controller.CountdownFinished(); break;
                case "CompleteGame": controller.CompleteGame(CompletionReason.OperatorForced); break;
                case "OnSubmissionSucceeded": controller.OnSubmissionSucceeded(Receipt()); break;
                case "OnSubmissionFailed": controller.OnSubmissionFailed(FakeBackendClient.TransientError()); break;
                case "RetrySubmission": controller.RetrySubmission(); break;
                case "AbandonSession": controller.AbandonSession(); break;
                case "ResetForNextParticipant": controller.ResetForNextParticipant(); break;
                default: throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
            }
        }

        // Completed is transient (CompleteGame moves straight on to Submitting) so it cannot be observed from outside.
        [TestCase(SessionState.Welcome)]
        [TestCase(SessionState.Registration)]
        [TestCase(SessionState.GenderSelection)]
        [TestCase(SessionState.Instructions)]
        [TestCase(SessionState.Loading)]
        [TestCase(SessionState.Countdown)]
        [TestCase(SessionState.Playing)]
        [TestCase(SessionState.Submitting)]
        [TestCase(SessionState.SubmissionFailed)]
        [TestCase(SessionState.Finished)]
        [TestCase(SessionState.Fatal)]
        public void IllegalOperations_ThrowInvalidOperation_AndLeaveStateUnchanged(SessionState state)
        {
            int checkedCount = 0;
            foreach (var operation in Operations)
            {
                if (IsLegal(operation, state))
                {
                    continue;
                }

                Create(countdownSeconds: 3);
                DriveTo(state);
                Assert.AreEqual(state, controller.State);
                var current = controller.Current;
                int eventCount = events.Count;

                Assert.Throws<InvalidOperationException>(() => Invoke(operation), operation + " in " + state);

                Assert.AreEqual(state, controller.State, operation + " must not change the state");
                Assert.AreSame(current, controller.Current, operation + " must not touch the session");
                Assert.AreEqual(eventCount, events.Count, operation + " must not raise events");
                checkedCount++;
            }

            Assert.Greater(checkedCount, 0);
        }

        [TestCase(SessionState.Welcome)]
        [TestCase(SessionState.Registration)]
        [TestCase(SessionState.GenderSelection)]
        [TestCase(SessionState.Instructions)]
        [TestCase(SessionState.Loading)]
        [TestCase(SessionState.Countdown)]
        [TestCase(SessionState.Playing)]
        [TestCase(SessionState.Submitting)]
        [TestCase(SessionState.SubmissionFailed)]
        [TestCase(SessionState.Finished)]
        public void LegalOperations_DoNotThrow(SessionState state)
        {
            foreach (var operation in Operations)
            {
                if (!IsLegal(operation, state))
                {
                    continue;
                }

                Create(countdownSeconds: 3);
                DriveTo(state);
                Assert.DoesNotThrow(() => Invoke(operation), operation + " in " + state);
                Assert.AreNotEqual(state, controller.State, operation + " must leave " + state);
            }
        }

        // ----- registration -----

        [Test]
        public void SubmitRegistration_InvalidInput_StaysInRegistration()
        {
            Create();
            controller.BeginRegistration();
            int eventCount = events.Count;

            var input = TestData.ValidInput();
            input.FirstName = "   ";
            input.Email = "not-an-email";
            var result = controller.SubmitRegistration(input);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.TryGetError(ParticipantValidator.FieldFirstName, out _));
            Assert.IsTrue(result.TryGetError(ParticipantValidator.FieldEmail, out _));
            Assert.AreEqual(SessionState.Registration, controller.State);
            Assert.IsNull(controller.Current);
            Assert.AreEqual(eventCount, events.Count);

            Assert.IsTrue(controller.SubmitRegistration(TestData.ValidInput()).IsValid, "can retry after fixing the form");
            Assert.AreEqual(SessionState.GenderSelection, controller.State);
        }

        [Test]
        public void SubmitRegistration_Null_Throws()
        {
            Create();
            controller.BeginRegistration();
            Assert.Throws<ArgumentNullException>(() => controller.SubmitRegistration(null));
            Assert.AreEqual(SessionState.Registration, controller.State);
        }

        [Test]
        public void Consent_NotRequired_WhenConsentTextEmpty()
        {
            Create(consentText: "   ");
            controller.BeginRegistration();
            var input = TestData.ValidInput();
            input.ConsentAccepted = false;
            input.ConsentVersion = "whatever";

            var result = controller.SubmitRegistration(input);

            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(SessionState.GenderSelection, controller.State);
            Assert.IsNull(controller.Current.Input.ConsentAccepted, "consent not collected");
            Assert.IsNull(controller.Current.Input.ConsentVersion);
        }

        [Test]
        public void Consent_Required_WhenConsentTextPresent()
        {
            Create(consentText: "KVKK aydınlatma metni", consentVersion: "2026-01");
            controller.BeginRegistration();

            var unticked = TestData.ValidInput();
            unticked.ConsentAccepted = false;
            var rejected = controller.SubmitRegistration(unticked);
            Assert.IsFalse(rejected.IsValid);
            Assert.IsTrue(rejected.TryGetError(ParticipantValidator.FieldConsent, out _));
            Assert.AreEqual(SessionState.Registration, controller.State);

            var missing = TestData.ValidInput();
            missing.ConsentAccepted = null;
            Assert.IsFalse(controller.SubmitRegistration(missing).IsValid);
            Assert.AreEqual(SessionState.Registration, controller.State);

            var accepted = controller.SubmitRegistration(TestData.ValidInput());
            Assert.IsTrue(accepted.IsValid);
            Assert.AreEqual(SessionState.GenderSelection, controller.State);
            Assert.AreEqual(true, controller.Current.Input.ConsentAccepted);
            Assert.AreEqual("2026-01", controller.Current.Input.ConsentVersion, "version comes from configuration");
        }

        [Test]
        public void SelectGender_WithBackend_RegistersInBackground()
        {
            Create(withOutbox: true);
            var participantId = Guid.NewGuid();
            backend.RegisterResult = BackendResult<RegisterReceipt>.Success(new RegisterReceipt { ParticipantId = participantId, Created = true });
            ParticipantSession reported = null;
            controller.RegistrationCompleted += (s, r) => reported = s;

            DriveTo(SessionState.Instructions);

            SyncTask.Completed(controller.RegistrationTask);
            Assert.AreEqual(1, backend.RegisterCalls);
            Assert.AreEqual(participantId, controller.Current.ParticipantId);
            Assert.AreSame(controller.Current, reported);
            Assert.AreEqual(SessionState.Instructions, controller.State, "registration never blocks the flow");
        }

        [Test]
        public void SelectGender_BackgroundRegistrationFailure_DoesNotChangeState()
        {
            Create(withOutbox: true);
            backend.RegisterResult = BackendResult<RegisterReceipt>.Failure(FakeBackendClient.TransientError());

            DriveTo(SessionState.Instructions);

            SyncTask.Completed(controller.RegistrationTask);
            Assert.IsNull(controller.Current.ParticipantId);
            Assert.AreEqual(SessionState.Instructions, controller.State);
        }

        // ----- countdown / timer -----

        [Test]
        public void ZeroCountdown_SkipsCountdownState()
        {
            Create(countdownSeconds: 0);
            Assert.AreEqual(0, controller.CountdownSeconds);
            DriveTo(SessionState.Loading);

            controller.LoadingFinished();

            Assert.AreEqual(SessionState.Playing, controller.State);
            Assert.IsTrue(timer.IsRunning);
            CollectionAssert.DoesNotContain(events, "Loading->Countdown");
            CollectionAssert.Contains(events, "Loading->Playing");
            Assert.Throws<InvalidOperationException>(() => controller.CountdownFinished());
        }

        [Test]
        public void Timer_StartsOnPlaying_AndStopsOnCompleteGame()
        {
            Create(countdownSeconds: 3);
            DriveTo(SessionState.Countdown);
            clock.AdvanceMs(3000);
            Assert.IsFalse(timer.IsRunning, "timer does not run during the countdown");
            Assert.AreEqual(0, timer.ElapsedMs);

            controller.CountdownFinished();
            Assert.IsTrue(timer.IsRunning);
            Assert.AreEqual(0, timer.ElapsedMs, "timer starts exactly when Playing is entered");

            clock.AdvanceMs(12345);
            Assert.AreEqual(12345, timer.ElapsedMs);
            var result = controller.CompleteGame(CompletionReason.RequiredItemsPlaced);

            Assert.IsFalse(timer.IsRunning);
            Assert.AreEqual(12345, result.CompletionMs);
            clock.AdvanceMs(5000);
            Assert.AreEqual(12345, timer.ElapsedMs, "value frozen after completion");
        }

        [Test]
        public void CompleteGame_ZeroElapsed_ReportsAtLeastOneMillisecond()
        {
            Create(countdownSeconds: 0);
            DriveTo(SessionState.Playing);

            var result = controller.CompleteGame(CompletionReason.OperatorForced);

            Assert.AreEqual(1, result.CompletionMs, "backend rejects completion_ms = 0");
        }

        // ----- result -----

        [Test]
        public void CompleteGame_BuildsResultSnapshot()
        {
            Create(countdownSeconds: 0);
            DriveTo(SessionState.Playing);
            score.TryApplyPlacement("laptop", 10);
            evaluator.NotifyPlaced("laptop");
            score.TryApplyPlacement("beach-towel", -5);
            evaluator.NotifyPlaced("beach-towel");
            score.TryApplyPlacement("pen", 10);
            evaluator.NotifyPlaced("pen");
            clock.AdvanceMs(45678);
            var before = DateTime.UtcNow;

            var result = controller.CompleteGame(CompletionReason.OperatorForced);

            var session = controller.Current;
            Assert.AreSame(result, session.Result);
            Assert.AreEqual(15, result.Score);
            Assert.AreEqual(45678, result.CompletionMs);
            Assert.AreEqual(2, result.CorrectCount);
            Assert.AreEqual(1, result.IncorrectCount);
            Assert.AreEqual(2, result.RequiredTotal);
            CollectionAssert.AreEqual(new[] { "laptop", "beach-towel", "pen" }, result.PlacedProductIds);
            Assert.AreEqual(CompletionReason.OperatorForced, result.Reason);
            Assert.AreEqual(DateTimeKind.Utc, result.CompletedAtUtc.Kind);
            Assert.GreaterOrEqual(result.CompletedAtUtc, before);
            Assert.IsTrue(session.SubmissionId.HasValue);
            Assert.AreNotEqual(Guid.Empty, session.SubmissionId.Value);
            Assert.AreEqual(SessionOutcome.Completed, session.Outcome);
            Assert.IsTrue(session.IsCompleted);

            score.TryApplyPlacement("late-item", 100);
            Assert.AreEqual(15, result.Score, "snapshot is immutable");
            Assert.AreEqual(3, result.PlacedProductIds.Length);
        }

        [Test]
        public void TryCompleteIfDue_CompletesOnTimeLimit()
        {
            Create(countdownSeconds: 0, timeLimitSeconds: 10);
            Assert.IsFalse(controller.TryCompleteIfDue(), "not playing yet");
            DriveTo(SessionState.Playing);

            clock.AdvanceMs(9999);
            Assert.IsFalse(controller.TryCompleteIfDue());
            clock.AdvanceMs(1);
            Assert.IsTrue(controller.TryCompleteIfDue());

            Assert.AreEqual(CompletionReason.TimeLimit, controller.Current.Result.Reason);
            Assert.AreEqual(10000, controller.Current.Result.CompletionMs);
            Assert.AreEqual(SessionState.Submitting, controller.State);
            Assert.IsFalse(controller.TryCompleteIfDue(), "safe to call in any state");
        }

        // ----- submission through the outbox -----

        [Test]
        public void Outbox_Success_FinishesWithReceipt()
        {
            Create(withOutbox: true, countdownSeconds: 0);
            backend.EnqueueSubmitSuccess(created: true, rank: 5);
            DriveTo(SessionState.Playing);
            score.TryApplyPlacement("laptop", 10);
            clock.AdvanceMs(2500);

            var result = controller.CompleteGame(CompletionReason.RequiredItemsPlaced);
            SyncTask.Completed(controller.SubmissionTask);

            Assert.AreEqual(SessionState.Finished, controller.State);
            var session = controller.Current;
            Assert.IsNotNull(session.Receipt);
            Assert.AreEqual(5, session.Receipt.Rank);
            Assert.AreEqual(session.Receipt.ParticipantId, session.ParticipantId);
            Assert.AreEqual(1, session.SubmissionAttempts);
            Assert.IsNull(session.LastSubmissionError);
            Assert.AreEqual(0, outbox.PendingCount);
            Assert.AreEqual(0, store.Entries.Count, "file deleted after acknowledgement");

            Assert.AreEqual(1, backend.SubmittedPayloads.Count);
            var payload = backend.SubmittedPayloads[0];
            Assert.AreEqual(session.SubmissionId.Value, payload.SubmissionId);
            Assert.AreEqual(session.ClientSessionId, payload.ClientSessionId);
            Assert.AreEqual(result.Score, payload.Score);
            Assert.AreEqual(2500, payload.CompletionMs);
            Assert.AreEqual(TestData.StationId, payload.StationId);
            Assert.AreEqual(TestData.ClientVersion, payload.ClientVersion);
            Assert.AreEqual("female", payload.Gender);
            Assert.AreEqual("completed", payload.Status);
            Assert.AreEqual("required_items_placed", payload.CompletionReason);
            Assert.AreEqual("+905321234567", payload.Participant.Phone);

            CollectionAssert.IsSubsetOf(new[] { "Playing->Completed", "Completed->Submitting", "Submitting->Finished" }, events);
        }

        [Test]
        public void Outbox_Failure_ThenRetry_Finishes()
        {
            Create(withOutbox: true, countdownSeconds: 0);
            backend.EnqueueSubmitFailure(FakeBackendClient.PermanentError());
            DriveTo(SessionState.Playing);

            controller.CompleteGame(CompletionReason.RequiredItemsPlaced);
            SyncTask.Completed(controller.SubmissionTask);

            Assert.AreEqual(SessionState.SubmissionFailed, controller.State);
            var session = controller.Current;
            Assert.IsNotNull(session.LastSubmissionError);
            Assert.AreEqual(BackendError.CodeEventAccessDenied, session.LastSubmissionError.Code);
            Assert.AreEqual(1, session.SubmissionAttempts);
            Assert.AreEqual(1, outbox.PendingCount);
            Assert.AreEqual(1, store.Entries.Count, "payload kept on disk");

            backend.EnqueueSubmitSuccess(created: true, rank: 1);
            controller.RetrySubmission();
            SyncTask.Completed(controller.SubmissionTask);

            Assert.AreEqual(SessionState.Finished, controller.State);
            Assert.IsNotNull(session.Receipt);
            Assert.IsNull(session.LastSubmissionError);
            Assert.AreEqual(2, session.SubmissionAttempts);
            Assert.AreEqual(0, outbox.PendingCount);
            Assert.AreEqual(0, store.Entries.Count);
            Assert.AreEqual(1, store.SaveCount, "retry does not write a second outbox file");
            Assert.AreEqual(2, backend.SubmittedPayloads.Count);
            Assert.AreEqual(backend.SubmittedPayloads[0].SubmissionId, backend.SubmittedPayloads[1].SubmissionId, "same idempotency key");
        }

        [Test]
        public void Outbox_TransientFailures_ExhaustRetries_ThenSubmissionFailed()
        {
            Create(withOutbox: true, countdownSeconds: 0, maxAutoRetries: 2);
            backend.DefaultSubmitResult = BackendResult<SubmissionReceipt>.Failure(FakeBackendClient.TransientError());
            DriveTo(SessionState.Playing);

            controller.CompleteGame(CompletionReason.RequiredItemsPlaced);
            SyncTask.Completed(controller.SubmissionTask);

            Assert.AreEqual(SessionState.SubmissionFailed, controller.State);
            Assert.AreEqual(3, controller.Current.SubmissionAttempts);
            Assert.AreEqual(2, delay.Delays.Count);
            Assert.IsTrue(controller.Current.LastSubmissionError.IsTransient);
            Assert.AreEqual(BackendErrorMessages.ServerUnreachable, controller.Current.LastSubmissionError.Message);
        }

        // ----- abandon -----

        [Test]
        public void AbandonSession_FromPlaying_FinishesAbandoned_WithoutSubmission()
        {
            Create(withOutbox: true, countdownSeconds: 0);
            DriveTo(SessionState.Playing);
            score.TryApplyPlacement("laptop", 10);
            clock.AdvanceMs(4000);

            controller.AbandonSession();

            Assert.AreEqual(SessionState.Finished, controller.State);
            Assert.AreEqual(SessionOutcome.Abandoned, controller.Current.Outcome);
            Assert.IsFalse(timer.IsRunning);
            Assert.IsNull(controller.Current.Result);
            Assert.IsNull(controller.Current.Receipt);
            Assert.AreEqual(0, backend.SubmittedPayloads.Count, "no leaderboard submission");
            Assert.AreEqual(0, store.Entries.Count, "nothing queued in the outbox");
            Assert.AreEqual(0, outbox.PendingCount);

            Assert.AreEqual(1, store.LogLines.Count, "PII-free local log line");
            var line = store.LogLines[0];
            Assert.AreEqual(WireFormats.StatusAbandoned, line.Status);
            Assert.IsFalse(line.Submitted);
            Assert.AreEqual(controller.Current.ClientSessionId, line.ClientSessionId);
            Assert.AreEqual(10, line.Score);
            Assert.AreEqual(4000, line.CompletionMs);
            SubmissionOutboxTests.AssertNoPii(line.ToCsv());
        }

        [Test]
        public void AbandonSession_BeforeParticipantExists_FinishesWithoutLogLine()
        {
            Create(withOutbox: true);
            DriveTo(SessionState.Registration);

            controller.AbandonSession();

            Assert.AreEqual(SessionState.Finished, controller.State);
            Assert.IsNull(controller.Current);
            Assert.AreEqual(0, store.LogLines.Count);
        }

        [Test]
        public void AbandonSession_FromSubmissionFailed_KeepsResultPendingInOutbox()
        {
            Create(withOutbox: true, countdownSeconds: 0);
            backend.EnqueueSubmitFailure(FakeBackendClient.PermanentError());
            DriveTo(SessionState.Playing);
            controller.CompleteGame(CompletionReason.RequiredItemsPlaced);
            Assert.AreEqual(SessionState.SubmissionFailed, controller.State);

            controller.AbandonSession();

            Assert.AreEqual(SessionState.Finished, controller.State);
            Assert.AreEqual(SessionOutcome.Abandoned, controller.Current.Outcome);
            Assert.AreEqual(1, outbox.PendingCount, "result already handed to the outbox stays pending");
            Assert.AreEqual(1, backend.SubmittedPayloads.Count, "abandon does not trigger another submission");
        }

        // ----- reset -----

        [Test]
        public void ResetForNextParticipant_ClearsEverything_AndRaisesResetBeforeWelcome()
        {
            Create(withOutbox: true, countdownSeconds: 0);
            DriveTo(SessionState.Playing);
            score.TryApplyPlacement("laptop", 10);
            evaluator.NotifyPlaced("laptop");
            clock.AdvanceMs(3000);
            evaluator.NotifyElapsed(timer.ElapsedMs);
            controller.CompleteGame(CompletionReason.OperatorForced);
            Assert.AreEqual(SessionState.Finished, controller.State);
            var firstSessionId = controller.Current.ClientSessionId;

            SessionState stateDuringReset = SessionState.Fatal;
            ParticipantSession currentDuringReset = controller.Current;
            int scoreDuringReset = -1;
            controller.SessionResetRequested += () =>
            {
                stateDuringReset = controller.State;
                currentDuringReset = controller.Current;
                scoreDuringReset = score.Score;
            };
            events.Clear();

            controller.ResetForNextParticipant();

            CollectionAssert.AreEqual(new[] { "reset-requested", "Finished->Welcome" }, events);
            Assert.AreEqual(SessionState.Finished, stateDuringReset, "raised before the Welcome transition");
            Assert.IsNull(currentDuringReset, "participant already cleared when gameplay resets");
            Assert.AreEqual(0, scoreDuringReset);

            Assert.AreEqual(SessionState.Welcome, controller.State);
            Assert.IsNull(controller.Current);
            Assert.IsNull(controller.Fatal);
            Assert.AreEqual(0, score.Score);
            Assert.AreEqual(0, score.CountedProductIds.Count);
            Assert.AreEqual(0, score.PositiveCount);
            Assert.AreEqual(0, timer.ElapsedMs);
            Assert.IsFalse(timer.IsRunning);
            Assert.AreEqual(0, evaluator.RequiredPlacedCount);
            Assert.AreEqual(0, evaluator.ElapsedMs);

            // A second participant starts from a clean slate.
            DriveTo(SessionState.Playing);
            Assert.AreNotEqual(firstSessionId, controller.Current.ClientSessionId);
            Assert.IsNull(controller.Current.Result);
            Assert.AreEqual(0, timer.ElapsedMs);
        }

        [Test]
        public void ResetForNextParticipant_FromSubmissionFailed_ReturnsToWelcome()
        {
            Create(countdownSeconds: 0);
            DriveTo(SessionState.SubmissionFailed);

            controller.ResetForNextParticipant();

            Assert.AreEqual(SessionState.Welcome, controller.State);
            Assert.IsNull(controller.Current);
        }

        // ----- fatal -----

        [TestCase(SessionState.Welcome)]
        [TestCase(SessionState.Registration)]
        [TestCase(SessionState.GenderSelection)]
        [TestCase(SessionState.Instructions)]
        [TestCase(SessionState.Loading)]
        [TestCase(SessionState.Countdown)]
        [TestCase(SessionState.Playing)]
        [TestCase(SessionState.Submitting)]
        [TestCase(SessionState.SubmissionFailed)]
        [TestCase(SessionState.Finished)]
        [TestCase(SessionState.Fatal)]
        public void ReportFatal_FromAnyState_ThenReset(SessionState state)
        {
            Create(countdownSeconds: 3);
            DriveTo(state);
            FatalInfo reported = null;
            controller.FatalReported += info => reported = info;

            controller.ReportFatal(FatalReason.XrInitializationFailed, "OpenXR runtime missing");

            Assert.AreEqual(SessionState.Fatal, controller.State);
            Assert.IsNotNull(controller.Fatal);
            Assert.AreSame(controller.Fatal, reported);
            Assert.AreEqual(FatalReason.XrInitializationFailed, controller.Fatal.Reason);
            Assert.AreEqual(FatalInfo.MessageFor(FatalReason.XrInitializationFailed), controller.Fatal.Message);
            Assert.AreEqual("OpenXR runtime missing", controller.Fatal.Detail);
            Assert.IsFalse(timer.IsRunning);

            controller.ResetForNextParticipant();

            Assert.AreEqual(SessionState.Welcome, controller.State);
            Assert.IsNull(controller.Fatal);
            Assert.IsNull(controller.Current);
            Assert.AreEqual(0, timer.ElapsedMs);
            Assert.AreEqual(0, score.Score);
        }
    }
}
