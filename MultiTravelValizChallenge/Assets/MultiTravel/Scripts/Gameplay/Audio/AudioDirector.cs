using System.Collections.Generic;
using MultiTravel.Core.Scoring;
using MultiTravel.Core.Session;
using MultiTravel.Gameplay.Common;
using MultiTravel.Gameplay.Director;
using MultiTravel.Gameplay.Items;
using MultiTravel.Gameplay.Suitcase;
using UnityEngine;

namespace MultiTravel.Gameplay.Audio
{
    /// <summary>Mixing category of a sound; each has its own volume on <see cref="AudioDirector"/>.</summary>
    public enum AudioCategory
    {
        Ambient = 0,
        Sfx = 1,
        Ui = 2
    }

    /// <summary>
    /// The experience's sound engine (OVERHAUL_PLAN §5). AudioMixers cannot be authored from code, so mixing is done
    /// in-component: <see cref="poolSize"/> pooled <see cref="AudioSource"/>s shared by every caller, three categories
    /// (Ambient / SFX / UI) under one master volume (serialized default 0.8, never read from Core config), 3D foley per
    /// <see cref="SoundKind"/> with a random pitch of 0.95–1.05, a looping room tone, latch / zip, countdown beeps
    /// (+0 / +2 / +4 semitones), a success motif and a faint stopwatch tick.
    /// <para>
    /// Real recordings can replace every sound through the serialized <c>AudioClip[]</c> arrays; a procedural clip from
    /// <see cref="ProceduralAudio"/> is generated only for arrays that are empty. Nothing allocates after <c>Awake</c>.
    /// </para>
    /// <para>
    /// Wiring: the director (countdown beeps, start cue), the suitcase (foley on placement, derived from the product
    /// category) and the session (success motif on Completed) are optional. When the mechanics layer raises its richer
    /// <c>SuitcaseController.ItemLanded(item, kind, isCorrect)</c>, wire it to <see cref="ItemLanded"/> and turn
    /// <see cref="deriveFoleyFromPlacement"/> off so landings are not voiced twice.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioDirector : MonoBehaviour
    {
        [Header("Mix")]
        [SerializeField]
        [Range(0f, 1f)]
        private float masterVolume = 0.8f;

        [SerializeField]
        [Range(0f, 1f)]
        private float ambientVolume = 0.6f;

        [SerializeField]
        [Range(0f, 1f)]
        private float sfxVolume = 1f;

        [SerializeField]
        [Range(0f, 1f)]
        private float uiVolume = 0.9f;

        [SerializeField]
        [Tooltip("Pooled one-shot voices (plus one dedicated ambient source).")]
        [Range(4, 32)]
        private int poolSize = 16;

        [SerializeField]
        [Tooltip("Random pitch range applied to foley (min, max).")]
        private Vector2 foleyPitchRange = new Vector2(0.95f, 1.05f);

        [Header("Scene hooks (optional)")]
        [SerializeField]
        private GameplayDirector director;

        [SerializeField]
        private SuitcaseController suitcase;

        [SerializeField]
        [Tooltip("Suitcase lid: zip on Closing, latch click on Closed / Opened.")]
        private SuitcaseLid lid;

        [SerializeField]
        [Tooltip("Play a category-derived foley on SuitcaseController.ItemPlaced. Turn off when ItemLanded is wired to this component.")]
        private bool deriveFoleyFromPlacement = true;

        [SerializeField]
        [Tooltip("Play the 3-2-1 beeps and the start cue from GameplayDirector.CountdownTick (turn off when PlacementFeedback already does).")]
        private bool playCountdownBeeps = true;

        [SerializeField]
        [Tooltip("Start the looping room tone at Start.")]
        private bool playRoomTone = true;

        [Header("Clips (empty = procedural fallback)")]
        [SerializeField] private AudioClip[] clothClips = new AudioClip[0];
        [SerializeField] private AudioClip[] leatherClips = new AudioClip[0];
        [SerializeField] private AudioClip[] hardClips = new AudioClip[0];
        [SerializeField] private AudioClip[] metalClips = new AudioClip[0];
        [SerializeField] private AudioClip[] paperClips = new AudioClip[0];
        [SerializeField] private AudioClip[] roomToneClips = new AudioClip[0];
        [SerializeField] private AudioClip[] latchClips = new AudioClip[0];
        [SerializeField] private AudioClip[] zipClips = new AudioClip[0];
        [SerializeField] [Tooltip("Index 0 = 3, 1 = 2, 2 = 1 (remaining seconds).")] private AudioClip[] countdownBeepClips = new AudioClip[0];
        [SerializeField] private AudioClip[] startClips = new AudioClip[0];
        [SerializeField] private AudioClip[] successClips = new AudioClip[0];
        [SerializeField] private AudioClip[] stopwatchTickClips = new AudioClip[0];
        [SerializeField] private AudioClip[] confirmClips = new AudioClip[0];

        private readonly AudioClip[][] foleyByKind = new AudioClip[SoundKinds.Count][];
        private readonly List<AudioClip> generated = new List<AudioClip>();
        private AudioSource[] voices = new AudioSource[0];
        private AudioSource ambientSource;
        private int nextVoice;
        private AudioClip[] beeps = new AudioClip[0];
        private AudioClip[] startSet;
        private AudioClip[] successSet;
        private AudioClip[] latchSet;
        private AudioClip[] zipSet;
        private AudioClip[] tickSet;
        private AudioClip[] confirmSet;
        private AudioClip[] roomSet;
        private SessionController session;
        private bool sessionSubscribed;
        private GameplayDirector subscribedDirector;
        private SuitcaseController subscribedSuitcase;
        private SuitcaseLid subscribedLid;
        private Camera listenerCamera;
        private float nextCameraLookup;
        private bool built;

        /// <summary>Master volume (0–1).</summary>
        public float MasterVolume => masterVolume;

        /// <summary>Number of pooled voices currently playing (diagnostics / tests).</summary>
        public int PlayingVoices
        {
            get
            {
                int count = 0;
                for (int i = 0; i < voices.Length; i++)
                {
                    if (voices[i] != null && voices[i].isPlaying)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>True while the room tone loops.</summary>
        public bool RoomTonePlaying => ambientSource != null && ambientSource.isPlaying;

        // ----- setup -----

        /// <summary>Generator API: scene hooks (any may be null).</summary>
        public void Configure(GameplayDirector gameplayDirector, SuitcaseController suitcaseController, SuitcaseLid suitcaseLid)
        {
            if (subscribedLid != null && subscribedLid != suitcaseLid)
            {
                UnsubscribeLid();
            }

            lid = suitcaseLid;
            if (subscribedDirector != null && subscribedDirector != gameplayDirector)
            {
                subscribedDirector.CountdownTick -= OnCountdownTick;
                subscribedDirector = null;
            }

            if (subscribedSuitcase != null && subscribedSuitcase != suitcaseController)
            {
                subscribedSuitcase.ItemPlaced -= OnItemPlaced;
                subscribedSuitcase.ItemRemoved -= OnItemRemoved;
                subscribedSuitcase = null;
            }

            director = gameplayDirector;
            suitcase = suitcaseController;
            if (isActiveAndEnabled)
            {
                SubscribeScene();
            }
        }

        /// <summary>Test / generator API: explicit session (null = resolve from AppServices in Start).</summary>
        public void Bind(SessionController sessionController)
        {
            if (sessionController == null || sessionController == session)
            {
                return;
            }

            UnsubscribeSession();
            session = sessionController;
            SubscribeSession();
        }

        /// <summary>Sets the master volume (0–1); applied to the ambient loop immediately and to every later one-shot.</summary>
        public void SetMasterVolume(float value)
        {
            masterVolume = Mathf.Clamp01(value);
            ApplyAmbientVolume();
        }

        /// <summary>Generator API: foley recordings for a kind (null / empty keeps the procedural clip).</summary>
        public void SetFoleyClips(SoundKind kind, AudioClip[] clips)
        {
            switch (kind)
            {
                case SoundKind.Cloth: clothClips = clips ?? new AudioClip[0]; break;
                case SoundKind.Leather: leatherClips = clips ?? new AudioClip[0]; break;
                case SoundKind.Hard: hardClips = clips ?? new AudioClip[0]; break;
                case SoundKind.Metal: metalClips = clips ?? new AudioClip[0]; break;
                case SoundKind.Paper: paperClips = clips ?? new AudioClip[0]; break;
            }

            if (built)
            {
                foleyByKind[(int)kind] = Resolve(clips, null);
            }
        }

        // ----- playback API -----

        /// <summary>Plays a clip from the pool at a world position with category volume and pitch (0 spatial blend for UI).</summary>
        public AudioSource Play(AudioClip clip, Vector3 position, AudioCategory category, float volumeScale = 1f, float pitch = 1f)
        {
            if (clip == null || voices.Length == 0)
            {
                return null;
            }

            var source = TakeVoice();
            if (source == null)
            {
                return null;
            }

            source.transform.position = position;
            source.spatialBlend = category == AudioCategory.Ui ? 0f : 1f;
            source.clip = clip;
            source.pitch = pitch;
            source.volume = Mathf.Clamp01(masterVolume * CategoryVolume(category) * volumeScale);
            source.Play();
            return source;
        }

        /// <summary>3D foley for an item landing; wrong items are slightly duller (lower pitch, less volume).</summary>
        public void PlayFoley(SoundKind kind, Vector3 position, bool isCorrect)
        {
            EnsureBuilt();
            var set = foleyByKind[(int)kind];
            var clip = Pick(set);
            if (clip == null)
            {
                return;
            }

            float pitch = Random.Range(foleyPitchRange.x, foleyPitchRange.y) * (isCorrect ? 1f : 0.94f);
            Play(clip, position, AudioCategory.Sfx, isCorrect ? 1f : 0.85f, pitch);
        }

        /// <summary>Signature-compatible target for <c>SuitcaseController.ItemLanded</c>.</summary>
        public void ItemLanded(ProductItem item, SoundKind kind, bool isCorrect)
        {
            if (item == null)
            {
                return;
            }

            PlayFoley(kind, item.AnchorPosition, isCorrect);
        }

        /// <summary>Countdown beep for the remaining seconds (3 → +0, 2 → +2, 1 → +4 semitones); 0 plays the start cue.</summary>
        public void PlayCountdownBeep(int remaining)
        {
            EnsureBuilt();
            if (remaining <= 0)
            {
                PlayStart();
                return;
            }

            int index = Mathf.Clamp(3 - remaining, 0, beeps.Length - 1);
            if (beeps.Length > 0)
            {
                Play(beeps[index], ListenerPosition(), AudioCategory.Ui, 1f, 1f);
            }
        }

        /// <summary>"BAŞLA!" cue.</summary>
        public void PlayStart()
        {
            EnsureBuilt();
            Play(Pick(startSet), ListenerPosition(), AudioCategory.Ui, 1f, 1f);
        }

        /// <summary>Success motif (game completed).</summary>
        public void PlaySuccess()
        {
            EnsureBuilt();
            Play(Pick(successSet), ListenerPosition(), AudioCategory.Ui, 1f, 1f);
        }

        /// <summary>Soft confirmation (tutorial step done).</summary>
        public void PlayConfirm()
        {
            EnsureBuilt();
            Play(Pick(confirmSet), ListenerPosition(), AudioCategory.Ui, 0.8f, 1f);
        }

        /// <summary>Suitcase latch click at the lid.</summary>
        public void PlayLatch(Vector3 position)
        {
            EnsureBuilt();
            Play(Pick(latchSet), position, AudioCategory.Sfx, 1f, Random.Range(0.97f, 1.03f));
        }

        /// <summary>Zip sweep while the lid closes.</summary>
        public void PlayZip(Vector3 position)
        {
            EnsureBuilt();
            Play(Pick(zipSet), position, AudioCategory.Sfx, 0.9f, Random.Range(0.96f, 1.04f));
        }

        /// <summary>Faint 1 Hz stopwatch tick (played by <c>StopwatchDisplay</c> at the stopwatch).</summary>
        public void PlayStopwatchTick(Vector3 position)
        {
            EnsureBuilt();
            Play(Pick(tickSet), position, AudioCategory.Sfx, 0.25f, 1f);
        }

        /// <summary>Stops every one-shot voice (room tone keeps playing).</summary>
        public void StopAll()
        {
            for (int i = 0; i < voices.Length; i++)
            {
                if (voices[i] != null)
                {
                    voices[i].Stop();
                }
            }
        }

        // ----- Unity -----

        private void Awake()
        {
            EnsureBuilt();
        }

        private void OnEnable()
        {
            SubscribeScene();
            SubscribeSession();
        }

        private void Start()
        {
            if (session == null)
            {
                ServiceResolver.TryResolve(ref session);
                SubscribeSession();
            }

            if (playRoomTone && ambientSource != null && !ambientSource.isPlaying)
            {
                var clip = Pick(roomSet);
                if (clip != null)
                {
                    ambientSource.clip = clip;
                    ApplyAmbientVolume();
                    ambientSource.Play();
                }
            }
        }

        private void OnDisable()
        {
            UnsubscribeScene();
            UnsubscribeSession();
        }

        private void OnDestroy()
        {
            UnsubscribeScene();
            UnsubscribeSession();
            for (int i = 0; i < generated.Count; i++)
            {
                if (generated[i] != null)
                {
                    Destroy(generated[i]);
                }
            }

            generated.Clear();
        }

        // ----- internals -----

        private void EnsureBuilt()
        {
            if (built)
            {
                return;
            }

            built = true;
            foleyByKind[(int)SoundKind.Cloth] = Resolve(clothClips, ProceduralAudio.Cloth);
            foleyByKind[(int)SoundKind.Leather] = Resolve(leatherClips, ProceduralAudio.Leather);
            foleyByKind[(int)SoundKind.Hard] = Resolve(hardClips, ProceduralAudio.Hard);
            foleyByKind[(int)SoundKind.Metal] = Resolve(metalClips, ProceduralAudio.Metal);
            foleyByKind[(int)SoundKind.Paper] = Resolve(paperClips, ProceduralAudio.Paper);
            roomSet = Resolve(roomToneClips, ProceduralAudio.RoomTone);
            latchSet = Resolve(latchClips, ProceduralAudio.Latch);
            zipSet = Resolve(zipClips, ProceduralAudio.Zip);
            startSet = Resolve(startClips, ProceduralAudio.Start);
            successSet = Resolve(successClips, ProceduralAudio.Success);
            tickSet = Resolve(stopwatchTickClips, ProceduralAudio.StopwatchTick);
            confirmSet = Resolve(confirmClips, ProceduralAudio.Confirm);
            if (countdownBeepClips != null && countdownBeepClips.Length >= 3)
            {
                beeps = countdownBeepClips;
            }
            else
            {
                beeps = new[] { Track(ProceduralAudio.Beep(0)), Track(ProceduralAudio.Beep(2)), Track(ProceduralAudio.Beep(4)) };
            }

            voices = new AudioSource[Mathf.Clamp(poolSize, 4, 32)];
            for (int i = 0; i < voices.Length; i++)
            {
                voices[i] = CreateSource("Voice_" + i, false);
            }

            ambientSource = CreateSource("RoomTone", true);
            ambientSource.spatialBlend = 0f;
        }

        private AudioSource CreateSource(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 1f;
            source.minDistance = 0.5f;
            source.maxDistance = 10f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.dopplerLevel = 0f;
            source.spread = 20f;
            return source;
        }

        private AudioClip[] Resolve(AudioClip[] serialized, System.Func<AudioClip> fallback)
        {
            if (serialized != null)
            {
                for (int i = 0; i < serialized.Length; i++)
                {
                    if (serialized[i] != null)
                    {
                        return serialized;
                    }
                }
            }

            return fallback != null ? new[] { Track(fallback()) } : new AudioClip[0];
        }

        private AudioClip Track(AudioClip clip)
        {
            generated.Add(clip);
            return clip;
        }

        private static AudioClip Pick(AudioClip[] set)
        {
            if (set == null || set.Length == 0)
            {
                return null;
            }

            if (set.Length == 1)
            {
                return set[0];
            }

            // Skip null entries deterministically; sets are short.
            int start = Random.Range(0, set.Length);
            for (int i = 0; i < set.Length; i++)
            {
                var clip = set[(start + i) % set.Length];
                if (clip != null)
                {
                    return clip;
                }
            }

            return null;
        }

        private AudioSource TakeVoice()
        {
            for (int i = 0; i < voices.Length; i++)
            {
                var candidate = voices[(nextVoice + i) % voices.Length];
                if (candidate != null && !candidate.isPlaying)
                {
                    nextVoice = (nextVoice + i + 1) % voices.Length;
                    return candidate;
                }
            }

            // All busy: steal the oldest in round-robin order.
            var stolen = voices[nextVoice];
            nextVoice = (nextVoice + 1) % voices.Length;
            if (stolen != null)
            {
                stolen.Stop();
            }

            return stolen;
        }

        private float CategoryVolume(AudioCategory category)
        {
            switch (category)
            {
                case AudioCategory.Ambient: return ambientVolume;
                case AudioCategory.Ui: return uiVolume;
                default: return sfxVolume;
            }
        }

        private void ApplyAmbientVolume()
        {
            if (ambientSource != null)
            {
                ambientSource.volume = Mathf.Clamp01(masterVolume * ambientVolume);
            }
        }

        private Vector3 ListenerPosition()
        {
            if (listenerCamera == null || !listenerCamera.isActiveAndEnabled)
            {
                if (Time.unscaledTime >= nextCameraLookup)
                {
                    nextCameraLookup = Time.unscaledTime + 1f;
                    listenerCamera = Camera.main;
                }
            }

            return listenerCamera != null ? listenerCamera.transform.position : transform.position;
        }

        private void SubscribeScene()
        {
            if (director != null && subscribedDirector != director)
            {
                if (subscribedDirector != null)
                {
                    subscribedDirector.CountdownTick -= OnCountdownTick;
                }

                director.CountdownTick += OnCountdownTick;
                subscribedDirector = director;
            }

            if (suitcase != null && subscribedSuitcase != suitcase)
            {
                if (subscribedSuitcase != null)
                {
                    subscribedSuitcase.ItemPlaced -= OnItemPlaced;
                    subscribedSuitcase.ItemRemoved -= OnItemRemoved;
                }

                suitcase.ItemPlaced += OnItemPlaced;
                suitcase.ItemRemoved += OnItemRemoved;
                subscribedSuitcase = suitcase;
            }

            if (lid != null && subscribedLid != lid)
            {
                UnsubscribeLid();
                lid.Closing += OnLidClosing;
                lid.Closed += OnLidClosed;
                lid.Opened += OnLidOpened;
                subscribedLid = lid;
            }
        }

        private void UnsubscribeLid()
        {
            if (subscribedLid != null)
            {
                subscribedLid.Closing -= OnLidClosing;
                subscribedLid.Closed -= OnLidClosed;
                subscribedLid.Opened -= OnLidOpened;
                subscribedLid = null;
            }
        }

        private void OnLidClosing()
        {
            PlayZip(lid != null ? lid.transform.position : ListenerPosition());
        }

        private void OnLidClosed()
        {
            PlayLatch(lid != null ? lid.transform.position : ListenerPosition());
        }

        private void OnLidOpened()
        {
            PlayLatch(lid != null ? lid.transform.position : ListenerPosition());
        }

        private void UnsubscribeScene()
        {
            UnsubscribeLid();
            if (subscribedDirector != null)
            {
                subscribedDirector.CountdownTick -= OnCountdownTick;
                subscribedDirector = null;
            }

            if (subscribedSuitcase != null)
            {
                subscribedSuitcase.ItemPlaced -= OnItemPlaced;
                subscribedSuitcase.ItemRemoved -= OnItemRemoved;
                subscribedSuitcase = null;
            }
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

        private void OnStateChanged(SessionState previous, SessionState next)
        {
            if (next == SessionState.Completed)
            {
                PlaySuccess();
            }
        }

        private void OnCountdownTick(int remaining)
        {
            if (playCountdownBeeps)
            {
                PlayCountdownBeep(remaining);
            }
        }

        private void OnItemPlaced(ProductItem item, ScoreChange change)
        {
            if (!deriveFoleyFromPlacement || item == null)
            {
                return;
            }

            bool correct = item.Definition != null ? item.Definition.IsCorrect : change.Delta >= 0;
            PlayFoley(SoundKinds.For(item.Definition), item.AnchorPosition, correct);
        }

        private void OnItemRemoved(ProductItem item, ScoreChange change)
        {
            if (!deriveFoleyFromPlacement || item == null)
            {
                return;
            }

            // Lifting an item out: the same family, quieter and a touch higher.
            var clip = Pick(foleyByKind[(int)SoundKinds.For(item.Definition)]);
            Play(clip, item.AnchorPosition, AudioCategory.Sfx, 0.45f, Random.Range(1.02f, 1.08f));
        }
    }
}
