using System.Collections.Generic;
using System.Globalization;
using MultiTravel.Core.Scoring;
using MultiTravel.Gameplay.Common;
using MultiTravel.Gameplay.Items;
using MultiTravel.Gameplay.Suitcase;
using MultiTravel.Gameplay.UI;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

namespace MultiTravel.Gameplay.Feedback
{
    /// <summary>
    /// Audio, haptic and visual feedback for suitcase placements (ARCHITECTURE.md §2.8):
    /// procedural clips generated once in <c>Awake</c> (positive chime, negative buzz, tick), a haptic impulse on the
    /// interactor that released the item, a slot pulse and pooled floating "+10" / "-5" labels that rise and fade.
    /// Nothing is allocated per frame; label strings are cached per score value.
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
        [Range(0f, 1f)]
        private float positiveAmplitude = 0.45f;

        [SerializeField]
        [Min(0f)]
        private float positiveDuration = 0.08f;

        [SerializeField]
        [Range(0f, 1f)]
        private float negativeAmplitude = 0.85f;

        [SerializeField]
        [Min(0f)]
        private float negativeDuration = 0.22f;

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

        /// <summary>Generated positive clip.</summary>
        public AudioClip ChimeClip => chimeClip;

        /// <summary>Generated negative clip.</summary>
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
        /// Uses <see cref="HapticImpulsePlayer"/> and falls back to an <see cref="IXRHapticImpulseProvider"/> in the parents.
        /// </summary>
        public static bool SendHaptic(Transform interactor, float amplitude, float duration)
        {
            if (interactor == null || amplitude <= 0f || duration <= 0f)
            {
                return false;
            }

            var player = interactor.GetComponentInParent<HapticImpulsePlayer>(true);
            if (player != null && player.SendHapticImpulse(amplitude, duration))
            {
                return true;
            }

            var provider = interactor.GetComponentInParent<IXRHapticImpulseProvider>(true);
            if (provider == null)
            {
                return false;
            }

            var group = provider.GetChannelGroup();
            if (group == null || group.channelCount == 0)
            {
                return false;
            }

            var channel = group.GetChannel();
            return channel != null && channel.SendHapticImpulse(amplitude, duration, 0f);
        }

        // ----- Unity -----

        private void Awake()
        {
            chimeClip = FeedbackAudioSynth.CreateClip("MT_PositiveChime", FeedbackAudioSynth.PositiveChime());
            buzzClip = FeedbackAudioSynth.CreateClip("MT_NegativeBuzz", FeedbackAudioSynth.NegativeBuzz());
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
                    SendHaptic(item.LastInteractor, positiveAmplitude, positiveDuration);
                }
                else
                {
                    SendHaptic(item.LastInteractor, negativeAmplitude, negativeDuration);
                }
            }

            if (suitcase != null && suitcase.TryGetSlot(item, out var slot))
            {
                slot.Pulse(positive ? VrUiStyle.Teal : VrUiStyle.Orange);
            }

            ShowScoreLabel(change.Delta, position + Vector3.up * labelHeightOffset);
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
