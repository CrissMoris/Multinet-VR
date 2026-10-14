using MultiTravel.Core.Completion;
using MultiTravel.Core.Session;
using MultiTravel.Core.Timing;
using MultiTravel.Gameplay.Audio;
using MultiTravel.Gameplay.Common;
using MultiTravel.Gameplay.Director;
using MultiTravel.Gameplay.UI;
using TMPro;
using UnityEngine;
using Math = System.Math;

namespace MultiTravel.Gameplay.Presentation
{
    /// <summary>
    /// Drives the stopwatch model on the backdrop (OVERHAUL_PLAN §5): the needle (one revolution per minute) and the TMP
    /// digits on the face. Idle shows the wordmark, the countdown shows big 3-2-1 digits with a scale pop and "BAŞLA!",
    /// play shows <c>mm:ss.f</c> from the Core <see cref="GameTimer"/> (never frame counting) with a faint 1 Hz tick
    /// through <see cref="AudioDirector"/>, and completion freezes the final time with a pulse.
    /// Strings are only rebuilt when the displayed tenth changes (TMP <c>SetText</c>, no allocations per frame).
    /// <para>
    /// With a time limit (<see cref="CompletionEvaluator.TimeLimitSeconds"/> &gt; 0) the stopwatch counts DOWN: the digits
    /// show the remaining time, the needle sweeps back to zero, and the last <see cref="warningSeconds"/> turn the digits
    /// orange with a pop and a beep per second. A small lazy-follow label below the participant's gaze mirrors the
    /// remaining time while playing, so it is visible wherever they look.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StopwatchDisplay : MonoBehaviour
    {
        /// <summary>Text shown while idle.</summary>
        public const string IdleText = "MultiTravel";

        /// <summary>Text shown when the countdown reaches zero.</summary>
        public const string StartText = "BAŞLA!";

        /// <summary>Shown (and said) before the 3-2-1 numbers.</summary>
        public const string ReadyText = "HAZIR OL!";

        /// <summary>Resources folder of the spoken countdown clips (countdown_hazir_ol, _3, _2, _1, _basla).</summary>
        public const string VoiceResourcePrefix = "Voice/countdown_";

        /// <summary>Caption under the digits while counting down.</summary>
        public const string RemainingCaption = "KALAN SÜRE";

        /// <summary>Caption when the time limit ended the game.</summary>
        public const string TimeUpCaption = "SÜRE DOLDU";

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

        [Header("Countdown (time limit)")]
        [SerializeField]
        [Tooltip("Last seconds highlighted in orange with a pop per second.")]
        [Min(0)]
        private int warningSeconds = 10;

        [SerializeField]
        [Tooltip("Last seconds that also play a warning beep.")]
        [Min(0)]
        private int beepSeconds = 5;

        [SerializeField]
        [Tooltip("Show the remaining time on a small label that follows the participant's gaze while playing.")]
        private bool gazeLabel = true;

        [SerializeField]
        [Tooltip("Say \"Hazır ol! Üç, iki, bir, başla!\" (Turkish voice clips under Resources/Voice).")]
        private bool voiceCountdown = true;

        [SerializeField]
        [Range(0f, 1f)]
        private float voiceVolume = 1f;

        [SerializeField] private float gazeCountdownDrop = 0.05f;
        [SerializeField] private float gazeLabelDistance = 1.3f;
        [SerializeField] private float gazeLabelDrop = 0.32f;
        [SerializeField] private float gazeLabelFontSize = 0.55f;

        private SessionController session;
        private GameTimer timer;
        private CompletionEvaluator completion;
        private TextMeshPro gazeText;
        private Camera gazeCamera;
        private bool gazePlaced;
        private bool warning;
        private AudioSource voice;
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

        /// <summary>Time limit in milliseconds (0 = no limit: the stopwatch counts up).</summary>
        public long LimitMs => completion != null ? completion.TimeLimitSeconds * 1000L : 0L;

        /// <summary>True while the stopwatch counts down.</summary>
        public bool CountsDown => LimitMs > 0;

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
        public void Bind(SessionController sessionController, GameTimer gameTimer, CompletionEvaluator evaluator = null)
        {
            if (evaluator != null)
            {
                completion = evaluator;
            }

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

            if (gazeText != null && gazeText.gameObject.activeInHierarchy)
            {
                gazeText.text = remaining > 0 ? remaining.ToString() : StartText;
            }

            PlayVoice(remaining > 0 ? remaining.ToString() : "basla");

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

            if (completion == null)
            {
                ServiceResolver.TryResolve(ref completion);
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

            ShowGazeLabel(false);
        }

        private void OnDestroy()
        {
            if (gazeText != null)
            {
                Destroy(gazeText.transform.parent.gameObject);
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
                    OnSecond(elapsed, second);
                }

                UpdateGazeLabel();
            }
            else if (state == SessionState.Countdown)
            {
                UpdateGazeLabel();
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
                    SetWarning(false);
                    caption.text = VrUiTexts.CountdownHeading;
                    digits.fontSize = countdownFontSize;
                    gazePlaced = false;
                    ShowGazeLabel(gazeLabel);
                    if (gazeText != null)
                    {
                        gazeText.fontSize = gazeLabelFontSize * 2f;
                    }

                    if (director != null && director.HasCountdownLeadIn && director.CountdownRemaining <= 0)
                    {
                        // Lead-in: "HAZIR OL!" until the director's first tick (3).
                        digits.fontSize = idleFontSize;
                        digits.text = ReadyText;
                        if (gazeText != null)
                        {
                            gazeText.text = ReadyText;
                        }

                        PlayVoice("hazir_ol");
                        Pop(0.2f, 0.4f);
                    }
                    else
                    {
                        int remaining = director != null && director.CountdownRemaining > 0 ? director.CountdownRemaining : session.CountdownSeconds;
                        OnCountdownTick(remaining);
                    }

                    break;
                case SessionState.Playing:
                    frozen = false;
                    lastTickSecond = -1;
                    caption.text = CountsDown ? RemainingCaption : string.Empty;
                    digits.fontSize = playingFontSize;
                    SetWarning(false);
                    SetTime(timer.ElapsedMs);
                    Pop(0.15f, 0.25f);
                    gazePlaced = false;
                    ShowGazeLabel(gazeLabel && CountsDown);
                    if (gazeText != null)
                    {
                        gazeText.fontSize = gazeLabelFontSize;
                    }
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
                    ShowGazeLabel(false);
                    SetWarning(false);
                    bool timeUp = result != null && result.Reason == CompletionReason.TimeLimit;
                    caption.text = participant != null && participant.Outcome == SessionOutcome.Abandoned
                        ? string.Empty
                        : timeUp ? TimeUpCaption : VrUiTexts.CompletedHeading;
                    SetTime(ms);
                    if (!wasFrozen)
                    {
                        Pop(0.2f, 0.5f);
                    }

                    break;
                case SessionState.Loading:
                    frozen = false;
                    ShowGazeLabel(false);
                    SetWarning(false);
                    ResetNeedle();
                    digits.fontSize = idleFontSize;
                    digits.text = VrUiTexts.LoadingHeading;
                    caption.text = string.Empty;
                    break;
                default:
                    frozen = false;
                    ShowGazeLabel(false);
                    SetWarning(false);
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

            long limitMs = LimitMs;
            if (limitMs > 0)
            {
                SetRemaining(Math.Max(0L, limitMs - elapsedMs));
                return;
            }

            long tenths = elapsedMs / 100;
            if (tenths != shownTenths)
            {
                shownTenths = tenths;
                long totalSeconds = tenths / 10;
                digits.SetText("{0:00}:{1:00}.{2}", totalSeconds / 60, totalSeconds % 60, tenths % 10);
            }

            SetNeedle(elapsedMs);
        }

        /// <summary>Countdown digits: <c>mm:ss</c>, with tenths in the warning window (rounded up so 0 means time is up).</summary>
        private void SetRemaining(long remainingMs)
        {
            long tenths = (remainingMs + 99) / 100;
            bool inWarning = remainingMs <= warningSeconds * 1000L;
            long shown = inWarning ? tenths : (tenths + 9) / 10 * 10;
            if (shown != shownTenths)
            {
                shownTenths = shown;
                WriteRemaining(digits, shown, inWarning);
                if (gazeText != null)
                {
                    WriteRemaining(gazeText, shown, inWarning);
                }
            }

            SetNeedle(remainingMs);
        }

        private static void WriteRemaining(TMP_Text text, long tenths, bool withTenths)
        {
            long totalSeconds = tenths / 10;
            if (withTenths)
            {
                text.SetText("{0:00}:{1:00}.{2}", totalSeconds / 60, totalSeconds % 60, tenths % 10);
            }
            else
            {
                text.SetText("{0:00}:{1:00}", totalSeconds / 60, totalSeconds % 60);
            }
        }

        private void SetNeedle(long ms)
        {
            needleAngle = (float)((ms / 1000d * 6d) % 360d);
            if (needle != null)
            {
                needle.localRotation = Quaternion.AngleAxis(needleAngle, needleAxis);
            }
        }

        /// <summary>Once per elapsed second: the faint tick, and in countdown mode the warning pop / beeps.</summary>
        private void OnSecond(long elapsedMs, long second)
        {
            long limitMs = LimitMs;
            if (limitMs <= 0)
            {
                if (tickSound && audioDirector != null && second > 0)
                {
                    audioDirector.PlayStopwatchTick(transform.position);
                }

                return;
            }

            long remainingSeconds = Math.Max(0L, (limitMs - elapsedMs + 999) / 1000);
            bool inWarning = remainingSeconds > 0 && remainingSeconds <= warningSeconds;
            SetWarning(inWarning);
            if (inWarning)
            {
                Pop(0.12f, 0.35f);
            }

            if (audioDirector == null || second <= 0)
            {
                return;
            }

            if (remainingSeconds > 0 && remainingSeconds <= beepSeconds)
            {
                // Rising pitch towards zero (beep 3 → 1 = +0 → +4 semitones).
                audioDirector.PlayCountdownBeep((int)Math.Min(3L, remainingSeconds));
            }
            else if (tickSound)
            {
                audioDirector.PlayStopwatchTick(transform.position);
            }
        }

        private void SetWarning(bool on)
        {
            if (warning == on || digits == null)
            {
                return;
            }

            warning = on;
            var color = on ? PresentationStyle.Orange : PresentationStyle.Ivory;
            digits.color = color;
            if (gazeText != null)
            {
                gazeText.color = color;
            }
        }

        private void ShowGazeLabel(bool show)
        {
            if (!show)
            {
                if (gazeText != null && gazeText.gameObject.activeSelf)
                {
                    gazeText.gameObject.SetActive(false);
                }

                return;
            }

            if (gazeText == null)
            {
                // Not parented to the stopwatch: it floats in front of the participant (world space, lazy follow).
                var root = new GameObject("Time Left (gaze)").transform;
                gazeText = PresentationStyle.CreateText("Digits", root, gazeLabelFontSize, PresentationStyle.Ivory, TextAlignmentOptions.Center, new Vector2(0.3f, 0.08f));
                gazeText.fontStyle = FontStyles.Bold;
                gazeText.outlineWidth = 0.2f;
                gazeText.outlineColor = new Color32(0x07, 0x2A, 0x63, 0xFF);
                // Always drawn on top so the suitcase or a shelf never hides the remaining time.
                var material = gazeText.fontMaterial;
                material.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Overlay - 1;
            }

            gazeText.color = warning ? PresentationStyle.Orange : PresentationStyle.Ivory;
            gazeText.text = string.Empty;
            shownTenths = long.MinValue; // next SetTime writes both texts
            gazeText.transform.parent.gameObject.SetActive(true);
            gazeText.gameObject.SetActive(true);
        }

        /// <summary>Keeps the label low in the field of view, easing after the head instead of being glued to it.</summary>
        private void UpdateGazeLabel()
        {
            if (gazeText == null || !gazeText.gameObject.activeInHierarchy)
            {
                return;
            }

            if (gazeCamera == null || !gazeCamera.isActiveAndEnabled)
            {
                gazeCamera = Camera.main;
                if (gazeCamera == null)
                {
                    return;
                }
            }

            var head = gazeCamera.transform;
            var forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f)
            {
                forward = head.up;
                forward.y = 0f;
            }

            forward.Normalize();
            float drop = session != null && session.State == SessionState.Countdown ? gazeCountdownDrop : gazeLabelDrop;
            var target = head.position + forward * gazeLabelDistance + Vector3.down * drop;
            var root = gazeText.transform.parent;
            if (!gazePlaced)
            {
                root.position = target;
                gazePlaced = true;
            }
            else
            {
                root.position = Vector3.Lerp(root.position, target, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
            }

            var look = root.position - head.position;
            look.y = 0f;
            if (look.sqrMagnitude > 1e-4f)
            {
                root.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
            }
        }

        /// <summary>Plays a spoken countdown clip (Resources/Voice/countdown_&lt;key&gt;), head-relative (2D).</summary>
        private void PlayVoice(string key)
        {
            if (!voiceCountdown || !Application.isPlaying)
            {
                return;
            }

            var clip = Resources.Load<AudioClip>(VoiceResourcePrefix + key);
            if (clip == null)
            {
                return;
            }

            if (voice == null)
            {
                voice = gameObject.AddComponent<AudioSource>();
                voice.playOnAwake = false;
                voice.spatialBlend = 0f;
                voice.priority = 0;
            }

            voice.Stop();
            voice.clip = clip;
            voice.volume = voiceVolume;
            voice.Play();
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
