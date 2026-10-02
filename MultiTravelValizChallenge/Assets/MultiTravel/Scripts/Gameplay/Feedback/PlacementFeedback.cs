using System.Collections.Generic;
using System.Globalization;
using MultiTravel.Core.Scoring;
using MultiTravel.Gameplay.Common;
using MultiTravel.Gameplay.Items;
using MultiTravel.Gameplay.Suitcase;
using MultiTravel.Gameplay.UI;
using TMPro;
using UnityEngine;

namespace MultiTravel.Gameplay.Feedback
{
    /// <summary>
    /// Audio, haptic and visual feedback for suitcase placements (ARCHITECTURE.md §2.8, OVERHAUL_PLAN §5):
    /// procedural clips generated once in <c>Awake</c> (positive chime, soft wrong-item thud with a falling tone, tick),
    /// haptics from the <see cref="Haptics"/> table on the interactor that released the item (correct: double pulse,
    /// wrong: one longer pulse), a slot pulse and pooled floating "+10" / "-5" labels that rise and fade. The practice item
    /// gets the positive chime and haptics but no score label. Per-material landing foley is left to the audio director
    /// (<see cref="SuitcaseController.ItemLanded"/>). Nothing is allocated per frame; label strings are cached per score value.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlacementFeedback : MonoBehaviour
    {
        private struct LabelState
        {
            public TextMeshPro Text;
            public Vector3 Origin;
            public float StartTime;
            public bool Active;
        }

        [SerializeField]
        [Tooltip("Suitcase whose ItemPlaced / ItemRemoved events drive the feedback.")]
        private SuitcaseController suitcase;

        [Header("Audio")]
        [SerializeField]
        [Range(0f, 1f)]
        private float volume = 0.8f;

        [SerializeField]
        [Tooltip("0 = 2D, 1 = fully positional.")]
        [Range(0f, 1f)]
        private float spatialBlend = 0.7f;

        [SerializeField]
        [Min(1)]
        private int audioVoices = 4;

        [Header("Haptics")]
        [SerializeField]
        [Tooltip("Haptics are only sent when the placement happened within this many seconds of the release.")]
        [Min(0f)]
        private float hapticReleaseWindow = 0.6f;

        [Header("Floating labels")]
        [SerializeField]
        [Min(1)]
        private int labelPoolSize = 6;

        [SerializeField]
        [Min(0.1f)]
        private float labelLifetime = 1.1f;

        [SerializeField]
        [Tooltip("Distance the label rises over its lifetime (metres).")]
        private float labelRise = 0.25f;

        [SerializeField]
        [Tooltip("Offset above the item anchor where labels appear (metres).")]
        private float labelHeightOffset = 0.15f;

        private readonly Dictionary<int, string> labelStrings = new Dictionary<int, string>();
        private AudioSource[] voices = new AudioSource[0];
        private int nextVoice;
        private AudioClip chimeClip;
        private AudioClip buzzClip;
        private AudioClip tickClip;
        private LabelState[] labels = new LabelState[0];
        private int nextLabel;
        private int activeLabelCount;
        private Camera viewCamera;
        private float nextCameraLookup;
        private SuitcaseController subscribedSuitcase;
        private Transform pendingPulseTarget;
        private float pendingPulseTime = -1f;

        /// <summary>Generated positive clip.</summary>
        public AudioClip ChimeClip => chimeClip;

        /// <summary>Generated wrong-item clip (low dull thud + falling tone).</summary>
        public AudioClip BuzzClip => buzzClip;

        /// <summary>Generated tick clip.</summary>
        public AudioClip TickClip => tickClip;

        /// <summary>Number of labels currently animating.</summary>
        public int ActiveLabelCount => activeLabelCount;

        /// <summary>Generator / test API: binds to a suitcase (re-subscribes).</summary>
        public void SetSuitcase(SuitcaseController target)
        {
            Unsubscribe();
            suitcase = target;
            Subscribe();
        }

        /// <summary>Plays the tick (countdown seconds, removals).</summary>
        public void PlayTick(Vector3 position)
        {
            Play(tickClip, position, 1f);
        }

        /// <summary>Plays the tick at the listener position (2D-ish).</summary>
        public void PlayTick()
        {
            var cam = ResolveCamera();
            PlayTick(cam != null ? cam.transform.position : transform.position);
        }

        /// <summary>Plays the positive chime at the listener position (countdown "Başla!").</summary>
        public void PlayStart()
        {
            var cam = ResolveCamera();
            Play(chimeClip, cam != null ? cam.transform.position : transform.position, 1f);
        }

        /// <summary>Shows a floating label for a score delta at a world position.</summary>
        public void ShowScoreLabel(int delta, Vector3 worldPosition)
        {
            if (delta == 0 || labels.Length == 0)
            {
                return;
            }

            ref var label = ref labels[nextLabel];
            nextLabel = (nextLabel + 1) % labels.Length;
            if (!label.Active)
            {
                activeLabelCount++;
            }

            label.Active = true;
            label.Origin = worldPosition;
            label.StartTime = Time.unscaledTime;
            label.Text.text = LabelFor(delta);
            label.Text.color = delta > 0 ? VrUiStyle.Teal : VrUiStyle.Orange;
            label.Text.alpha = 1f;
            label.Text.transform.position = worldPosition;
            FaceCamera(label.Text.transform);
            label.Text.gameObject.SetActive(true);
        }

        /// <summary>Hides every label and stops all sounds (session reset).</summary>
        public void ResetFeedback()
        {
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i].Active = false;
                if (labels[i].Text != null)
                {
                    labels[i].Text.gameObject.SetActive(false);
                }
            }

            activeLabelCount = 0;
            pendingPulseTarget = null;
            pendingPulseTime = -1f;
            for (int i = 0; i < voices.Length; i++)
            {
                if (voices[i] != null)
                {
                    voices[i].Stop();
                }
            }
        }

        /// <summary>
        /// Sends a haptic impulse to the controller owning <paramref name="interactor"/> (hands have no haptics; no-op).
        /// Same as <see cref="Haptics.Send(Transform, float, float)"/>.
        /// </summary>
        public static bool SendHaptic(Transform interactor, float amplitude, float duration)
        {
            return Haptics.Send(interactor, amplitude, duration);
        }

        /// <summary>Plays the correct-placement double pulse (0.5 / 30 ms, then 0.3 / 60 ms) on <paramref name="interactor"/>.</summary>
        public void PlayCorrectHaptics(Transform interactor)
        {
            if (interactor == null)
            {
                return;
            }

            Haptics.Send(interactor, Haptics.CorrectFirst);
            pendingPulseTarget = interactor;
            pendingPulseTime = Time.unscaledTime + Haptics.CorrectSecondDelay;
        }

        /// <summary>Plays the wrong-placement pulse (0.6 / 90 ms) on <paramref name="interactor"/>.</summary>
        public void PlayWrongHaptics(Transform interactor)
        {
            Haptics.Send(interactor, Haptics.Wrong);
        }

        // ----- Unity -----

        private void Awake()
        {
            chimeClip = FeedbackAudioSynth.CreateClip("MT_PositiveChime", FeedbackAudioSynth.PositiveChime());
            buzzClip = FeedbackAudioSynth.CreateClip("MT_WrongThud", FeedbackAudioSynth.WrongThud());
            tickClip = FeedbackAudioSynth.CreateClip("MT_Tick", FeedbackAudioSynth.Tick());

            voices = new AudioSource[Mathf.Max(1, audioVoices)];
            for (int i = 0; i < voices.Length; i++)
            {
                var go = new GameObject("FeedbackVoice_" + i);
                go.transform.SetParent(transform, false);
                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = spatialBlend;
                source.minDistance = 0.6f;
                source.maxDistance = 12f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.dopplerLevel = 0f;
                voices[i] = source;
            }

            labels = new LabelState[Mathf.Max(1, labelPoolSize)];
            var font = VrUiStyle.Font;
            for (int i = 0; i < labels.Length; i++)
            {
                var go = new GameObject("ScoreLabel_" + i);
                go.transform.SetParent(transform, false);
                var text = go.AddComponent<TextMeshPro>();
                if (font != null)
                {
                    text.font = font;
                }

                text.fontSize = VrUiStyle.FloatingLabelSize;
                text.fontStyle = FontStyles.Bold;
                text.alignment = TextAlignmentOptions.Center;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.rectTransform.sizeDelta = new Vector2(1f, 0.3f);
                text.outlineWidth = 0.15f;
                text.outlineColor = new Color32(0x07, 0x2A, 0x63, 0xFF);
                go.SetActive(false);
                labels[i] = new LabelState { Text = text };
            }
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void Start()
        {
            if (suitcase == null)
            {
                Debug.LogError(ServiceResolver.LogPrefix + "PlacementFeedback: no SuitcaseController assigned; placement feedback is disabled.", this);
            }

            Subscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            DestroyClip(ref chimeClip);
            DestroyClip(ref buzzClip);
            DestroyClip(ref tickClip);
        }

        private void Update()
        {
            if (pendingPulseTime >= 0f && Time.unscaledTime >= pendingPulseTime)
            {
                Haptics.Send(pendingPulseTarget, Haptics.CorrectSecond);
                pendingPulseTarget = null;
                pendingPulseTime = -1f;
            }

            if (activeLabelCount == 0)
            {
                return;
            }

            float now = Time.unscaledTime;
            for (int i = 0; i < labels.Length; i++)
            {
                ref var label = ref labels[i];
                if (!label.Active)
                {
                    continue;
                }

                float k = (now - label.StartTime) / labelLifetime;
                if (k >= 1f)
                {
                    label.Active = false;
                    label.Text.gameObject.SetActive(false);
                    activeLabelCount--;
                    continue;
                }

                float eased = 1f - (1f - k) * (1f - k);
                var labelTransform = label.Text.transform;
                labelTransform.position = label.Origin + Vector3.up * (labelRise * eased);
                FaceCamera(labelTransform);
                label.Text.alpha = k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f;
            }
        }

        // ----- internals -----

        private void Subscribe()
        {
            if (suitcase == null || subscribedSuitcase == suitcase)
            {
                return;
            }

            Unsubscribe();
            suitcase.ItemPlaced += OnItemPlaced;
            suitcase.ItemRemoved += OnItemRemoved;
            suitcase.PracticeItemPlaced += OnPracticePlaced;
            suitcase.PracticeItemRemoved += OnPracticeRemoved;
            subscribedSuitcase = suitcase;
        }

        private void Unsubscribe()
        {
            if (subscribedSuitcase == null)
            {
                return;
            }

            subscribedSuitcase.ItemPlaced -= OnItemPlaced;
            subscribedSuitcase.ItemRemoved -= OnItemRemoved;
            subscribedSuitcase.PracticeItemPlaced -= OnPracticePlaced;
            subscribedSuitcase.PracticeItemRemoved -= OnPracticeRemoved;
            subscribedSuitcase = null;
        }

        private void OnItemPlaced(ProductItem item, ScoreChange change)
        {
            var position = item.AnchorPosition;
            bool positive = change.Delta > 0 || (change.Delta == 0 && item.Definition != null && item.Definition.IsCorrect);
            if (change.Delta > 0)
            {
                Play(chimeClip, position, 1f);
            }
            else if (change.Delta < 0)
            {
                Play(buzzClip, position, 1f);
            }
            else
            {
                Play(tickClip, position, 1f);
            }

            if (item.LastInteractor != null && Time.unscaledTime - item.LastReleaseTime <= hapticReleaseWindow)
            {
                if (positive)
                {
                    PlayCorrectHaptics(item.LastInteractor);
                }
                else
                {
                    PlayWrongHaptics(item.LastInteractor);
                }
            }

            if (suitcase != null && suitcase.TryGetSlot(item, out var slot))
            {
                slot.Pulse(positive ? VrUiStyle.Teal : VrUiStyle.Orange);
            }

            ShowScoreLabel(change.Delta, position + Vector3.up * labelHeightOffset);
        }

        private void OnPracticePlaced(ProductItem item)
        {
            Play(chimeClip, item.AnchorPosition, 0.8f);
            if (item.LastInteractor != null && Time.unscaledTime - item.LastReleaseTime <= hapticReleaseWindow)
            {
                PlayCorrectHaptics(item.LastInteractor);
            }

            if (suitcase != null && suitcase.TryGetSlot(item, out var slot))
            {
                slot.Pulse(VrUiStyle.Teal);
            }
        }

        private void OnPracticeRemoved(ProductItem item)
        {
            Play(tickClip, item.AnchorPosition, 0.6f);
        }

        private void OnItemRemoved(ProductItem item, ScoreChange change)
        {
            var position = item.AnchorPosition;
            Play(tickClip, position, 0.8f);
            ShowScoreLabel(change.Delta, position + Vector3.up * labelHeightOffset);
        }

        private void Play(AudioClip clip, Vector3 position, float volumeScale)
        {
            if (clip == null || voices.Length == 0)
            {
                return;
            }

            var source = voices[nextVoice];
            nextVoice = (nextVoice + 1) % voices.Length;
            if (source == null)
            {
                return;
            }

            source.transform.position = position;
            source.Stop();
            source.clip = clip;
            source.volume = volume * volumeScale;
            source.Play();
        }

        private string LabelFor(int delta)
        {
            if (!labelStrings.TryGetValue(delta, out var text))
            {
                text = delta > 0
                    ? "+" + delta.ToString(CultureInfo.InvariantCulture)
                    : delta.ToString(CultureInfo.InvariantCulture);
                labelStrings.Add(delta, text);
            }

            return text;
        }

        private void FaceCamera(Transform target)
        {
            var cam = ResolveCamera();
            if (cam == null)
            {
                return;
            }

            var direction = target.position - cam.transform.position;
            if (direction.sqrMagnitude > 1e-6f)
            {
                target.rotation = Quaternion.LookRotation(direction, Vector3.up);
            }
        }

        private Camera ResolveCamera()
        {
            if (viewCamera != null && viewCamera.isActiveAndEnabled)
            {
                return viewCamera;
            }

            if (Time.unscaledTime >= nextCameraLookup)
            {
                nextCameraLookup = Time.unscaledTime + 1f;
                viewCamera = Camera.main;
            }

            return viewCamera;
        }

        private static void DestroyClip(ref AudioClip clip)
        {
            if (clip != null)
            {
                Destroy(clip);
                clip = null;
            }
        }
    }
}
