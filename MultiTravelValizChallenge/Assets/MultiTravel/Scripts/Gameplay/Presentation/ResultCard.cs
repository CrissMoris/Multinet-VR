using MultiTravel.Core.Session;
using MultiTravel.Core.Utility;
using MultiTravel.Gameplay.Common;
using MultiTravel.Gameplay.Suitcase;
using MultiTravel.Gameplay.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiTravel.Gameplay.Presentation
{
    /// <summary>
    /// World-space result card (0.9 × 0.55 m, OVERHAUL_PLAN §5) shown after the suitcase lid closed: puan, süre,
    /// doğru / yanlış adet, submission status and — when the receipt arrives — the rank with a fade-in. Slides in over
    /// 0.5 s from <see cref="slideOffset"/>. The card is a world-space canvas built once in <c>Awake</c> under this
    /// transform (place it where UI.result is).
    /// <para>
    /// Trigger: <see cref="SuitcaseLid.Closed"/> when a lid is configured, otherwise <see cref="fallbackDelaySeconds"/>
    /// after Completed (the lid animation length).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ResultCard : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField]
        private Vector2 sizeMetres = new Vector2(0.9f, 0.55f);

        [SerializeField]
        [Tooltip("Local offset the card slides in from (metres).")]
        private Vector3 slideOffset = new Vector3(0f, -0.25f, 0f);

        [SerializeField]
        [Min(0.05f)]
        private float slideSeconds = 0.5f;

        [SerializeField]
        [Min(0.05f)]
        private float rankFadeSeconds = 0.45f;

        [Header("Trigger")]
        [SerializeField]
        [Tooltip("Lid whose Closed event shows the card. Optional.")]
        private SuitcaseLid lid;

        [SerializeField]
        [Tooltip("Seconds after Completed when no lid is configured (lid close takes 1.1 s).")]
        [Min(0f)]
        private float fallbackDelaySeconds = 1.2f;

        private SessionController session;
        private bool sessionSubscribed;
        private SuitcaseLid subscribedLid;
        private Canvas canvas;
        private CanvasGroup group;
        private RectTransform cardRect;
        private TextMeshProUGUI titleText;
        private TextMeshProUGUI scoreValue;
        private TextMeshProUGUI timeValue;
        private TextMeshProUGUI countsValue;
        private TextMeshProUGUI rankLabel;
        private TextMeshProUGUI rankValue;
        private TextMeshProUGUI statusText;
        private CanvasGroup rankGroup;
        private Vector3 restPosition;
        private bool shown;
        private bool pending;
        private float pendingAt;
        private float slideStart = -10f;
        private bool sliding;
        private float rankFadeStart = -10f;
        private bool rankShown;
        private SessionState appliedState = (SessionState)(-1);
        private bool built;

        /// <summary>True while the card is visible (or sliding in).</summary>
        public bool IsShown => shown;

        /// <summary>True while waiting for the lid / delay before showing.</summary>
        public bool IsPending => pending;

        /// <summary>True once the rank line is visible.</summary>
        public bool RankShown => rankShown;

        public TextMeshProUGUI ScoreValue => scoreValue;

        public TextMeshProUGUI TimeValue => timeValue;

        public TextMeshProUGUI RankValue => rankValue;

        public TextMeshProUGUI StatusText => statusText;

        /// <summary>Generator API.</summary>
        public void Configure(SuitcaseLid suitcaseLid, Vector2 cardSizeMetres)
        {
            SetLid(suitcaseLid);
            sizeMetres = cardSizeMetres;
        }

        /// <summary>Assigns (and subscribes to) the lid.</summary>
        public void SetLid(SuitcaseLid suitcaseLid)
        {
            if (subscribedLid != null && subscribedLid != suitcaseLid)
            {
                subscribedLid.Closed -= OnLidClosed;
                subscribedLid = null;
            }

            lid = suitcaseLid;
            SubscribeLid();
        }

        /// <summary>Test / generator API: explicit session (null = resolve from AppServices in Start).</summary>
        public void Bind(SessionController sessionController)
        {
            if (sessionController == null || sessionController == session)
            {
                return;
            }

            Unsubscribe();
            session = sessionController;
            Subscribe();
            Build();
            ApplyState(session.State);
        }

        /// <summary>Shows the card now (slide-in).</summary>
        public void Show()
        {
            Build();
            pending = false;
            if (shown)
            {
                return;
            }

            shown = true;
            sliding = true;
            slideStart = Time.unscaledTime;
            canvas.gameObject.SetActive(true);
            group.alpha = 0f;
            cardRect.localPosition = restPosition + slideOffset;
        }

        /// <summary>Hides the card immediately.</summary>
        public void Hide()
        {
            Build();
            pending = false;
            shown = false;
            sliding = false;
            rankShown = false;
            rankGroup.alpha = 0f;
            canvas.gameObject.SetActive(false);
        }

        // ----- Unity -----

        private void Awake()
        {
            Build();
        }

        private void OnEnable()
        {
            Subscribe();
            SubscribeLid();
        }

        private void Start()
        {
            if (session == null)
            {
                if (ServiceResolver.Resolve(ref session, this, nameof(ResultCard)))
                {
                    Subscribe();
                    ApplyState(session.State);
                }
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
            if (subscribedLid != null)
            {
                subscribedLid.Closed -= OnLidClosed;
                subscribedLid = null;
            }
        }

        private void Update()
        {
            if (session == null || !built)
            {
                return;
            }

            if (session.State != appliedState)
            {
                ApplyState(session.State);
            }

            if (pending && Time.unscaledTime >= pendingAt)
            {
                Show();
            }

            if (sliding)
            {
                float k = (Time.unscaledTime - slideStart) / slideSeconds;
                if (k >= 1f)
                {
                    sliding = false;
                    k = 1f;
                }

                float e = PresentationStyle.EaseOutBack(Mathf.Clamp01(k));
                cardRect.localPosition = Vector3.LerpUnclamped(restPosition + slideOffset, restPosition, e);
                group.alpha = PresentationStyle.SmoothStep(Mathf.Min(1f, k * 1.6f));
            }

            if (rankShown && rankGroup.alpha < 1f)
            {
                rankGroup.alpha = PresentationStyle.SmoothStep((Time.unscaledTime - rankFadeStart) / rankFadeSeconds);
            }
        }

        // ----- building -----

        private void Build()
        {
            if (built)
            {
                return;
            }

            built = true;
            var canvasGo = new GameObject("Result card", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            group = canvasGo.AddComponent<CanvasGroup>();
            cardRect = (RectTransform)canvasGo.transform;
            const float unitsPerMetre = 1000f;
            cardRect.sizeDelta = sizeMetres * unitsPerMetre;
            cardRect.localScale = Vector3.one / unitsPerMetre;
            restPosition = Vector3.zero;

            var background = CreateImage("Background", cardRect, VrUiStyle.PanelBackground);
            Stretch(background, Vector2.zero, Vector2.one);
            var accent = CreateImage("Accent", cardRect, PresentationStyle.Teal);
            Stretch(accent, new Vector2(0f, 0.965f), Vector2.one);

            float h = cardRect.sizeDelta.y;
            titleText = CreateText("Title", cardRect, h * 0.16f, PresentationStyle.Ivory, FontStyles.Bold, new Vector2(0.04f, 0.74f), new Vector2(0.96f, 0.94f));

            scoreValue = CreateTile(cardRect, "Puan", new Vector2(0.04f, 0.38f), new Vector2(0.34f, 0.72f), PresentationStyle.Teal);
            timeValue = CreateTile(cardRect, "Süre", new Vector2(0.35f, 0.38f), new Vector2(0.65f, 0.72f), PresentationStyle.Ivory);
            countsValue = CreateTile(cardRect, "Doğru / Yanlış", new Vector2(0.66f, 0.38f), new Vector2(0.96f, 0.72f), PresentationStyle.Orange);

            var rankRow = new GameObject("Rank", typeof(RectTransform));
            rankRow.transform.SetParent(cardRect, false);
            var rankRect = (RectTransform)rankRow.transform;
            Stretch(rankRect, new Vector2(0.04f, 0.19f), new Vector2(0.96f, 0.36f));
            rankGroup = rankRow.AddComponent<CanvasGroup>();
            rankGroup.alpha = 0f;
            rankLabel = CreateText("Label", rankRect, h * 0.1f, VrUiStyle.SecondaryText, FontStyles.Normal, new Vector2(0f, 0f), new Vector2(0.5f, 1f));
            rankLabel.alignment = TextAlignmentOptions.MidlineRight;
            rankLabel.text = VrUiTexts.SummaryRankLabel;
            rankValue = CreateText("Value", rankRect, h * 0.15f, PresentationStyle.Teal, FontStyles.Bold, new Vector2(0.52f, 0f), new Vector2(1f, 1f));
            rankValue.alignment = TextAlignmentOptions.MidlineLeft;

            statusText = CreateText("Status", cardRect, h * 0.085f, VrUiStyle.SecondaryText, FontStyles.Normal, new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.18f));
            canvasGo.SetActive(false);
        }

        private TextMeshProUGUI CreateTile(RectTransform parent, string label, Vector2 anchorMin, Vector2 anchorMax, Color valueColor)
        {
            var tile = CreateImage(label, parent, VrUiStyle.PanelStrip);
            Stretch(tile, anchorMin, anchorMax);
            float h = parent.sizeDelta.y;
            var caption = CreateText("Label", tile, h * 0.075f, VrUiStyle.SecondaryText, FontStyles.Normal, new Vector2(0f, 0.68f), new Vector2(1f, 1f));
            caption.text = label;
            var value = CreateText("Value", tile, h * 0.17f, valueColor, FontStyles.Bold, new Vector2(0f, 0f), new Vector2(1f, 0.7f));
            value.enableAutoSizing = true;
            value.fontSizeMax = h * 0.17f;
            value.fontSizeMin = h * 0.07f;
            return value;
        }

        private static RectTransform CreateImage(string name, RectTransform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return (RectTransform)go.transform;
        }

        private static TextMeshProUGUI CreateText(string name, RectTransform parent, float size, Color color, FontStyles style, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            Stretch(rect, anchorMin, anchorMax);
            var text = go.AddComponent<TextMeshProUGUI>();
            var font = VrUiStyle.Font;
            if (font != null)
            {
                text.font = font;
            }

            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            text.text = string.Empty;
            return text;
        }

        private static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        // ----- content -----

        private void ApplyState(SessionState state)
        {
            Build();
            appliedState = state;
            switch (state)
            {
                case SessionState.Completed:
                    FillResult();
                    statusText.text = VrUiTexts.CompletedStatus;
                    statusText.color = VrUiStyle.SecondaryText;
                    if (!shown && !pending)
                    {
                        pending = true;
                        pendingAt = Time.unscaledTime + (lid != null ? float.MaxValue : fallbackDelaySeconds);
                    }

                    break;
                case SessionState.Submitting:
                    FillResult();
                    statusText.text = VrUiTexts.SubmittingStatus;
                    statusText.color = VrUiStyle.SecondaryText;
                    if (!shown && !pending)
                    {
                        pending = true;
                        pendingAt = Time.unscaledTime + (lid != null ? float.MaxValue : fallbackDelaySeconds);
                    }

                    break;
                case SessionState.SubmissionFailed:
                    FillResult();
                    statusText.text = VrUiTexts.SubmissionFailedStatus;
                    statusText.color = VrUiStyle.ErrorText;
                    if (!shown && !pending)
                    {
                        Show();
                    }

                    break;
                case SessionState.Finished:
                    var participant = session.Current;
                    if (participant != null && participant.Outcome == SessionOutcome.Abandoned)
                    {
                        Hide();
                        break;
                    }

                    FillResult();
                    statusText.text = VrUiTexts.FinishedStatus;
                    statusText.color = PresentationStyle.Teal;
                    if (!shown)
                    {
                        Show();
                    }

                    ShowRank();
                    break;
                default:
                    Hide();
                    break;
            }
        }

        private void FillResult()
        {
            var participant = session.Current;
            var result = participant != null ? participant.Result : null;
            titleText.text = participant != null && !string.IsNullOrEmpty(participant.Input.FirstName)
                ? "Tebrikler, " + participant.Input.FirstName + "!"
                : VrUiTexts.CompletedHeading;
            if (result == null)
            {
                scoreValue.text = "0";
                timeValue.text = TimeFormat.FormatTenths(0);
                countsValue.text = "0 / 0";
                return;
            }

            scoreValue.SetText("{0}", result.Score);
            timeValue.text = TimeFormat.FormatTenths(result.CompletionMs);
            countsValue.SetText("{0} / {1}", result.CorrectCount, result.IncorrectCount);
        }

        private void ShowRank()
        {
            var participant = session.Current;
            var receipt = participant != null ? participant.Receipt : null;
            if (receipt == null || !receipt.Rank.HasValue || rankShown)
            {
                return;
            }

            rankValue.SetText("{0}.", receipt.Rank.Value);
            rankShown = true;
            rankFadeStart = Time.unscaledTime;
            rankGroup.alpha = 0f;
        }

        private void OnLidClosed()
        {
            if (pending || (session != null && (session.State == SessionState.Completed || session.State == SessionState.Submitting)))
            {
                Show();
            }
        }

        private void SubscribeLid()
        {
            if (lid == null || subscribedLid == lid)
            {
                return;
            }

            lid.Closed += OnLidClosed;
            subscribedLid = lid;
        }

        private void Subscribe()
        {
            if (session == null || sessionSubscribed)
            {
                return;
            }

            session.StateChanged += OnStateChanged;
            session.SessionResetRequested += Hide;
            sessionSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (session != null && sessionSubscribed)
            {
                session.StateChanged -= OnStateChanged;
                session.SessionResetRequested -= Hide;
            }

            sessionSubscribed = false;
        }

        private void OnStateChanged(SessionState previous, SessionState next)
        {
            if (isActiveAndEnabled)
            {
                ApplyState(next);
            }
        }
    }
}
