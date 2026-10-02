using System;
using MultiTravel.Core.Backend;
using MultiTravel.Core.Completion;
using MultiTravel.Core.Config;
using MultiTravel.Core.Outbox;
using MultiTravel.Core.Scoring;
using MultiTravel.Core.Session;
using MultiTravel.Core.Timing;
using MultiTravel.Core.Xr;
using UnityEngine;

namespace MultiTravel.Operator
{
    /// <summary>
    /// Services shared by the operator panels plus guarded command helpers. Created by <see cref="OperatorScreen"/> in
    /// <c>Awake</c> (unbound) and bound in <c>Start</c> once <c>AppServices</c> resolves. Panels only read services
    /// after binding. <see cref="Epoch"/> increases every time a new participant cycle begins (state Welcome), so any
    /// asynchronous continuation can detect that the session it belonged to is gone.
    /// </summary>
    public sealed class OperatorContext
    {
        private const string LogPrefix = "[MultiTravel.Operator] ";

        /// <summary>True once every required service was resolved.</summary>
        public bool IsBound { get; private set; }

        public SessionController Session { get; private set; }

        public RuntimeConfig Config { get; private set; }

        public ScoreService Score { get; private set; }

        public GameTimer Timer { get; private set; }

        public CompletionEvaluator Completion { get; private set; }

        /// <summary>Optional: null when the outbox is not registered (pending count shows "—").</summary>
        public SubmissionOutbox Outbox { get; private set; }

        /// <summary>Optional: null when no backend client is registered (server status "Bilinmiyor").</summary>
        public IBackendClient Backend { get; private set; }

        /// <summary>Optional: null when no XR status service is registered (VR status "Bilinmiyor").</summary>
        public IXrStatusService Xr { get; private set; }

        /// <summary>Participant-cycle counter; incremented whenever the session returns to Welcome.</summary>
        public int Epoch { get; private set; }

        /// <summary>Current session state (Welcome while unbound).</summary>
        public SessionState State => Session != null ? Session.State : SessionState.Welcome;

        internal void Bind(
            SessionController session,
            RuntimeConfig config,
            ScoreService score,
            GameTimer timer,
            CompletionEvaluator completion,
            SubmissionOutbox outbox,
            IBackendClient backend,
            IXrStatusService xr)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Score = score ?? throw new ArgumentNullException(nameof(score));
            Timer = timer ?? throw new ArgumentNullException(nameof(timer));
            Completion = completion ?? throw new ArgumentNullException(nameof(completion));
            Outbox = outbox;
            Backend = backend;
            Xr = xr;
            IsBound = true;
        }

        internal void AdvanceEpoch()
        {
            Epoch++;
        }

        /// <summary>
        /// Runs a session command only when the controller is still in <paramref name="requiredState"/>. Stale clicks
        /// (double clicks, Enter + click in the same frame, clicks racing an async transition) are ignored with a warning;
        /// exceptions are logged instead of propagating into uGUI.
        /// </summary>
        public bool Run(SessionState requiredState, Action command, string operation)
        {
            if (!IsBound)
            {
                Debug.LogWarning(LogPrefix + operation + " ignored: services are not bound.");
                return false;
            }

            if (Session.State != requiredState)
            {
                Debug.LogWarning(LogPrefix + operation + " ignored: expected state " + requiredState + ", current state " + Session.State + ".");
                return false;
            }

            return Execute(command, operation);
        }

        /// <summary>Runs a session command when the predicate on the current state holds (see <see cref="Run(SessionState, Action, string)"/>).</summary>
        public bool Run(Func<SessionState, bool> isAllowed, Action command, string operation)
        {
            if (!IsBound)
            {
                Debug.LogWarning(LogPrefix + operation + " ignored: services are not bound.");
                return false;
            }

            if (isAllowed != null && !isAllowed(Session.State))
            {
                Debug.LogWarning(LogPrefix + operation + " ignored in state " + Session.State + ".");
                return false;
            }

            return Execute(command, operation);
        }

        /// <summary>True when <see cref="SessionController.AbandonSession"/> is valid in <paramref name="state"/>.</summary>
        public static bool CanAbandon(SessionState state)
        {
            return state != SessionState.Welcome && state != SessionState.Finished;
        }

        /// <summary>True when <see cref="SessionController.ResetForNextParticipant"/> is valid in <paramref name="state"/>.</summary>
        public static bool CanReset(SessionState state)
        {
            return state == SessionState.Finished || state == SessionState.SubmissionFailed || state == SessionState.Fatal;
        }

        private static bool Execute(Action command, string operation)
        {
            try
            {
                command();
                return true;
            }
            catch (InvalidOperationException ex)
            {
                Debug.LogWarning(LogPrefix + operation + " rejected by the session controller: " + ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError(LogPrefix + operation + " failed: " + ex);
                return false;
            }
        }
    }
}
