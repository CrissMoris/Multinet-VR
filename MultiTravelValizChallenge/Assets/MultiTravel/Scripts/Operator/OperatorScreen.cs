using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
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
    /// PC-monitor operator screen (ARCHITECTURE.md §2.9). Add this single component to a GameObject in the Main scene; it
    /// builds a Screen Space Overlay canvas with every panel in <c>Awake</c> (no prefabs), resolves services from
    /// <see cref="AppServices"/> in <c>Start</c>, shows exactly one panel per <see cref="SessionState"/> and refreshes live
    /// values at 10 Hz. When services are missing it logs an error, shows an on-screen message and keeps retrying.
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

        private Canvas canvas;
        private RectTransform contentArea;
        private StatusBar statusBar;
        private LeaderboardPanel leaderboard;
        private RegistrationPanel registrationPanel;
        private ServiceErrorPanel serviceErrorPanel;
        private OperatorPanel activePanel;

        private WaitForSecondsRealtime refreshWait;
        private Coroutine refreshRoutine;
        private SessionController subscribedSession;
        private bool started;
        private bool missingServicesLogged;
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
            BuildUi();
        }

        private void Start()
        {
            started = true;
            EnsureEventSystem();
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

            statusBar?.Dispose();
            leaderboard?.Dispose();
        }

        private void Update()
        {
            if (registrationPanel != null && registrationPanel.IsVisible)
            {
                registrationPanel.Tick();
            }
        }

        // ----- construction -----

        private void BuildUi()
        {
            canvas = UiFactory.CreateOverlayCanvas("OperatorCanvas", transform, CanvasSortingOrder);
            var canvasRect = (RectTransform)canvas.transform;

            var background = UiFactory.CreateImage("Background", canvasRect, OperatorUiStyle.Background, false);
            UiFactory.Stretch(background.rectTransform);

            contentArea = UiFactory.CreateRect("Content", canvasRect);
            UiFactory.Stretch(contentArea, 0f, StatusBar.TotalHeight, 0f, 0f);

            registrationPanel = new RegistrationPanel(context);
            panels.Add(new WelcomePanel(context));
            panels.Add(registrationPanel);
            panels.Add(new GenderPanel(context));
            panels.Add(new InstructionsPanel(context));
            panels.Add(new LoadingPanel(context));
            panels.Add(new PlayingPanel(context));
            panels.Add(new ResultPanel(context));
            panels.Add(new FatalPanel(context));
            for (int i = 0; i < panels.Count; i++)
            {
                panels[i].Build(contentArea);
            }

            serviceErrorPanel = new ServiceErrorPanel(context);
            serviceErrorPanel.Build(contentArea);

            leaderboard = new LeaderboardPanel(context);
            leaderboard.Build(contentArea);

            statusBar = new StatusBar(context, ToggleLeaderboard);
            statusBar.Build(canvasRect);
            statusBar.SetLeaderboardOpen(false);
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
            statusBar.OnBound();
            ShowPanelFor(session.State);
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
                if (next == SessionState.Welcome)
                {
                    context.AdvanceEpoch();
                    for (int i = 0; i < panels.Count; i++)
                    {
                        panels[i].ClearParticipantData();
                    }
                }

                ShowPanelFor(next);

                if (next == SessionState.Finished && leaderboard.IsOpen)
                {
                    leaderboard.Refresh();
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
            statusBar.SetLeaderboardOpen(open);
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
                statusBar.Refresh(now);
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
