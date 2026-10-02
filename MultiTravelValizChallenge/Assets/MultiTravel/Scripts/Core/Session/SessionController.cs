using System;
using System.Threading.Tasks;
using MultiTravel.Core.Backend;
using MultiTravel.Core.Completion;
using MultiTravel.Core.Config;
using MultiTravel.Core.Outbox;
using MultiTravel.Core.Scoring;
using MultiTravel.Core.Timing;
using UnityEngine;

namespace MultiTravel.Core.Session
{
    /// <summary>
    /// The per-participant state machine (ARCHITECTURE.md §2.3). Every public transition validates the source state and
    /// throws <see cref="InvalidOperationException"/> otherwise. All members are main-thread only.
    /// <para>
    /// Flow: Welcome → Registration → GenderSelection → Instructions → Loading → (Countdown) → Playing → Completed →
    /// Submitting → Finished | SubmissionFailed → (Submitting ...). <see cref="AbandonSession"/> ends any non-Welcome
    /// session as Finished; <see cref="ResetForNextParticipant"/> returns to Welcome and raises
    /// <see cref="SessionResetRequested"/> so gameplay clears items, suitcase and HUD.
    /// </para>
    /// <para>
    /// Gameplay signals: <see cref="LoadingFinished"/> when the item set is ready and <see cref="CountdownFinished"/>
    /// when the VR countdown elapsed (skipped automatically when <c>CountdownSeconds</c> is 0).
    /// When a <see cref="SubmissionOutbox"/> is supplied, entering <see cref="SessionState.Submitting"/> enqueues the payload
    /// and runs the submission; otherwise the host calls <see cref="OnSubmissionSucceeded"/> / <see cref="OnSubmissionFailed"/>.
    /// </para>
    /// </summary>
    public sealed class SessionController
    {
        private readonly RuntimeConfig config;
        private readonly ScoreService scoreService;
        private readonly GameTimer timer;
        private readonly CompletionEvaluator completionEvaluator;
        private readonly SubmissionOutbox outbox;
        private readonly ILocalStore localStore;
        private readonly IBackendClient backendClient;

        public SessionController(
            RuntimeConfig config,
            ScoreService scoreService,
            GameTimer timer,
            CompletionEvaluator completionEvaluator,
            SubmissionOutbox outbox = null,
            ILocalStore localStore = null,
            IBackendClient backendClient = null)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.scoreService = scoreService ?? throw new ArgumentNullException(nameof(scoreService));
            this.timer = timer ?? throw new ArgumentNullException(nameof(timer));
            this.completionEvaluator = completionEvaluator ?? throw new ArgumentNullException(nameof(completionEvaluator));
            this.outbox = outbox;
            this.localStore = localStore;
            this.backendClient = backendClient;
        }

        public SessionState State { get; private set; } = SessionState.Welcome;

        /// <summary>The active participant; null in Welcome / Registration and after a reset.</summary>
        public ParticipantSession Current { get; private set; }

        /// <summary>Set while in <see cref="SessionState.Fatal"/>; cleared by <see cref="ResetForNextParticipant"/>.</summary>
        public FatalInfo Fatal { get; private set; }

        /// <summary>Length of the VR countdown in seconds (0 = no countdown state).</summary>
        public int CountdownSeconds => config.Gameplay.CountdownSeconds;

        /// <summary>Task of the current / last outbox submission run (completed task when none).</summary>
        public Task SubmissionTask { get; private set; } = Task.CompletedTask;

        /// <summary>Task of the current / last background registration (completed task when none).</summary>
        public Task RegistrationTask { get; private set; } = Task.CompletedTask;

        /// <summary>Raised after every transition with (previous, next).</summary>
        public event Action<SessionState, SessionState> StateChanged;

        /// <summary>Raised by <see cref="ResetForNextParticipant"/> before the Welcome transition; gameplay clears all per-participant state.</summary>
        public event Action SessionResetRequested;

        /// <summary>Raised by <see cref="ReportFatal"/> after the Fatal transition.</summary>
        public event Action<FatalInfo> FatalReported;

        /// <summary>Raised when the background registration after gender selection finishes (success or failure).</summary>
        public event Action<ParticipantSession, BackendResult<RegisterReceipt>> RegistrationCompleted;

        // ----- transitions -----

        /// <summary>Welcome → Registration.</summary>
        public void BeginRegistration()
        {
            Require(SessionState.Welcome, nameof(BeginRegistration));
            TransitionTo(SessionState.Registration);
        }

        /// <summary>
        /// Registration → GenderSelection when the input is valid; otherwise stays and returns the field errors.
        /// Consent is required only when the configured consent text is non-empty.
        /// </summary>
        public ValidationResult SubmitRegistration(ParticipantInput input)
        {
            Require(SessionState.Registration, nameof(SubmitRegistration));
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            bool consentRequired = config.Privacy.IsConsentRequired;
            var validation = ParticipantValidator.Validate(input, consentRequired);
            if (!validation.IsValid)
            {
                return validation;
            }

            var normalized = validation.Normalized;
            if (consentRequired)
            {
                normalized.ConsentAccepted = true;
                normalized.ConsentVersion = config.Privacy.ConsentVersion;
            }
            else
            {
                normalized.ConsentAccepted = null;
                normalized.ConsentVersion = null;
            }

            Current = new ParticipantSession(Guid.NewGuid(), normalized, DateTime.UtcNow);
            TransitionTo(SessionState.GenderSelection);
            return validation;
        }

        /// <summary>GenderSelection → Instructions. Starts a non-blocking background registration when a backend client is available.</summary>
        public void SelectGender(Gender gender)
        {
            Require(SessionState.GenderSelection, nameof(SelectGender));
            var session = RequireCurrent();
            session.Gender = gender;
            session.GenderSelected = true;
            TransitionTo(SessionState.Instructions);

            if (backendClient != null)
            {
                RegistrationTask = RegisterInBackgroundAsync(session);
            }
        }

        /// <summary>Instructions → Loading. The gameplay director prepares the item set and calls <see cref="LoadingFinished"/>.</summary>
        public void StartGame()
        {
            Require(SessionState.Instructions, nameof(StartGame));
            RequireCurrent();
            TransitionTo(SessionState.Loading);
        }

        /// <summary>Loading → Countdown (or directly Playing when the countdown is 0 s).</summary>
        public void LoadingFinished()
        {
            Require(SessionState.Loading, nameof(LoadingFinished));
            if (CountdownSeconds > 0)
            {
                TransitionTo(SessionState.Countdown);
            }
            else
            {
                EnterPlaying();
            }
        }

        /// <summary>Countdown → Playing; the timer starts in this call.</summary>
        public void CountdownFinished()
        {
            Require(SessionState.Countdown, nameof(CountdownFinished));
            EnterPlaying();
        }

        /// <summary>
        /// Playing → Completed → Submitting. Stops the timer first, builds the <see cref="GameResult"/> snapshot and,
        /// when an outbox is configured, enqueues and submits it.
        /// </summary>
        public GameResult CompleteGame(CompletionReason reason)
        {
            Require(SessionState.Playing, nameof(CompleteGame));
            var session = RequireCurrent();

            timer.Stop();
            var result = new GameResult(
                scoreService.Score,
                Math.Max(1L, timer.ElapsedMs),
                scoreService.PositiveCount,
                scoreService.NegativeCount,
                completionEvaluator.RequiredTotal,
                scoreService.SnapshotCountedProductIds(),
                DateTime.UtcNow,
                reason);

            session.Result = result;
            session.SubmissionId = Guid.NewGuid();
            session.Outcome = SessionOutcome.Completed;

            TransitionTo(SessionState.Completed);
            TransitionTo(SessionState.Submitting);
            DispatchSubmission(session);
            return result;
        }

        /// <summary>
        /// Convenience for per-frame polling: feeds the timer into the evaluator and completes the game when due.
        /// Returns true when a completion happened. Safe to call in any state.
        /// </summary>
        public bool TryCompleteIfDue()
        {
            if (State != SessionState.Playing)
            {
                return false;
            }

            completionEvaluator.NotifyElapsed(timer.ElapsedMs);
            if (!completionEvaluator.TryGetCompletion(out var reason))
            {
                return false;
            }

            CompleteGame(reason);
            return true;
        }

        /// <summary>Submitting → Finished.</summary>
        public void OnSubmissionSucceeded(SubmissionReceipt receipt)
        {
            Require(SessionState.Submitting, nameof(OnSubmissionSucceeded));
            if (receipt == null)
            {
                throw new ArgumentNullException(nameof(receipt));
            }

            var session = RequireCurrent();
            session.Receipt = receipt;
            session.ParticipantId = receipt.ParticipantId;
            session.LastSubmissionError = null;
            TransitionTo(SessionState.Finished);
        }

        /// <summary>Submitting → SubmissionFailed.</summary>
        public void OnSubmissionFailed(BackendError error)
        {
            Require(SessionState.Submitting, nameof(OnSubmissionFailed));
            var session = RequireCurrent();
            session.LastSubmissionError = error ?? BackendError.Transport("unknown");
            TransitionTo(SessionState.SubmissionFailed);
        }

        /// <summary>SubmissionFailed → Submitting (re-dispatches through the outbox when configured).</summary>
        public void RetrySubmission()
        {
            Require(SessionState.SubmissionFailed, nameof(RetrySubmission));
            var session = RequireCurrent();
            TransitionTo(SessionState.Submitting);
            DispatchSubmission(session);
        }

        /// <summary>
        /// Any state except Welcome and Finished → Finished with <see cref="SessionOutcome.Abandoned"/>. No leaderboard
        /// submission is started; a PII-free line is appended to the local results log when a session exists.
        /// A result already handed to the outbox stays pending there.
        /// </summary>
        public void AbandonSession()
        {
            if (State == SessionState.Welcome || State == SessionState.Finished)
            {
                throw new InvalidOperationException($"{nameof(AbandonSession)} is not valid in state {State}.");
            }

            timer.Stop();
            var session = Current;
            if (session != null)
            {
                session.Outcome = SessionOutcome.Abandoned;
                AppendAbandonLog(session);
            }

            TransitionTo(SessionState.Finished);
        }

        /// <summary>
        /// Finished / SubmissionFailed / Fatal → Welcome. Clears the participant, score, timer and evaluator progress,
        /// then raises <see cref="SessionResetRequested"/> (gameplay reset) before the Welcome transition.
        /// </summary>
        public void ResetForNextParticipant()
        {
            if (State != SessionState.Finished && State != SessionState.SubmissionFailed && State != SessionState.Fatal)
            {
                throw new InvalidOperationException($"{nameof(ResetForNextParticipant)} is not valid in state {State}.");
            }

            Current = null;
            Fatal = null;
            timer.Reset();
            scoreService.Reset();
            completionEvaluator.Reset();
            SessionResetRequested?.Invoke();
            TransitionTo(SessionState.Welcome);
        }

        /// <summary>Any state → Fatal. Stops the timer; the operator screen shows <see cref="FatalInfo.Message"/> and a retry.</summary>
        public void ReportFatal(FatalReason reason, string detail = null)
        {
            timer.Stop();
            Fatal = new FatalInfo(reason, detail, DateTime.UtcNow);
            Debug.LogWarning("[MultiTravel] Fatal: " + Fatal);
            TransitionTo(SessionState.Fatal);
            FatalReported?.Invoke(Fatal);
        }

        // ----- internals -----

        private void EnterPlaying()
        {
            var session = RequireCurrent();
            timer.Reset();
            timer.Start();
            session.StartedAtUtc = DateTime.UtcNow;
            TransitionTo(SessionState.Playing);
        }

        private void DispatchSubmission(ParticipantSession session)
        {
            if (outbox == null)
            {
                return;
            }

            SubmissionTask = SubmitThroughOutboxAsync(session);
        }

        private async Task SubmitThroughOutboxAsync(ParticipantSession session)
        {
            SubmissionAttemptResult attempt;
            try
            {
                var payload = SubmissionPayload.FromSession(session, config.Backend.StationId, config.ClientVersion);
                outbox.Enqueue(payload);
                attempt = await outbox.TrySubmitAsync(session.SubmissionId.Value);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[MultiTravel] Submission dispatch failed: " + ex.Message);
                attempt = SubmissionAttemptResult.Failed(BackendError.Transport(ex.GetType().Name + ": " + ex.Message), 0);
            }

            if (!ReferenceEquals(Current, session))
            {
                return;
            }

            session.SubmissionAttempts += attempt.Attempts;
            if (State != SessionState.Submitting)
            {
                return;
            }

            if (attempt.Succeeded)
            {
                OnSubmissionSucceeded(attempt.Receipt);
            }
            else
            {
                OnSubmissionFailed(attempt.Error);
            }
        }

        private async Task RegisterInBackgroundAsync(ParticipantSession session)
        {
            BackendResult<RegisterReceipt> result;
            try
            {
                result = await backendClient.RegisterAsync(session);
            }
            catch (Exception ex)
            {
                result = BackendResult<RegisterReceipt>.Failure(BackendError.Transport(ex.GetType().Name + ": " + ex.Message));
            }

            if (result.Ok)
            {
                if (ReferenceEquals(Current, session))
                {
                    session.ParticipantId = result.Value.ParticipantId;
                }
            }
            else
            {
                Debug.LogWarning("[MultiTravel] Background registration failed (submit_result will upsert the participant): " + result.Error);
            }

            RegistrationCompleted?.Invoke(session, result);
        }

        private void AppendAbandonLog(ParticipantSession session)
        {
            if (localStore == null)
            {
                return;
            }

            try
            {
                var result = session.Result;
                localStore.AppendResultLog(new ResultLogLine
                {
                    LoggedAtUtc = DateTime.UtcNow,
                    ClientSessionId = session.ClientSessionId,
                    SubmissionId = session.SubmissionId,
                    Score = result != null ? result.Score : scoreService.Score,
                    CompletionMs = result != null ? result.CompletionMs : timer.ElapsedMs,
                    Gender = session.GenderSelected ? WireFormats.GenderToWire(session.Gender) : string.Empty,
                    Status = WireFormats.StatusAbandoned,
                    CompletionReason = result != null ? WireFormats.CompletionReasonToWire(result.Reason) : string.Empty,
                    Submitted = false
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[MultiTravel] Could not append abandon log line: " + ex.Message);
            }
        }

        private void TransitionTo(SessionState next)
        {
            var previous = State;
            State = next;
            StateChanged?.Invoke(previous, next);
        }

        private void Require(SessionState expected, string operation)
        {
            if (State != expected)
            {
                throw new InvalidOperationException($"{operation} is only valid in state {expected} (current state: {State}).");
            }
        }

        private ParticipantSession RequireCurrent()
        {
            return Current ?? throw new InvalidOperationException("No active participant session.");
        }
    }
}
