using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using MultiTravel.Core.Backend;
using MultiTravel.Core.Completion;
using MultiTravel.Core.Config;
using MultiTravel.Core.Outbox;
using MultiTravel.Core.Scoring;
using MultiTravel.Core.Services;
using MultiTravel.Core.Session;
using MultiTravel.Core.Timing;
using MultiTravel.Core.Xr;
using MultiTravel.Operator.Panels;
using MultiTravel.Operator.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace MultiTravel.Operator
{
    /// <summary>
    /// PC-monitor operator screen (ARCHITECTURE.md §2.9, OVERHAUL_PLAN.md §5). Add this single component to a GameObject in the
    /// Main scene; it builds a Screen Space Overlay canvas with every panel in <c>Awake</c> (no prefabs, no asset sprites),
    /// resolves services from <see cref="AppServices"/> in <c>Start</c>, shows exactly one panel per <see cref="SessionState"/>
    /// and refreshes live values at 10 Hz. Layout at 1920x1080: 72 px top bar, 300 px stepper, centre content, 400 px status
    /// rail. The optional <see cref="SpectatorCamera"/> in the scene is found automatically.
    /// When services are missing it logs an error, shows an on-screen message and keeps retrying.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OperatorScreen : MonoBehaviour
    {
        /// <summary>Sorting order of the overlay canvas (above any other screen-space UI).</summary>
        public const int CanvasSortingOrder = 100;

        private const float RefreshIntervalSeconds = 0.1f;
        private const float BindRetryIntervalSeconds = 1f;
        private const int MaxLoggedRefreshErrors = 5;
        private const string LogPrefix = "[MultiTravel.Operator] ";

        private readonly OperatorContext context = new OperatorContext();
        private readonly List<OperatorPanel> panels = new List<OperatorPanel>();
        private readonly SpectatorFeed feed = new SpectatorFeed();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();

        private Canvas canvas;
        private RectTransform centreArea;
        private CanvasGroup backdropGroup;
        private TopBar topBar;
        private Stepper stepper;
        private StatusRail rail;
        private LeaderboardPanel leaderboard;
        private ServiceErrorPanel serviceErrorPanel;
        private OperatorPanel activePanel;
        private UiToast toast;
        private ConfirmModal modal;
        private UiTooltipHost tooltip;

        private WaitForSecondsRealtime refreshWait;
        private Coroutine refreshRoutine;
        private SessionController subscribedSession;
        private bool started;
        private bool missingServicesLogged;
        private bool backdropShown;
        private float nextBindAttemptAt;
        private int loggedRefreshErrors;

        /// <summary>The overlay canvas built in Awake.</summary>
        public Canvas Canvas => canvas;

        /// <summary>Services and command helpers used by the panels (bound after Start).</summary>
        public OperatorContext Context => context;

        /// <summary>True once the required services were resolved.</summary>
        public bool IsBound => context.IsBound;

        private void Awake()
        {
            context.Lifetime = lifetime.Token;
            BuildUi();
        }

        private void Start()
        {
            started = true;
            EnsureEventSystem();
            OperatorUiStyle.CheckFont(canvas.scaleFactor);
            TryBind();
            StartRefreshLoop();
        }

        private void OnEnable()
        {
            if (started)
            {
                StartRefreshLoop();
            }
        }

        private void OnDisable()
        {
            if (refreshRoutine != null)
            {
                StopCoroutine(refreshRoutine);
                refreshRoutine = null;
            }
        }

        private void OnDestroy()
        {
            if (subscribedSession != null)
            {
                subscribedSession.StateChanged -= OnStateChanged;
                subscribedSession = null;
            }

            lifetime.Cancel();
            rail?.Dispose();
            leaderboard?.Dispose();
            for (int i = 0; i < panels.Count; i++)
            {
                panels[i].Dispose();
            }

            feed.SetWanted(false);
            UiTooltipHost.Release(tooltip);
            UiTween.Clear();
            lifetime.Dispose();
        }

        private void Update()
        {
            UiTween.Tick(Time.unscaledTime);
            toast?.Tick(Time.unscaledTime);
            if (modal != null)
            {
                modal.Tick();
            }

            if (activePanel != null && activePanel.IsVisible && (modal == null || !modal.IsOpen))
            {
                activePanel.Tick();
            }
        }

        // ----- construction -----

        private void BuildUi()
        {
            canvas = UiFactory.CreateOverlayCanvas("OperatorCanvas", transform, CanvasSortingOrder);
            var canvasRect = (RectTransform)canvas.transform;

            var background = UiFactory.CreateImage("Background", canvasRect, OperatorUiStyle.Background, false);
            UiFactory.Stretch(background.rectTransform);

            BuildBackdrop(canvasRect);

            var body = UiFactory.CreateRect("Body", canvasRect);
            UiFactory.Stretch(body, 0f, TopBar.Height, 0f, 0f);

            var stepperHost = UiFactory.CreateRect("StepperHost", body);
            UiFactory.AnchorLeft(stepperHost, OperatorUiStyle.StepperWidth);
            var railHost = UiFactory.CreateRect("RailHost", body);
            UiFactory.AnchorRight(railHost, OperatorUiStyle.RailWidth);
            centreArea = UiFactory.CreateRect("Content", body);
            UiFactory.Stretch(centreArea, OperatorUiStyle.StepperWidth + 12f, 24f, OperatorUiStyle.RailWidth + 12f, 24f);

            // Overlays first so panels can use them while building.
            context.Feed = feed;

            stepper = new Stepper(context);
            stepper.Build(stepperHost);

            rail = new StatusRail(context);
            rail.Build(railHost);

            BuildPanels(centreArea);

            leaderboard = new LeaderboardPanel(context);
            leaderboard.Build(centreArea);

            topBar = new TopBar(context, ToggleLeaderboard);
            topBar.Build(canvasRect);
            topBar.SetLeaderboardOpen(false);

            tooltip = UiTooltipHost.Create(canvasRect);
            toast = UiToast.Create(canvasRect);
            modal = ConfirmModal.Create(canvasRect);
            context.Toast = toast;
            context.Modal = modal;
            stepper.SetState(SessionState.Welcome);
        }

        private void BuildPanels(RectTransform area)
        {
            // Panels only touch Context.Modal / Context.Toast after Build (inside event handlers), so the overlays may be
            // created after the panels; the context properties are assigned in BuildUi.
            panels.Add(new WelcomePanel(context));
            panels.Add(new RegistrationPanel(context));
            panels.Add(new GenderPanel(context));
            panels.Add(new InstructionsPanel(context));
            panels.Add(new LoadingPanel(context));
            panels.Add(new PlayingPanel(context));
            panels.Add(new ResultPanel(context));
            panels.Add(new FatalPanel(context));
            for (int i = 0; i < panels.Count; i++)
            {
                panels[i].Build(area);
            }

            serviceErrorPanel = new ServiceErrorPanel(context);
            serviceErrorPanel.Build(area);
        }

        private void BuildBackdrop(RectTransform canvasRect)
        {
            var root = UiFactory.CreateRect("Backdrop", canvasRect);
            UiFactory.Stretch(root);
            backdropGroup = root.gameObject.AddComponent<CanvasGroup>();
            backdropGroup.alpha = 0f;
            backdropGroup.blocksRaycasts = false;
            backdropGroup.interactable = false;

            feed.CreateBackdrop(root, "SpectatorBackdrop");

            var tint = UiFactory.CreateImage("Tint", root, OperatorUiStyle.WithAlpha(OperatorUiStyle.Background, 0.74f), false);
            UiFactory.Stretch(tint.rectTransform);

            var top = UiFactory.CreateImage("TopGradient", root, OperatorUiStyle.Background, false);
            top.sprite = UiSprites.Gradient(true);
            UiFactory.AnchorTop(top.rectTransform, 360f);
            var bottom = UiFactory.CreateImage("BottomGradient", root, OperatorUiStyle.Background, false);
            bottom.sprite = UiSprites.Gradient(false);
            UiFactory.AnchorBottom(bottom.rectTransform, 420f);
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var existing = FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
            if (existing != null)
            {
                if (!existing.isActiveAndEnabled)
                {
                    Debug.LogWarning(LogPrefix + "An EventSystem exists but is inactive ('" + existing.name +
                                     "'); the operator screen will not receive mouse / keyboard input until it is enabled.");
                }

                return;
            }

            var go = new GameObject("EventSystem (Operator)");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
            Debug.LogWarning(LogPrefix + "No EventSystem found; created one with InputSystemUIInputModule. " +
                             "The scene should provide a single EventSystem (XRUIInputModule with mouse input enabled) for both VR and operator UI.");
        }

        // ----- service binding -----

        private bool TryBind()
        {
            if (context.IsBound)
            {
                return true;
            }

            nextBindAttemptAt = Time.realtimeSinceStartup + BindRetryIntervalSeconds;

            AppServices.TryGet<SessionController>(out var session);
            AppServices.TryGet<RuntimeConfig>(out var config);
            AppServices.TryGet<ScoreService>(out var score);
            AppServices.TryGet<GameTimer>(out var timer);
            AppServices.TryGet<CompletionEvaluator>(out var completion);
            AppServices.TryGet<SubmissionOutbox>(out var outbox);
            AppServices.TryGet<IBackendClient>(out var backend);
            AppServices.TryGet<IXrStatusService>(out var xr);

            if (session == null || config == null || score == null || timer == null || completion == null)
            {
                var missing = new StringBuilder();
                AppendMissing(missing, session == null, nameof(SessionController));
                AppendMissing(missing, config == null, nameof(RuntimeConfig));
                AppendMissing(missing, score == null, nameof(ScoreService));
                AppendMissing(missing, timer == null, nameof(GameTimer));
                AppendMissing(missing, completion == null, nameof(CompletionEvaluator));
                var detail = "Eksik servisler: " + missing;
                if (!missingServicesLogged)
                {
                    missingServicesLogged = true;
                    Debug.LogError(LogPrefix + "Required services are not registered in AppServices: " + missing +
                                   ". Start the application from the Bootstrap scene (AppBootstrap registers them). " +
                                   "The operator screen shows an error and retries every " + BindRetryIntervalSeconds + " s.");
                }

                ShowServiceError(detail);
                return false;
            }

            if (outbox == null)
            {
                Debug.LogError(LogPrefix + "SubmissionOutbox is not registered; the pending submission count is unavailable.");
            }

            if (backend == null)
            {
                Debug.LogError(LogPrefix + "IBackendClient is not registered; server status and leaderboard are unavailable.");
            }

            if (xr == null)
            {
                Debug.LogError(LogPrefix + "IXrStatusService is not registered; the VR status indicator shows 'Bilinmiyor'.");
            }

            context.Bind(session, config, score, timer, completion, outbox, backend, xr);
            if (missingServicesLogged)
            {
                Debug.Log(LogPrefix + "Services resolved; operator screen is active.");
            }

            subscribedSession = session;
            session.StateChanged += OnStateChanged;

            serviceErrorPanel.Hide();
            topBar.OnBound();
            rail.OnBound();
            ShowPanelFor(session.State);
            ApplyDecor(session.State);
            return true;
        }

        private static void AppendMissing(StringBuilder builder, bool isMissing, string name)
        {
            if (!isMissing)
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            builder.Append(name);
        }

        private void ShowServiceError(string detail)
        {
            if (activePanel != null)
            {
                activePanel.Hide();
                activePanel = null;
            }

            serviceErrorPanel.SetDetail(detail);
            serviceErrorPanel.Show(SessionState.Welcome);
        }

        // ----- state handling -----

        private void OnStateChanged(SessionState previous, SessionState next)
        {
            // Never let a UI problem propagate into the session controller's transition.
            try
            {
                modal.Close();
                if (next == SessionState.Welcome)
                {
                    context.AdvanceEpoch();
                    for (int i = 0; i < panels.Count; i++)
                    {
                        panels[i].ClearParticipantData();
                    }
                }

                ShowPanelFor(next);
                ApplyDecor(next);

                if (next == SessionState.Finished)
                {
                    leaderboard.RefreshIfOpen();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError(LogPrefix + "Operator UI failed to handle " + previous + " -> " + next + ": " + ex);
            }
        }

        private void ShowPanelFor(SessionState state)
        {
            OperatorPanel target = null;
            for (int i = 0; i < panels.Count; i++)
            {
                if (panels[i].HandlesState(state))
                {
                    target = panels[i];
                    break;
                }
            }

            if (target != activePanel)
            {
                if (activePanel != null)
                {
                    activePanel.Hide();
                }

                ClearSelection();
                activePanel = target;
            }

            if (target == null)
            {
                Debug.LogWarning(LogPrefix + "No operator panel handles state " + state + ".");
                return;
            }

            target.Show(state);
        }

        /// <summary>Stepper highlight, spectator backdrop (Welcome / result) and camera activity for a state.</summary>
        private void ApplyDecor(SessionState state)
        {
            stepper.SetState(state);

            bool backdrop = state == SessionState.Welcome || state == SessionState.Completed || state == SessionState.Submitting
                            || state == SessionState.SubmissionFailed || state == SessionState.Finished;
            bool video = backdrop || state == SessionState.Loading || state == SessionState.Countdown || state == SessionState.Playing;
            if (backdrop != backdropShown)
            {
                backdropShown = backdrop;
                UiTween.Fade(backdropGroup, backdrop ? 1f : 0f, UiTween.Slow);
            }

            feed.SetWanted(video);
        }

        private static void ClearSelection()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem != null && !eventSystem.alreadySelecting)
            {
                eventSystem.SetSelectedGameObject(null);
            }
        }

        private void ToggleLeaderboard()
        {
            bool open = leaderboard.Toggle();
            topBar.SetLeaderboardOpen(open);
        }

        // ----- 10 Hz refresh -----

        private void StartRefreshLoop()
        {
            if (refreshRoutine != null || !isActiveAndEnabled)
            {
                return;
            }

            refreshWait = new WaitForSecondsRealtime(RefreshIntervalSeconds);
            refreshRoutine = StartCoroutine(RefreshLoop());
        }

        private IEnumerator RefreshLoop()
        {
            while (true)
            {
                RefreshTick();
                yield return refreshWait;
            }
        }

        private void RefreshTick()
        {
            float now = Time.realtimeSinceStartup;
            if (!context.IsBound)
            {
                if (now >= nextBindAttemptAt)
                {
                    TryBind();
                }

                return;
            }

            try
            {
                rail.Refresh(now);
                stepper.Refresh();
                feed.Refresh();
                if (activePanel != null && activePanel.IsVisible)
                {
                    activePanel.Refresh();
                }
            }
            catch (Exception ex)
            {
                if (loggedRefreshErrors < MaxLoggedRefreshErrors)
                {
                    loggedRefreshErrors++;
                    Debug.LogError(LogPrefix + "Operator UI refresh failed: " + ex);
                }
            }
        }
    }
}
