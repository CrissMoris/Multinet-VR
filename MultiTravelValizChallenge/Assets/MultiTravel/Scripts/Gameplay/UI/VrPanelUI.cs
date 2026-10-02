using System.Text;
using MultiTravel.Core.Completion;
using MultiTravel.Core.Config;
using MultiTravel.Core.Products;
using MultiTravel.Core.Scoring;
using MultiTravel.Core.Session;
using MultiTravel.Core.Timing;
using MultiTravel.Core.Utility;
using MultiTravel.Gameplay.Common;
using MultiTravel.Gameplay.Director;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiTravel.Gameplay.UI
{
    /// <summary>
    /// Read-only VR mirror of the session (ARCHITECTURE.md §2.8): a world-space canvas built in <c>Awake</c> on this
    /// GameObject (place it on the curved backdrop). Shows title, scenario, a HUD row (score, timer <c>mm:ss.f</c>,
    /// progress "n/N gerekli ürün") and one panel per <see cref="SessionState"/> (countdown digits, result summary with
    /// score / time / rank, "Gönderiliyor...", failure text).
    /// <para>
    /// The canvas is 1600 × 1000 units at scale 0.001 (1.6 m × 1.0 m); text heights are chosen for reading at ~2 m.
    /// Values refresh at <see cref="refreshHz"/> and strings are rebuilt only when the value changed.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VrPanelUI : MonoBehaviour
    {
        private const int StateCount = (int)SessionState.Fatal + 1;

        [SerializeField]
        [Tooltip("Optional: source of the countdown digits.")]
        private GameplayDirector director;

        [SerializeField]
        [Tooltip("Optional: scenario title fallback when the configured scenario text is empty.")]
        private ProductCatalog catalog;

        [SerializeField]
        [Tooltip("Canvas size in canvas units (millimetres at the default scale).")]
        private Vector2 canvasSize = new Vector2(1600f, 1000f);

        [SerializeField]
        [Tooltip("World units per canvas unit.")]
        [Min(0.0001f)]
        private float canvasScale = 0.001f;

        [SerializeField]
        [Tooltip("Value refresh rate of the HUD (Hz).")]
        [Range(1f, 30f)]
        private float refreshHz = 10f;

        private SessionController session;
        private RuntimeConfig config;
        private ScoreService scoreService;
        private GameTimer timer;
        private CompletionEvaluator completionEvaluator;
        private bool servicesReady;
        private bool resetSubscribed;

        private Canvas canvas;
        private TextMeshProUGUI titleText;
        private TextMeshProUGUI scenarioText;
        private GameObject hudRoot;
        private TextMeshProUGUI scoreText;
        private TextMeshProUGUI timerText;
        private TextMeshProUGUI progressText;
        private readonly GameObject[] statePanels = new GameObject[StateCount];
        private readonly TextMeshProUGUI[] headings = new TextMeshProUGUI[StateCount];
        private readonly TextMeshProUGUI[] bodies = new TextMeshProUGUI[StateCount];
        private readonly TextMeshProUGUI[] statuses = new TextMeshProUGUI[StateCount];
        private TextMeshProUGUI countdownDigits;
        private readonly StringBuilder builder = new StringBuilder(256);

        private SessionState shownState = (SessionState)(-1);
        private int shownScore = int.MinValue;
        private long shownTenths = long.MinValue;
        private int shownPlaced = int.MinValue;
        private int shownRequired = int.MinValue;
        private int shownCountdown = int.MinValue;
        private float nextRefresh;
        private bool built;

        /// <summary>The state whose panel is visible.</summary>
        public SessionState ShownState => shownState;

        /// <summary>The generated canvas.</summary>
        public Canvas Canvas => canvas;

        /// <summary>Generator / test API.</summary>
        public void Configure(GameplayDirector gameplayDirector, ProductCatalog productCatalog)
        {
            director = gameplayDirector;
            catalog = productCatalog;
        }

        /// <summary>Test API: explicit services (null arguments are resolved from AppServices in Start).</summary>
        public void Bind(SessionController sessionController, RuntimeConfig runtimeConfig, ScoreService score, GameTimer gameTimer, CompletionEvaluator evaluator)
        {
            session = sessionController ?? session;
            config = runtimeConfig ?? config;
            scoreService = score ?? scoreService;
            timer = gameTimer ?? timer;
            completionEvaluator = evaluator ?? completionEvaluator;
            servicesReady = session != null && config != null && scoreService != null && timer != null && completionEvaluator != null;
            if (servicesReady)
            {
                OnServicesReady();
            }
        }

        /// <summary>Clears every cached value and the result texts (session reset).</summary>
        public void ResetDisplay()
        {
            shownState = (SessionState)(-1);
            shownScore = int.MinValue;
            shownTenths = long.MinValue;
            shownPlaced = int.MinValue;
            shownRequired = int.MinValue;
            shownCountdown = int.MinValue;
            if (!built)
            {
                return;
            }

            scoreText.text = string.Empty;
            timerText.text = string.Empty;
            progressText.text = string.Empty;
            for (int i = 0; i < StateCount; i++)
            {
                if (statuses[i] != null)
                {
                    statuses[i].text = string.Empty;
                }
            }
        }

        // ----- Unity -----

        private void Awake()
        {
            Build();
        }

        private void Start()
        {
            if (!servicesReady)
            {
                bool ok = ServiceResolver.Resolve(ref session, this, nameof(VrPanelUI));
                ok &= ServiceResolver.Resolve(ref config, this, nameof(VrPanelUI));
                ok &= ServiceResolver.Resolve(ref scoreService, this, nameof(VrPanelUI));
                ok &= ServiceResolver.Resolve(ref timer, this, nameof(VrPanelUI));
                ok &= ServiceResolver.Resolve(ref completionEvaluator, this, nameof(VrPanelUI));
                servicesReady = ok;
                if (ok)
                {
                    OnServicesReady();
                }
            }

            if (director == null)
            {
                director = FindAnyObjectByType<GameplayDirector>();
            }
        }

        private void OnDestroy()
        {
            if (resetSubscribed && session != null)
            {
                session.SessionResetRequested -= ResetDisplay;
            }

            resetSubscribed = false;
        }

        private void Update()
        {
            if (!servicesReady || !built)
            {
                return;
            }

            var state = session.State;
            if (state != shownState)
            {
                ShowState(state);
            }

            if (state == SessionState.Countdown)
            {
                UpdateCountdown();
            }

            if (Time.unscaledTime < nextRefresh)
            {
                return;
            }

            nextRefresh = Time.unscaledTime + 1f / refreshHz;
            RefreshHud();
        }

        // ----- building -----

        private void Build()
        {
            if (built)
            {
                return;
            }

            canvas = GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }

            canvas.renderMode = RenderMode.WorldSpace;
            var rect = GetComponent<RectTransform>();
            if (rect == null)
            {
                rect = gameObject.AddComponent<RectTransform>();
            }

            rect.sizeDelta = canvasSize;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localScale = Vector3.one * canvasScale;

            var background = CreateChild("Background", transform);
            Stretch(background, Vector2.zero, Vector2.one);
            var backgroundImage = background.gameObject.AddComponent<Image>();
            backgroundImage.color = VrUiStyle.PanelBackground;
            backgroundImage.raycastTarget = false;

            var accent = CreateChild("AccentBar", transform);
            Stretch(accent, new Vector2(0f, 0.985f), Vector2.one);
            var accentImage = accent.gameObject.AddComponent<Image>();
            accentImage.color = VrUiStyle.Orange;
            accentImage.raycastTarget = false;

            titleText = CreateText("Title", transform, VrUiStyle.TitleSize, VrUiStyle.White, new Vector2(0.04f, 0.86f), new Vector2(0.96f, 0.97f), TextAlignmentOptions.Center);
            titleText.fontStyle = FontStyles.Bold;
            scenarioText = CreateText("Scenario", transform, VrUiStyle.SubtitleSize, VrUiStyle.SecondaryText, new Vector2(0.04f, 0.79f), new Vector2(0.96f, 0.86f), TextAlignmentOptions.Center);

            var hud = CreateChild("Hud", transform);
            Stretch(hud, new Vector2(0f, 0.67f), new Vector2(1f, 0.78f));
            var hudImage = hud.gameObject.AddComponent<Image>();
            hudImage.color = VrUiStyle.PanelStrip;
            hudImage.raycastTarget = false;
            hudRoot = hud.gameObject;
            scoreText = CreateText("Score", hud, VrUiStyle.HudSize, VrUiStyle.Teal, new Vector2(0.02f, 0f), new Vector2(0.32f, 1f), TextAlignmentOptions.Center);
            scoreText.fontStyle = FontStyles.Bold;
            timerText = CreateText("Timer", hud, VrUiStyle.HudSize, VrUiStyle.White, new Vector2(0.34f, 0f), new Vector2(0.66f, 1f), TextAlignmentOptions.Center);
            timerText.fontStyle = FontStyles.Bold;
            progressText = CreateText("Progress", hud, VrUiStyle.HudSize * 0.8f, VrUiStyle.Orange, new Vector2(0.66f, 0f), new Vector2(0.98f, 1f), TextAlignmentOptions.Center);

            var panelsRoot = CreateChild("StatePanels", transform);
            Stretch(panelsRoot, new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.65f));
            for (int i = 0; i < StateCount; i++)
            {
                BuildStatePanel((SessionState)i, panelsRoot);
            }

            built = true;
            hudRoot.SetActive(false);
            for (int i = 0; i < StateCount; i++)
            {
                statePanels[i].SetActive(false);
            }
        }

        private void BuildStatePanel(SessionState state, Transform parent)
        {
            var panel = CreateChild("Panel_" + state, parent);
            Stretch(panel, Vector2.zero, Vector2.one);
            statePanels[(int)state] = panel.gameObject;

            headings[(int)state] = CreateText("Heading", panel, VrUiStyle.HeadingSize, VrUiStyle.White, new Vector2(0f, 0.74f), new Vector2(1f, 1f), TextAlignmentOptions.Center);
            headings[(int)state].fontStyle = FontStyles.Bold;

            if (state == SessionState.Countdown)
            {
                countdownDigits = CreateText("Digits", panel, VrUiStyle.CountdownSize, VrUiStyle.Orange, new Vector2(0f, 0f), new Vector2(1f, 0.76f), TextAlignmentOptions.Center);
                countdownDigits.fontStyle = FontStyles.Bold;
                return;
            }

            bool isResult = state == SessionState.Completed || state == SessionState.Submitting ||
                            state == SessionState.SubmissionFailed || state == SessionState.Finished;
            if (isResult)
            {
                bodies[(int)state] = CreateText("Summary", panel, VrUiStyle.BodySize * 1.1f, VrUiStyle.White, new Vector2(0f, 0.22f), new Vector2(1f, 0.74f), TextAlignmentOptions.Center);
                statuses[(int)state] = CreateText("Status", panel, VrUiStyle.BodySize, VrUiStyle.SecondaryText, new Vector2(0f, 0f), new Vector2(1f, 0.22f), TextAlignmentOptions.Center);
            }
            else
            {
                bodies[(int)state] = CreateText("Body", panel, VrUiStyle.BodySize, VrUiStyle.SecondaryText, new Vector2(0.03f, 0f), new Vector2(0.97f, 0.74f), TextAlignmentOptions.Top);
                bodies[(int)state].textWrappingMode = TextWrappingModes.Normal;
                bodies[(int)state].enableAutoSizing = true;
                bodies[(int)state].fontSizeMin = VrUiStyle.BodySize * 0.7f;
                bodies[(int)state].fontSizeMax = VrUiStyle.BodySize;
            }
        }

        private static RectTransform CreateChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private static TextMeshProUGUI CreateText(string name, Transform parent, float size, Color color, Vector2 anchorMin, Vector2 anchorMax, TextAlignmentOptions alignment)
        {
            var rect = CreateChild(name, parent);
            Stretch(rect, anchorMin, anchorMax);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            var font = VrUiStyle.Font;
            if (font != null)
            {
                text.font = font;
            }

            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            text.text = string.Empty;
            return text;
        }

        // ----- content -----

        private void OnServicesReady()
        {
            if (!resetSubscribed && session != null)
            {
                session.SessionResetRequested += ResetDisplay;
                resetSubscribed = true;
            }

            if (!built)
            {
                Build();
            }

            titleText.text = config.Branding.ProductTitle;
            string scenario = config.Texts.ScenarioText;
            if (string.IsNullOrWhiteSpace(scenario) && catalog != null)
            {
                scenario = catalog.ScenarioTitle;
            }

            scenarioText.text = scenario ?? string.Empty;

            headings[(int)SessionState.Welcome].text = config.Texts.WelcomeTitle;
            bodies[(int)SessionState.Welcome].text = config.Texts.WelcomeSubtitle + "\n\n" + VrUiTexts.WelcomeHint;
            headings[(int)SessionState.Registration].text = VrUiTexts.RegistrationHeading;
            bodies[(int)SessionState.Registration].text = VrUiTexts.RegistrationBody;
            headings[(int)SessionState.GenderSelection].text = VrUiTexts.GenderHeading;
            bodies[(int)SessionState.GenderSelection].text = VrUiTexts.GenderBody;
            headings[(int)SessionState.Instructions].text = VrUiTexts.InstructionsHeading;
            bodies[(int)SessionState.Instructions].text = config.Texts.InstructionsText;
            headings[(int)SessionState.Loading].text = VrUiTexts.LoadingHeading;
            bodies[(int)SessionState.Loading].text = VrUiTexts.LoadingBody;
            headings[(int)SessionState.Countdown].text = VrUiTexts.CountdownHeading;
            headings[(int)SessionState.Playing].text = VrUiTexts.PlayingHeading;
            bodies[(int)SessionState.Playing].text = PlayingBody(completionEvaluator.Mode);
            headings[(int)SessionState.Completed].text = VrUiTexts.CompletedHeading;
            headings[(int)SessionState.Submitting].text = VrUiTexts.CompletedHeading;
            headings[(int)SessionState.SubmissionFailed].text = VrUiTexts.CompletedHeading;
            headings[(int)SessionState.Finished].text = VrUiTexts.FinishedHeading;
            headings[(int)SessionState.Fatal].text = VrUiTexts.FatalHeading;
            ResetDisplay();
        }

        private static string PlayingBody(CompletionMode mode)
        {
            switch (mode)
            {
                case CompletionMode.ManualConfirm:
                    return VrUiTexts.PlayingBodyManual;
                case CompletionMode.RequiredItemsOrManual:
                    return VrUiTexts.PlayingBodyEither;
                default:
                    return VrUiTexts.PlayingBodyRequired;
            }
        }

        private void ShowState(SessionState state)
        {
            int index = (int)state;
            if (index < 0 || index >= StateCount)
            {
                return;
            }

            for (int i = 0; i < StateCount; i++)
            {
                bool active = i == index;
                if (statePanels[i].activeSelf != active)
                {
                    statePanels[i].SetActive(active);
                }
            }

            shownState = state;
            bool showHud = state == SessionState.Countdown || state == SessionState.Playing || IsResultState(state);
            if (hudRoot.activeSelf != showHud)
            {
                hudRoot.SetActive(showHud);
            }

            switch (state)
            {
                case SessionState.Countdown:
                    shownCountdown = int.MinValue;
                    UpdateCountdown();
                    break;
                case SessionState.Completed:
                    FillResult(state, VrUiTexts.CompletedStatus, VrUiStyle.SecondaryText);
                    break;
                case SessionState.Submitting:
                    FillResult(state, VrUiTexts.SubmittingStatus, VrUiStyle.SecondaryText);
                    break;
                case SessionState.SubmissionFailed:
                    FillResult(state, VrUiTexts.SubmissionFailedStatus, VrUiStyle.ErrorText);
                    break;
                case SessionState.Finished:
                    FillFinished();
                    break;
                case SessionState.Fatal:
                    var fatal = session.Fatal;
                    bodies[index].text = (fatal != null ? fatal.Message : FatalInfo.MessageFor(FatalReason.Unexpected)) + "\n\n" + VrUiTexts.FatalHint;
                    bodies[index].color = VrUiStyle.ErrorText;
                    break;
            }

            // Force the HUD to refresh with the new state immediately.
            nextRefresh = 0f;
        }

        private static bool IsResultState(SessionState state)
        {
            return state == SessionState.Completed || state == SessionState.Submitting ||
                   state == SessionState.SubmissionFailed || state == SessionState.Finished;
        }

        private void FillResult(SessionState state, string status, Color statusColor)
        {
            int index = (int)state;
            bodies[index].text = BuildSummary();
            statuses[index].text = status;
            statuses[index].color = statusColor;
        }

        private void FillFinished()
        {
            int index = (int)SessionState.Finished;
            var participant = session.Current;
            if (participant != null && participant.Outcome == SessionOutcome.Abandoned)
            {
                headings[index].text = VrUiTexts.AbandonedHeading;
                bodies[index].text = string.Empty;
                statuses[index].text = VrUiTexts.AbandonedStatus;
                return;
            }

            headings[index].text = VrUiTexts.FinishedHeading;
            bodies[index].text = BuildSummary();
            statuses[index].text = VrUiTexts.FinishedStatus;
            statuses[index].color = VrUiStyle.Teal;
        }

        private string BuildSummary()
        {
            var participant = session.Current;
            var result = participant != null ? participant.Result : null;
            builder.Length = 0;
            if (result == null)
            {
                builder.Append(VrUiTexts.SummaryScoreLabel).Append(scoreService.Score);
                builder.Append('\n').Append(VrUiTexts.SummaryTimeLabel).Append(TimeFormat.FormatTenths(timer.ElapsedMs));
                return builder.ToString();
            }

            builder.Append(VrUiTexts.SummaryScoreLabel).Append(result.Score);
            builder.Append('\n').Append(VrUiTexts.SummaryTimeLabel).Append(TimeFormat.FormatTenths(result.CompletionMs));
            builder.Append('\n').Append(VrUiTexts.SummaryCorrectLabel).Append(result.CorrectCount)
                .Append("   ").Append(VrUiTexts.SummaryIncorrectLabel).Append(result.IncorrectCount);
            var receipt = participant.Receipt;
            if (receipt != null && receipt.Rank.HasValue)
            {
                builder.Append('\n').Append(VrUiTexts.SummaryRankLabel).Append(receipt.Rank.Value).Append('.');
            }

            return builder.ToString();
        }

        private void UpdateCountdown()
        {
            int remaining = director != null ? director.CountdownRemaining : 0;
            if (remaining == shownCountdown)
            {
                return;
            }

            shownCountdown = remaining;
            if (remaining > 0)
            {
                countdownDigits.SetText("{0:0}", remaining);
            }
            else
            {
                countdownDigits.text = VrUiTexts.CountdownGo;
            }
        }

        private void RefreshHud()
        {
            if (!hudRoot.activeSelf)
            {
                return;
            }

            int score = scoreService.Score;
            if (score != shownScore)
            {
                shownScore = score;
                scoreText.SetText(VrUiTexts.ScoreFormat, score);
            }

            long tenths = timer.ElapsedMs / 100;
            if (tenths != shownTenths)
            {
                shownTenths = tenths;
                timerText.text = VrUiTexts.TimerPrefix + TimeFormat.FormatTenths(timer.ElapsedMs);
            }

            int placed = completionEvaluator.RequiredPlacedCount;
            int required = completionEvaluator.RequiredTotal;
            if (placed != shownPlaced || required != shownRequired)
            {
                shownPlaced = placed;
                shownRequired = required;
                progressText.SetText(VrUiTexts.ProgressFormat, placed, required);
            }
        }
    }
}
