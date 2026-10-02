using MultiTravel.Core.Session;
using MultiTravel.Core.Timing;
using MultiTravel.Gameplay.Audio;
using MultiTravel.Gameplay.Common;
using MultiTravel.Gameplay.Director;
using MultiTravel.Gameplay.UI;
using TMPro;
using UnityEngine;

namespace MultiTravel.Gameplay.Presentation
{
    /// <summary>
    /// Drives the stopwatch model on the backdrop (OVERHAUL_PLAN §5): the needle (one revolution per minute) and the TMP
    /// digits on the face. Idle shows the wordmark, the countdown shows big 3-2-1 digits with a scale pop and "BAŞLA!",
    /// play shows <c>mm:ss.f</c> from the Core <see cref="GameTimer"/> (never frame counting) with a faint 1 Hz tick
    /// through <see cref="AudioDirector"/>, and completion freezes the final time with a pulse.
    /// Strings are only rebuilt when the displayed tenth changes (TMP <c>SetText</c>, no allocations per frame).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StopwatchDisplay : MonoBehaviour
    {
        /// <summary>Text shown while idle.</summary>
        public const string IdleText = "MultiTravel";

        /// <summary>Text shown when the countdown reaches zero.</summary>
        public const string StartText = "BAŞLA!";

        [Header("Model")]
        [SerializeField]
        [Tooltip("Needle pivot (PIVOT.needle). Rotated about needleAxis, 6° per second.")]
        private Transform needle;

        [SerializeField]
        [Tooltip("Local axis of the needle rotation (default: clockwise seen from the front of a -Z facing face).")]
        private Vector3 needleAxis = Vector3.back;

        [SerializeField]
        [Tooltip("Where the digits are created when no TMP text is assigned (UI.stopwatch_face). Defaults to this transform.")]
        private Transform digitsAnchor;

        [SerializeField]
        [Tooltip("Optional existing TMP text for the digits; created under digitsAnchor when empty.")]
        private TMP_Text digits;

        [SerializeField]
        [Tooltip("Optional small caption under the digits (\"Hazır ol!\" during the countdown).")]
        private TMP_Text caption;

        [Header("Sizes (TMP 3D units)")]
        [SerializeField] private float idleFontSize = 0.9f;
        [SerializeField] private float countdownFontSize = 2.6f;
        [SerializeField] private float playingFontSize = 1.5f;
        [SerializeField] private float captionFontSize = 0.45f;

        [Header("Hooks (optional)")]
        [SerializeField] private GameplayDirector director;
        [SerializeField] private AudioDirector audioDirector;

        [SerializeField]
        [Tooltip("Play the faint 1 Hz tick through the AudioDirector while Playing.")]
        private bool tickSound = true;

        private SessionController session;
        private GameTimer timer;
        private bool sessionSubscribed;
        private GameplayDirector subscribedDirector;
        private SessionState shownState = (SessionState)(-1);
        private long shownTenths = long.MinValue;
        private long lastTickSecond = -1;
        private int shownCountdown = int.MinValue;
        private float needleAngle;
        private Vector3 digitsBaseScale = Vector3.one;
        private float popStart = -10f;
        private float popSeconds;
        private float popAmount;
        private bool frozen;
        private long frozenMs;
        private bool built;

        /// <summary>Current digits string (tests).</summary>
        public string DigitsText => digits != null ? digits.text : string.Empty;

        /// <summary>Needle angle in degrees (0 = top, 6°/s).</summary>
        public float NeedleAngle => needleAngle;

        /// <summary>The digits text component.</summary>
        public TMP_Text Digits => digits;

        /// <summary>Generator API.</summary>
        public void Configure(Transform needlePivot, Transform faceAnchor, GameplayDirector gameplayDirector, AudioDirector audio)
        {
            needle = needlePivot;
            digitsAnchor = faceAnchor;
            audioDirector = audio;
            if (subscribedDirector != null && subscribedDirector != gameplayDirector)
            {
                subscribedDirector.CountdownTick -= OnCountdownTick;
                subscribedDirector = null;
            }

            director = gameplayDirector;
            SubscribeDirector();
        }

        /// <summary>Test / generator API: explicit services (null = resolve from AppServices in Start).</summary>
        public void Bind(SessionController sessionController, GameTimer gameTimer)
        {
            if (sessionController != null && sessionController != session)
            {
                UnsubscribeSession();
                session = sessionController;
                SubscribeSession();
            }

            if (gameTimer != null)
            {
                timer = gameTimer;
            }

            Build();
            if (session != null)
            {
                ApplyState(session.State);
            }
        }

        /// <summary>Countdown digit (n &gt; 0) or the start text (0) with a scale pop. Also wired to <see cref="GameplayDirector.CountdownTick"/>.</summary>
        public void OnCountdownTick(int remaining)
        {
            Build();
            if (remaining == shownCountdown)
            {
                return;
            }

            shownCountdown = remaining;
            digits.fontSize = countdownFontSize;
            if (remaining > 0)
            {
                digits.SetText("{0}", remaining);
            }
            else
            {
                digits.text = StartText;
            }

            Pop(0.3f, 0.3f);
        }

        // ----- Unity -----

        private void Awake()
        {
            Build();
        }

        private void OnEnable()
        {
            SubscribeSession();
            SubscribeDirector();
        }

        private void Start()
        {
            bool ok = true;
            if (session == null)
            {
                ok &= ServiceResolver.Resolve(ref session, this, nameof(StopwatchDisplay));
                SubscribeSession();
            }

            if (timer == null)
            {
                ok &= ServiceResolver.Resolve(ref timer, this, nameof(StopwatchDisplay));
            }

            if (director == null)
            {
                director = FindAnyObjectByType<GameplayDirector>();
                SubscribeDirector();
            }

            if (ok && session != null)
            {
                ApplyState(session.State);
            }
        }

        private void OnDisable()
        {
            UnsubscribeSession();
            if (subscribedDirector != null)
            {
                subscribedDirector.CountdownTick -= OnCountdownTick;
                subscribedDirector = null;
            }
        }

        private void Update()
        {
            if (session == null || timer == null || !built)
            {
                return;
            }

            var state = session.State;
            if (state != shownState)
            {
                ApplyState(state);
            }

            if (state == SessionState.Playing)
            {
                long elapsed = timer.ElapsedMs;
                SetTime(elapsed);
                long second = elapsed / 1000;
                if (second != lastTickSecond)
                {
                    lastTickSecond = second;
                    if (tickSound && audioDirector != null && second > 0)
                    {
                        audioDirector.PlayStopwatchTick(transform.position);
                    }
                }
            }
            else if (frozen)
            {
                SetTime(frozenMs);
            }

            UpdatePop();
        }

        // ----- internals -----

        private void Build()
        {
            if (built)
            {
                return;
            }

            built = true;
            var anchor = digitsAnchor != null ? digitsAnchor : transform;
            if (digits == null)
            {
                digits = PresentationStyle.CreateText("Digits", anchor, playingFontSize, PresentationStyle.Ivory, TextAlignmentOptions.Center, new Vector2(0.4f, 0.16f));
                digits.fontStyle = FontStyles.Bold;
            }

            if (caption == null)
            {
                var text = PresentationStyle.CreateText("Caption", anchor, captionFontSize, PresentationStyle.Teal, TextAlignmentOptions.Center, new Vector2(0.4f, 0.06f));
                text.transform.localPosition = new Vector3(0f, -0.075f, 0f);
                caption = text;
            }

            digitsBaseScale = digits.transform.localScale;
            digits.text = IdleText;
            digits.fontSize = idleFontSize;
            caption.text = string.Empty;
        }

        private void ApplyState(SessionState state)
        {
            Build();
            shownState = state;
            shownTenths = long.MinValue;
            shownCountdown = int.MinValue;
            switch (state)
            {
                case SessionState.Countdown:
                    frozen = false;
                    caption.text = VrUiTexts.CountdownHeading;
                    digits.fontSize = countdownFontSize;
                    int remaining = director != null && director.CountdownRemaining > 0 ? director.CountdownRemaining : session.CountdownSeconds;
                    OnCountdownTick(remaining);
                    break;
                case SessionState.Playing:
                    frozen = false;
                    lastTickSecond = -1;
                    caption.text = string.Empty;
                    digits.fontSize = playingFontSize;
                    SetTime(timer.ElapsedMs);
                    Pop(0.15f, 0.25f);
                    break;
                case SessionState.Completed:
                case SessionState.Submitting:
                case SessionState.SubmissionFailed:
                case SessionState.Finished:
                    var participant = session.Current;
                    var result = participant != null ? participant.Result : null;
                    long ms = result != null ? result.CompletionMs : timer.ElapsedMs;
                    bool wasFrozen = frozen;
                    frozen = true;
                    frozenMs = ms;
                    digits.fontSize = playingFontSize;
                    caption.text = participant != null && participant.Outcome == SessionOutcome.Abandoned ? string.Empty : VrUiTexts.CompletedHeading;
                    SetTime(ms);
                    if (!wasFrozen)
                    {
                        Pop(0.2f, 0.5f);
                    }

                    break;
                case SessionState.Loading:
                    frozen = false;
                    ResetNeedle();
                    digits.fontSize = idleFontSize;
                    digits.text = VrUiTexts.LoadingHeading;
                    caption.text = string.Empty;
                    break;
                default:
                    frozen = false;
                    ResetNeedle();
                    digits.fontSize = idleFontSize;
                    digits.text = IdleText;
                    caption.text = string.Empty;
                    break;
            }
        }

        private void SetTime(long elapsedMs)
        {
            if (elapsedMs < 0)
            {
                elapsedMs = 0;
            }

            long tenths = elapsedMs / 100;
            if (tenths != shownTenths)
            {
                shownTenths = tenths;
                long totalSeconds = tenths / 10;
                digits.SetText("{0:00}:{1:00}.{2}", totalSeconds / 60, totalSeconds % 60, tenths % 10);
            }

            needleAngle = (float)((elapsedMs / 1000d * 6d) % 360d);
            if (needle != null)
            {
                needle.localRotation = Quaternion.AngleAxis(needleAngle, needleAxis);
            }
        }

        private void ResetNeedle()
        {
            needleAngle = 0f;
            if (needle != null)
            {
                needle.localRotation = Quaternion.identity;
            }
        }

        private void Pop(float amount, float seconds)
        {
            popAmount = amount;
            popSeconds = Mathf.Max(0.01f, seconds);
            popStart = Time.unscaledTime;
        }

        private void UpdatePop()
        {
            float k = (Time.unscaledTime - popStart) / popSeconds;
            if (k > 1.05f)
            {
                if (digits.transform.localScale != digitsBaseScale)
                {
                    digits.transform.localScale = digitsBaseScale;
                }

                return;
            }

            // Scale pop: starts at 1 + amount and eases back to 1 (countdown spec 1.3 → 1.0).
            float scale = 1f + popAmount * (1f - PresentationStyle.SmoothStep(Mathf.Clamp01(k)));
            digits.transform.localScale = digitsBaseScale * scale;
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

        private void SubscribeDirector()
        {
            if (director == null || subscribedDirector == director)
            {
                return;
            }

            director.CountdownTick += OnCountdownTick;
            subscribedDirector = director;
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
