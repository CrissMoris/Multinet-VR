using System.Text;
using MultiTravel.Core.Completion;
using MultiTravel.Core.Scoring;
using MultiTravel.Core.Session;
using MultiTravel.Gameplay.Common;
using TMPro;
using UnityEngine;

namespace MultiTravel.Gameplay.Presentation
{
    /// <summary>
    /// The scoreboard right of the stopwatch (OVERHAUL_PLAN §5): participant greeting ("Hoş geldin, Ayşe"), the score
    /// with a 0.4 s roll-up and a teal / orange flash on changes, required-item pips <c>n/N</c> (filled teal), and
    /// attract content while the session is in Welcome (fed by <see cref="AttractMode"/>).
    /// Four TMP texts are created under <see cref="anchor"/> (UI.scoreboard) when none are assigned.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScoreboardDisplay : MonoBehaviour
    {
        /// <summary>Greeting prefix.</summary>
        public const string GreetingPrefix = "Hoş geldin, ";

        /// <summary>Thanks prefix (Finished).</summary>
        public const string ThanksPrefix = "Teşekkürler, ";

        [Header("Layout")]
        [SerializeField]
        [Tooltip("Where texts are created (UI.scoreboard). Defaults to this transform. Local +Y up, text faces -Z.")]
        private Transform anchor;

        [SerializeField]
        [Tooltip("Board size in metres used to lay out the generated texts.")]
        private Vector2 boardSize = new Vector2(1.0f, 0.45f);

        [SerializeField] private TMP_Text headlineText;
        [SerializeField] private TMP_Text scoreText;
        [SerializeField] private TMP_Text pipsText;
        [SerializeField] private TMP_Text progressText;

        [Header("Animation")]
        [SerializeField]
        [Min(0.05f)]
        private float rollUpSeconds = 0.4f;

        [SerializeField]
        [Min(0.05f)]
        private float flashSeconds = 0.6f;

        [SerializeField]
        [Tooltip("Maximum number of pips drawn (larger required sets fall back to the n/N text only).")]
        [Range(4, 40)]
        private int maxPips = 24;

        private readonly StringBuilder builder = new StringBuilder(512);
        private SessionController session;
        private ScoreService scoreService;
        private CompletionEvaluator completionEvaluator;
        private bool sessionSubscribed;
        private bool scoreSubscribed;
        private SessionState shownState = (SessionState)(-1);
        private float displayedScore;
        private int shownScore = int.MinValue;
        private int targetScore;
        private float rollFrom;
        private float rollStart = -10f;
        private bool rolling;
        private Color flashColor = Color.white;
        private float flashStart = -10f;
        private int shownPlaced = int.MinValue;
        private int shownRequired = int.MinValue;
        private bool attract;
        private bool built;

        /// <summary>Score currently shown (roll-up position).</summary>
        public int DisplayedScore => shownScore == int.MinValue ? 0 : shownScore;

        /// <summary>True while the roll-up animation runs.</summary>
        public bool IsRolling => rolling;

        /// <summary>True while attract content is shown (Welcome).</summary>
        public bool IsAttract => attract;

        public TMP_Text Headline => headlineText;

        public TMP_Text Score => scoreText;

        public TMP_Text Pips => pipsText;

        public TMP_Text Progress => progressText;

        /// <summary>Generator API.</summary>
        public void Configure(Transform boardAnchor, Vector2 sizeMetres)
        {
            anchor = boardAnchor;
            boardSize = sizeMetres;
        }

        /// <summary>Test / generator API: explicit services (null = resolve from AppServices in Start).</summary>
        public void Bind(SessionController sessionController, ScoreService score, CompletionEvaluator evaluator)
        {
            if (sessionController != null && sessionController != session)
            {
                UnsubscribeSession();
                session = sessionController;
                SubscribeSession();
            }

            if (score != null && score != scoreService)
            {
                UnsubscribeScore();
                scoreService = score;
                SubscribeScore();
            }

            if (evaluator != null)
            {
                completionEvaluator = evaluator;
            }

            Build();
            if (session != null)
            {
                ApplyState(session.State);
            }
        }

        /// <summary>
        /// Attract content (Welcome only): a headline and a body line on the score area. Ignored outside Welcome.
        /// Same strings are not re-assigned.
        /// </summary>
        public void ShowAttract(string headline, string body)
        {
            Build();
            if (!attract)
            {
                return;
            }

            if (headlineText.text != headline)
            {
                headlineText.text = headline ?? string.Empty;
            }

            if (scoreText.text != body)
            {
                scoreText.text = body ?? string.Empty;
            }
        }

        // ----- Unity -----

        private void Awake()
        {
            Build();
        }

        private void OnEnable()
        {
            SubscribeSession();
            SubscribeScore();
        }

        private void Start()
        {
            bool ok = true;
            if (session == null)
            {
                ok &= ServiceResolver.Resolve(ref session, this, nameof(ScoreboardDisplay));
                SubscribeSession();
            }

            if (scoreService == null)
            {
                ok &= ServiceResolver.Resolve(ref scoreService, this, nameof(ScoreboardDisplay));
                SubscribeScore();
            }

            if (completionEvaluator == null)
            {
                ok &= ServiceResolver.Resolve(ref completionEvaluator, this, nameof(ScoreboardDisplay));
            }

            if (ok)
            {
                ApplyState(session.State);
            }
        }

        private void OnDisable()
        {
            UnsubscribeSession();
            UnsubscribeScore();
        }

        private void Update()
        {
            if (session == null || !built)
            {
                return;
            }

            var state = session.State;
            if (state != shownState)
            {
                ApplyState(state);
            }

            if (attract)
            {
                return;
            }

            UpdateRoll();
            UpdateFlash();
            UpdatePips();
        }

        // ----- internals -----

        private void Build()
        {
            if (built)
            {
                return;
            }

            built = true;
            var root = anchor != null ? anchor : transform;
            float w = boardSize.x;
            float h = boardSize.y;
            if (headlineText == null)
            {
                var text = PresentationStyle.CreateText("Headline", root, h * 0.55f, PresentationStyle.Ivory, TextAlignmentOptions.Center, new Vector2(w * 0.94f, h * 0.22f));
                text.transform.localPosition = new Vector3(0f, h * 0.34f, 0f);
                text.overflowMode = TextOverflowModes.Ellipsis;
                headlineText = text;
            }

            if (scoreText == null)
            {
                var text = PresentationStyle.CreateText("Score", root, h * 1.45f, PresentationStyle.Ivory, TextAlignmentOptions.Center, new Vector2(w * 0.94f, h * 0.42f));
                text.transform.localPosition = new Vector3(0f, h * 0.02f, 0f);
                text.fontStyle = FontStyles.Bold;
                text.overflowMode = TextOverflowModes.Ellipsis;
                scoreText = text;
            }

            if (pipsText == null)
            {
                var text = PresentationStyle.CreateText("Pips", root, h * 0.5f, PresentationStyle.Teal, TextAlignmentOptions.Center, new Vector2(w * 0.94f, h * 0.16f));
                text.transform.localPosition = new Vector3(0f, -h * 0.26f, 0f);
                text.characterSpacing = 6f;
                pipsText = text;
            }

            if (progressText == null)
            {
                var text = PresentationStyle.CreateText("Progress", root, h * 0.4f, PresentationStyle.Teal, TextAlignmentOptions.Center, new Vector2(w * 0.94f, h * 0.14f));
                text.transform.localPosition = new Vector3(0f, -h * 0.41f, 0f);
                progressText = text;
            }
        }

        private void ApplyState(SessionState state)
        {
            Build();
            shownState = state;
            shownPlaced = int.MinValue;
            shownRequired = int.MinValue;
            bool wasAttract = attract;
            attract = state == SessionState.Welcome;
            if (attract)
            {
                if (!wasAttract)
                {
                    headlineText.text = string.Empty;
                    scoreText.text = string.Empty;
                    scoreText.color = PresentationStyle.Ivory;
                }

                pipsText.text = string.Empty;
                progressText.text = string.Empty;
                ResetScore();
                return;
            }

            var participant = session.Current;
            string firstName = participant != null ? participant.Input.FirstName : null;
            switch (state)
            {
                case SessionState.Finished:
                    SetGreeting(participant != null && participant.Outcome == SessionOutcome.Abandoned ? GreetingPrefix : ThanksPrefix, firstName);
                    break;
                case SessionState.Fatal:
                    headlineText.text = string.Empty;
                    break;
                default:
                    SetGreeting(GreetingPrefix, firstName);
                    break;
            }

            if (state == SessionState.Registration || state == SessionState.GenderSelection || state == SessionState.Instructions || state == SessionState.Loading)
            {
                ResetScore();
                pipsText.text = string.Empty;
                progressText.text = string.Empty;
            }
            else if (state == SessionState.Countdown)
            {
                ResetScore();
                UpdatePips();
            }
            else
            {
                // Playing / Completed / Submitting / Failed / Finished: show the live or final score without an animation jump.
                int current = CurrentScore();
                if (shownScore == int.MinValue)
                {
                    SnapScore(current);
                }
                else if (current != targetScore)
                {
                    StartRoll(current);
                }

                UpdatePips();
            }
        }

        private void SetGreeting(string prefix, string firstName)
        {
            builder.Length = 0;
            if (string.IsNullOrEmpty(firstName))
            {
                builder.Append(prefix.TrimEnd(' ', ','));
            }
            else
            {
                builder.Append(prefix).Append(firstName);
            }

            if (prefix == ThanksPrefix)
            {
                builder.Append('!');
            }

            headlineText.SetText(builder);
        }

        private int CurrentScore()
        {
            var participant = session.Current;
            if (participant != null && participant.Result != null && session.State != SessionState.Playing)
            {
                return participant.Result.Score;
            }

            return scoreService != null ? scoreService.Score : 0;
        }

        private void ResetScore()
        {
            rolling = false;
            targetScore = 0;
            displayedScore = 0f;
            SetScoreText(0);
            scoreText.color = PresentationStyle.Ivory;
            flashStart = -10f;
        }

        private void SnapScore(int value)
        {
            rolling = false;
            targetScore = value;
            displayedScore = value;
            SetScoreText(value);
        }

        private void StartRoll(int value)
        {
            rollFrom = displayedScore;
            targetScore = value;
            rollStart = Time.unscaledTime;
            rolling = true;
        }

        private void UpdateRoll()
        {
            if (!rolling)
            {
                return;
            }

            float k = (Time.unscaledTime - rollStart) / rollUpSeconds;
            if (k >= 1f)
            {
                rolling = false;
                displayedScore = targetScore;
            }
            else
            {
                displayedScore = Mathf.Lerp(rollFrom, targetScore, PresentationStyle.SmoothStep(k));
            }

            SetScoreText(Mathf.RoundToInt(displayedScore));
        }

        private void SetScoreText(int value)
        {
            if (value == shownScore)
            {
                return;
            }

            shownScore = value;
            scoreText.SetText("{0}", value);
        }

        private void UpdateFlash()
        {
            float k = (Time.unscaledTime - flashStart) / flashSeconds;
            if (k > 1.05f)
            {
                if (scoreText.color != PresentationStyle.Ivory)
                {
                    scoreText.color = PresentationStyle.Ivory;
                }

                return;
            }

            scoreText.color = Color.Lerp(flashColor, PresentationStyle.Ivory, PresentationStyle.SmoothStep(k));
        }

        private void UpdatePips()
        {
            if (completionEvaluator == null)
            {
                return;
            }

            int placed = completionEvaluator.RequiredPlacedCount;
            int required = completionEvaluator.RequiredTotal;
            if (placed == shownPlaced && required == shownRequired)
            {
                return;
            }

            shownPlaced = placed;
            shownRequired = required;
            if (required <= 0)
            {
                pipsText.text = string.Empty;
                progressText.text = string.Empty;
                return;
            }

            progressText.SetText("{0}/{1}", placed, required);
            if (required > maxPips)
            {
                pipsText.text = string.Empty;
                return;
            }

            builder.Length = 0;
            for (int i = 0; i < required; i++)
            {
                if (i < placed)
                {
                    builder.Append("<color=").Append(PresentationStyle.TealHex).Append(">●</color>");
                }
                else
                {
                    builder.Append("<color=").Append(PresentationStyle.DimHex).Append(">○</color>");
                }
            }

            pipsText.SetText(builder);
        }

        private void SubscribeSession()
        {
            if (session == null || sessionSubscribed)
            {
                return;
            }

            session.StateChanged += OnStateChanged;
            sessionSubscribed = true;
        }

        private void UnsubscribeSession()
        {
            if (session != null && sessionSubscribed)
            {
                session.StateChanged -= OnStateChanged;
            }

            sessionSubscribed = false;
        }

        private void SubscribeScore()
        {
            if (scoreService == null || scoreSubscribed)
            {
                return;
            }

            scoreService.Changed += OnScoreChanged;
            scoreSubscribed = true;
        }

        private void UnsubscribeScore()
        {
            if (scoreService != null && scoreSubscribed)
            {
                scoreService.Changed -= OnScoreChanged;
            }

            scoreSubscribed = false;
        }

        private void OnStateChanged(SessionState previous, SessionState next)
        {
            if (isActiveAndEnabled)
            {
                ApplyState(next);
            }
        }

        private void OnScoreChanged(ScoreChange change)
        {
            if (!built || attract)
            {
                return;
            }

            if (change.Reason == ScoreChangeReason.Reset)
            {
                ResetScore();
                return;
            }

            StartRoll(change.NewTotal);
            flashColor = change.Delta >= 0 ? PresentationStyle.Teal : PresentationStyle.Orange;
            flashStart = Time.unscaledTime;
        }
    }
}
