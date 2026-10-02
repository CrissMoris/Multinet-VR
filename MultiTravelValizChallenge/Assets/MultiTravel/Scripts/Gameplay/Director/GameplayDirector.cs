using System;
using System.Collections;
using MultiTravel.Core.Completion;
using MultiTravel.Core.Config;
using MultiTravel.Core.Products;
using MultiTravel.Core.Scoring;
using MultiTravel.Core.Session;
using MultiTravel.Gameplay.Common;
using MultiTravel.Gameplay.Feedback;
using MultiTravel.Gameplay.Items;
using MultiTravel.Gameplay.Suitcase;
using UnityEngine;

namespace MultiTravel.Gameplay.Director
{
    /// <summary>
    /// Connects the Core <see cref="SessionController"/> to the Main-scene gameplay (ARCHITECTURE.md §2.8).
    /// <list type="bullet">
    /// <item>Loading: resolves the <see cref="ProductSet"/> for the selected gender, configures the
    /// <see cref="CompletionEvaluator"/> with it (the evaluator is registered at bootstrap with an empty set),
    /// clears the suitcase, activates the pooled items, assigns spawn slots (optional seeded shuffle) and then calls
    /// <see cref="SessionController.LoadingFinished"/> on the next frame.</item>
    /// <item>Countdown: runs the VR countdown (unscaled seconds, <see cref="CountdownTick"/> per second) and calls
    /// <see cref="SessionController.CountdownFinished"/>.</item>
    /// <item>Playing: unlocks interaction and polls <see cref="SessionController.TryCompleteIfDue"/> every 0.25 s
    /// (and on the frame after every placement / manual confirm).</item>
    /// <item>Every other state: interaction locked, placements rejected.</item>
    /// <item><see cref="SessionController.SessionResetRequested"/>: full reset (suitcase cleared, every item pooled,
    /// coroutines stopped, feedback cleared).</item>
    /// </list>
    /// State work is deferred out of the <see cref="SessionController.StateChanged"/> callback so other listeners always
    /// see transitions in order. The director can be enabled at any time after bootstrap: it re-syncs to the current state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameplayDirector : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField]
        [Tooltip("Product catalog (Assets/MultiTravel/Data/ProductCatalog.asset).")]
        private ProductCatalog catalog;

        [Header("Scene references")]
        [SerializeField]
        private ItemPool itemPool;

        [SerializeField]
        private SpawnSlotLayout spawnLayout;

        [SerializeField]
        private SuitcaseController suitcase;

        [SerializeField]
        private InteractionLock interactionLock;

        [SerializeField]
        [Tooltip("Optional: countdown ticks and reset of floating labels.")]
        private PlacementFeedback feedback;

        [Header("Timing")]
        [SerializeField]
        [Tooltip("Interval of the completion poll while Playing (seconds).")]
        [Min(0.02f)]
        private float completionPollSeconds = 0.25f;

        [SerializeField]
        [Tooltip("Seconds between retries when services are not yet registered.")]
        [Min(0.1f)]
        private float serviceRetrySeconds = 1f;

        private SessionController session;
        private RuntimeConfig config;
        private ScoreService scoreService;
        private CompletionEvaluator completionEvaluator;
        private ProductSetResolver resolver;

        private bool bound;
        private bool subscribed;
        private bool missingServicesLogged;
        private float nextBindAttempt;
        private float pollTimer;
        private bool completionCheckRequested;
        private Coroutine loadingRoutine;
        private Coroutine countdownRoutine;
        private SessionState appliedState = (SessionState)(-1);
        private SuitcaseController subscribedSuitcase;

        /// <summary>Raised every countdown second with the remaining seconds (n..1), then 0 when play starts.</summary>
        public event Action<int> CountdownTick;

        /// <summary>Raised after the item set for a session was prepared (before LoadingFinished).</summary>
        public event Action<ProductSet> SessionPrepared;

        /// <summary>Raised after a full gameplay reset.</summary>
        public event Action GameplayReset;

        /// <summary>Remaining countdown seconds while in Countdown (0 otherwise).</summary>
        public int CountdownRemaining { get; private set; }

        /// <summary>The product set of the current session (null before the first Loading and after a reset).</summary>
        public ProductSet ActiveSet { get; private set; }

        /// <summary>Seed used for the spawn shuffle of the current session.</summary>
        public int ActiveShuffleSeed { get; private set; }

        /// <summary>True once the Core services were resolved and the controller is subscribed.</summary>
        public bool IsBound => bound;

        /// <summary>The bound session controller (null until bound).</summary>
        public SessionController Session => session;

        /// <summary>The catalog in use.</summary>
        public ProductCatalog Catalog => catalog;

        // ----- setup -----

        /// <summary>
        /// Explicit injection (tests / custom hosts). Must be called before <c>Start</c> or while unbound.
        /// Null arguments are resolved from AppServices.
        /// </summary>
        public void Bind(
            SessionController sessionController,
            RuntimeConfig runtimeConfig,
            ScoreService score,
            CompletionEvaluator evaluator,
            ProductSetResolver productSetResolver)
        {
            if (bound)
            {
                Debug.LogWarning(ServiceResolver.LogPrefix + "GameplayDirector.Bind called after binding; ignored.", this);
                return;
            }

            session = sessionController ?? session;
            config = runtimeConfig ?? config;
            scoreService = score ?? scoreService;
            completionEvaluator = evaluator ?? completionEvaluator;
            resolver = productSetResolver ?? resolver;
            TryBind(true);
        }

        /// <summary>Generator API: assigns every scene reference at once.</summary>
        public void Configure(
            ProductCatalog productCatalog,
            ItemPool pool,
            SpawnSlotLayout layout,
            SuitcaseController suitcaseController,
            InteractionLock lockComponent,
            PlacementFeedback placementFeedback)
        {
            catalog = productCatalog;
            itemPool = pool;
            spawnLayout = layout;
            suitcase = suitcaseController;
            interactionLock = lockComponent;
            feedback = placementFeedback;
        }

        /// <summary>Asks for a completion check on the next frame (placement, manual confirm).</summary>
        public void RequestCompletionCheck()
        {
            completionCheckRequested = true;
        }

        // ----- Unity -----

        private void Start()
        {
            ValidateSceneReferences();
            TryBind(false);
        }

        private void OnEnable()
        {
            SubscribeSuitcase();
            if (bound)
            {
                appliedState = (SessionState)(-1);
                SyncToState(session.State);
            }
        }

        private void OnDisable()
        {
            // Coroutines stop with the component; OnEnable restarts whatever the current state needs.
            loadingRoutine = null;
            countdownRoutine = null;
            appliedState = (SessionState)(-1);
        }

        private void OnDestroy()
        {
            if (subscribed && session != null)
            {
                session.StateChanged -= OnStateChanged;
                session.SessionResetRequested -= OnSessionResetRequested;
            }

            subscribed = false;
            UnsubscribeSuitcase();
        }

        private void Update()
        {
            if (!bound)
            {
                if (Time.unscaledTime >= nextBindAttempt)
                {
                    TryBind(false);
                }

                return;
            }

            var state = session.State;
            if (state != appliedState)
            {
                SyncToState(state);
            }

            if (state != SessionState.Playing)
            {
                pollTimer = 0f;
                completionCheckRequested = false;
                return;
            }

            pollTimer += Time.unscaledDeltaTime;
            if (!completionCheckRequested && pollTimer < completionPollSeconds)
            {
                return;
            }

            pollTimer = 0f;
            completionCheckRequested = false;
            try
            {
                session.TryCompleteIfDue();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
            }
        }

        // ----- binding -----

        private void TryBind(bool fromExplicitBind)
        {
            if (bound)
            {
                return;
            }

            nextBindAttempt = Time.unscaledTime + serviceRetrySeconds;
            bool logErrors = !missingServicesLogged;
            bool ok = Resolve(ref session, logErrors);
            ok &= Resolve(ref config, logErrors);
            ok &= Resolve(ref scoreService, logErrors);
            ok &= Resolve(ref completionEvaluator, logErrors);
            if (resolver == null && !ServiceResolver.TryResolve(ref resolver))
            {
                // Stateless; a local instance is equivalent to the registered one.
                resolver = new ProductSetResolver();
            }

            if (catalog == null)
            {
                ok &= logErrors ? ServiceResolver.ResolveCatalog(ref catalog, this, nameof(GameplayDirector)) : ServiceResolver.TryResolve(ref catalog);
            }

            if (!ok)
            {
                if (!missingServicesLogged && !fromExplicitBind)
                {
                    missingServicesLogged = true;
                }

                return;
            }

            bound = true;
            session.StateChanged += OnStateChanged;
            session.SessionResetRequested += OnSessionResetRequested;
            subscribed = true;
            SubscribeSuitcase();
            if (suitcase != null)
            {
                suitcase.Bind(scoreService, completionEvaluator, catalog);
            }

            appliedState = (SessionState)(-1);
            if (isActiveAndEnabled)
            {
                SyncToState(session.State);
            }
        }

        private bool Resolve<T>(ref T field, bool logErrors) where T : class
        {
            return logErrors ? ServiceResolver.Resolve(ref field, this, nameof(GameplayDirector)) : ServiceResolver.TryResolve(ref field);
        }

        private void ValidateSceneReferences()
        {
            if (itemPool == null)
            {
                Debug.LogError(ServiceResolver.LogPrefix + "GameplayDirector: ItemPool is not assigned.", this);
            }

            if (spawnLayout == null)
            {
                Debug.LogError(ServiceResolver.LogPrefix + "GameplayDirector: SpawnSlotLayout is not assigned.", this);
            }

            if (suitcase == null)
            {
                Debug.LogError(ServiceResolver.LogPrefix + "GameplayDirector: SuitcaseController is not assigned.", this);
            }

            if (interactionLock == null)
            {
                Debug.LogError(ServiceResolver.LogPrefix + "GameplayDirector: InteractionLock is not assigned.", this);
            }
        }

        private void SubscribeSuitcase()
        {
            if (suitcase == null || subscribedSuitcase == suitcase)
            {
                return;
            }

            UnsubscribeSuitcase();
            suitcase.ItemPlaced += OnItemPlaced;
            subscribedSuitcase = suitcase;
        }

        private void UnsubscribeSuitcase()
        {
            if (subscribedSuitcase == null)
            {
                return;
            }

            subscribedSuitcase.ItemPlaced -= OnItemPlaced;
            subscribedSuitcase = null;
        }

        // ----- state handling -----

        private void OnStateChanged(SessionState previous, SessionState next)
        {
            // Only cheap, synchronous safety work here (lock before anything else can react); the rest runs in Update.
            if (next != SessionState.Playing)
            {
                SetPlayable(false);
            }

            if (isActiveAndEnabled)
            {
                SyncToState(next);
            }
        }

        private void SyncToState(SessionState state)
        {
            if (state == appliedState)
            {
                return;
            }

            appliedState = state;
            if (state != SessionState.Loading)
            {
                StopRoutine(ref loadingRoutine);
            }

            if (state != SessionState.Countdown)
            {
                StopRoutine(ref countdownRoutine);
                CountdownRemaining = 0;
            }

            switch (state)
            {
                case SessionState.Loading:
                    SetPlayable(false);
                    StopRoutine(ref loadingRoutine);
                    loadingRoutine = StartCoroutine(LoadingRoutine());
                    break;
                case SessionState.Countdown:
                    SetPlayable(false);
                    StopRoutine(ref countdownRoutine);
                    countdownRoutine = StartCoroutine(CountdownRoutine());
                    break;
                case SessionState.Playing:
                    pollTimer = 0f;
                    completionCheckRequested = true;
                    SetPlayable(true);
                    break;
                default:
                    SetPlayable(false);
                    break;
            }
        }

        private void SetPlayable(bool playable)
        {
            // Placements first, so force-released items can never be placed while locking.
            if (suitcase != null)
            {
                suitcase.AcceptPlacements = playable;
            }

            if (interactionLock != null)
            {
                interactionLock.SetLocked(!playable);
            }
        }

        private IEnumerator LoadingRoutine()
        {
            // Leave the StateChanged dispatch first so every listener sees Loading before Countdown.
            yield return null;
            if (session.State != SessionState.Loading)
            {
                loadingRoutine = null;
                yield break;
            }

            string error = PrepareSession();
            if (error != null)
            {
                loadingRoutine = null;
                Debug.LogError(ServiceResolver.LogPrefix + "GameplayDirector: " + error, this);
                session.ReportFatal(FatalReason.ConfigurationInvalid, error);
                yield break;
            }

            // One frame for physics / transforms to settle at the new spawn poses.
            yield return null;
            loadingRoutine = null;
            if (session.State == SessionState.Loading)
            {
                session.LoadingFinished();
            }
        }

        /// <summary>Builds the session's item set. Returns null on success or an English error description.</summary>
        private string PrepareSession()
        {
            var participant = session.Current;
            if (participant == null || !participant.GenderSelected)
            {
                return "Loading entered without a participant / gender selection.";
            }

            if (catalog == null)
            {
                return "No ProductCatalog available.";
            }

            if (itemPool == null || spawnLayout == null || suitcase == null || interactionLock == null)
            {
                return "Scene references are missing (ItemPool, SpawnSlotLayout, SuitcaseController, InteractionLock).";
            }

            var set = resolver.Resolve(catalog, participant.Gender);
            if (set.Items.Count == 0)
            {
                return "The catalog has no products for gender " + participant.Gender + ".";
            }

            if (set.Required.Count == 0 && completionEvaluator.Mode == CompletionMode.RequiredItemsPlaced)
            {
                Debug.LogWarning(ServiceResolver.LogPrefix + "GameplayDirector: no required products for gender " + participant.Gender +
                                 "; the game can only end by time limit or operator.", this);
            }

            completionEvaluator.Configure(set);
            if (scoreService.Score != 0 || scoreService.CountedProductIds.Count > 0)
            {
                Debug.LogWarning(ServiceResolver.LogPrefix + "GameplayDirector: score was not zero at Loading; resetting it.", this);
                scoreService.Reset();
            }

            SetPlayable(false);
            suitcase.Clear();
            var items = itemPool.Activate(set);
            if (items.Count == 0)
            {
                return "No item instance could be activated (missing VisualPrefabs?).";
            }

            bool shuffle = config.Gameplay.ShuffleSpawnPositions && catalog.ShuffleSpawnPositions;
            ActiveShuffleSeed = ComputeSeed(participant);
            spawnLayout.Assign(items, shuffle, ActiveShuffleSeed);
            interactionLock.SetLocked(true);
            if (feedback != null)
            {
                feedback.ResetFeedback();
            }

            ActiveSet = set;
            SessionPrepared?.Invoke(set);
            return null;
        }

        private int ComputeSeed(ParticipantSession participant)
        {
            if (catalog.ShuffleSeedMode == ShuffleSeedMode.Fixed)
            {
                return catalog.FixedShuffleSeed;
            }

            // Stable per session (reproducible from the logged client session id), different between sessions.
            var bytes = participant.ClientSessionId.ToByteArray();
            int seed = 17;
            for (int i = 0; i < bytes.Length; i++)
            {
                seed = unchecked(seed * 31 + bytes[i]);
            }

            return seed;
        }

        private IEnumerator CountdownRoutine()
        {
            int seconds = Mathf.Max(0, session.CountdownSeconds);
            for (int remaining = seconds; remaining > 0; remaining--)
            {
                if (session.State != SessionState.Countdown)
                {
                    countdownRoutine = null;
                    CountdownRemaining = 0;
                    yield break;
                }

                CountdownRemaining = remaining;
                CountdownTick?.Invoke(remaining);
                if (feedback != null)
                {
                    feedback.PlayTick();
                }

                yield return new WaitForSecondsRealtime(1f);
            }

            countdownRoutine = null;
            CountdownRemaining = 0;
            if (session.State != SessionState.Countdown)
            {
                yield break;
            }

            CountdownTick?.Invoke(0);
            if (feedback != null)
            {
                feedback.PlayStart();
            }

            session.CountdownFinished();
        }

        private void OnSessionResetRequested()
        {
            FullReset();
        }

        /// <summary>Returns every item to the pool, empties the suitcase and stops all gameplay coroutines.</summary>
        public void FullReset()
        {
            StopRoutine(ref loadingRoutine);
            StopRoutine(ref countdownRoutine);
            CountdownRemaining = 0;
            completionCheckRequested = false;
            pollTimer = 0f;

            if (suitcase != null)
            {
                suitcase.AcceptPlacements = false;
                suitcase.Clear();
            }

            if (itemPool != null)
            {
                itemPool.DeactivateAll();
            }

            if (interactionLock != null)
            {
                interactionLock.SetLocked(true);
            }

            if (feedback != null)
            {
                feedback.ResetFeedback();
            }

            ActiveSet = null;
            GameplayReset?.Invoke();
        }

        private void OnItemPlaced(ProductItem item, ScoreChange change)
        {
            completionCheckRequested = true;
        }

        private void StopRoutine(ref Coroutine routine)
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }
        }
    }
}
