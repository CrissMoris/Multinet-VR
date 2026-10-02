using System;
using MultiTravel.Core.Services;
using MultiTravel.Core.Session;
using MultiTravel.Gameplay.Audio;
using MultiTravel.Gameplay.Common;
using MultiTravel.Gameplay.Director;
using MultiTravel.Gameplay.Items;
using MultiTravel.Gameplay.Presentation;
using MultiTravel.Gameplay.Suitcase;
using UnityEngine;

namespace MultiTravel.Gameplay.Tutorial
{
    /// <summary>Phase of the in-headset tutorial.</summary>
    public enum TutorialPhase
    {
        /// <summary>Not in Instructions.</summary>
        Inactive = 0,

        /// <summary>Practice tag floats and glows over the suitcase handle; caption "Tut".</summary>
        Grab = 1,

        /// <summary>Tag is held (or dropped outside); pulsing ring over the suitcase; caption "Valize bırak".</summary>
        Place = 2,

        /// <summary>Tag landed in the suitcase; caption "Harika! Hazırsın. Görevli oyunu başlatacak."</summary>
        Passed = 3
    }

    /// <summary>
    /// Instructions-state practice (OVERHAUL_PLAN §5). The <see cref="GameplayDirector"/> owns the practice item's
    /// lifecycle (<see cref="GameplayDirector.BeginPractice"/> activates and whitelists the <c>practice-tag</c>, the
    /// suitcase accepts it without scoring, <see cref="GameplayDirector.EndPractice"/> withdraws it); this controller
    /// drives the hand-off and everything the participant sees: the tag floats and glows above the suitcase handle, a
    /// pulsing ring with a large Turkish caption guides the hand ("Tut" → "Valize bırak"), and once the tag is grabbed and
    /// dropped into the suitcase the tutorial passes ("Harika! Hazırsın. Görevli oyunu başlatacak."). The score service is
    /// never touched. The result is published as <see cref="ITutorialStatus"/> in <see cref="AppServices"/> for the
    /// operator screen and resets whenever Instructions is left.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TutorialController : MonoBehaviour, ITutorialStatus
    {
        public const string CaptionGrab = "Tut";
        public const string CaptionPlace = "Valize bırak";
        public const string CaptionPassed = "Harika! Hazırsın. Görevli oyunu başlatacak.";
        public const string CaptionNoPractice = "Görevli oyunu başlatacak.";

        [Header("Scene references")]
        [SerializeField]
        [Tooltip("Director that owns the practice item (found in the scene when empty).")]
        private GameplayDirector director;

        [SerializeField]
        [Tooltip("Suitcase (volume centre for the ring and captions). Defaults to the scene's SuitcaseController.")]
        private SuitcaseController suitcase;

        [SerializeField] private AudioDirector audioDirector;

        [Header("Look")]
        [SerializeField] private float bobAmplitude = 0.015f;
        [SerializeField] private float bobPeriod = 2.4f;
        [SerializeField] private float captionFontSize = 0.6f;
        [SerializeField] private float ringRadius = 0.12f;
        [SerializeField] private float ringPulseSeconds = 1.3f;

        [SerializeField]
        [Tooltip("Seconds a released tag may rest outside the suitcase before it floats back to the handle.")]
        [Min(0.2f)]
        private float strayReturnSeconds = 1.5f;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private SessionController session;
        private bool subscribed;
        private GameplayDirector subscribedDirector;
        private ProductItem item;
        private bool itemSubscribed;
        private Renderer[] itemRenderers = new Renderer[0];
        private MaterialPropertyBlock block;
        private FloatingCaption caption;
        private Transform ring;
        private MeshRenderer ringRenderer;
        private Material ringMaterial;
        private Mesh ringMesh;
        private MaterialPropertyBlock ringBlock;
        private Camera viewCamera;
        private float nextCameraLookup;
        private Pose floatPose;
        private float phaseStart;
        private float strayTimer;
        private float nextSettleCheck;
        private bool passed;
        private bool warnedMissing;

        /// <summary>Current phase.</summary>
        public TutorialPhase Phase { get; private set; } = TutorialPhase.Inactive;

        /// <summary>True once the participant grabbed and placed the practice tag in this Instructions state.</summary>
        public bool Passed => passed;

        /// <summary>Alias of <see cref="Passed"/> (OVERHAUL_PLAN naming).</summary>
        public bool TutorialPassed => passed;

        /// <summary>Raised when <see cref="Passed"/> changes.</summary>
        public event Action Changed;

        /// <summary>Raised once per Instructions state when the tutorial passes.</summary>
        public event Action TutorialCompleted;

        /// <summary>The practice item in use (null outside Instructions or when missing).</summary>
        public ProductItem PracticeItem => item;

        /// <summary>The caption (tests read <c>Caption.Text.text</c>).</summary>
        public FloatingCaption Caption => caption;

        /// <summary>Generator API.</summary>
        public void Configure(GameplayDirector gameplayDirector, SuitcaseController suitcaseController, AudioDirector audio)
        {
            SetDirector(gameplayDirector);
            suitcase = suitcaseController;
            audioDirector = audio;
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
            Sync(session.State);
        }

        // ----- Unity -----

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            ringBlock = new MaterialPropertyBlock();
            caption = new FloatingCaption("Tutorial caption", transform, captionFontSize, PresentationStyle.Ivory, new Vector2(1.1f, 0.3f), true);
            BuildRing();
            AppServices.Register<ITutorialStatus>(this);
        }

        private void OnEnable()
        {
            Subscribe();
            SubscribeDirector();
        }

        private void Start()
        {
            if (director == null)
            {
                SetDirector(FindAnyObjectByType<GameplayDirector>());
            }

            if (suitcase == null)
            {
                suitcase = FindAnyObjectByType<SuitcaseController>();
            }

            if (session == null)
            {
                if (ServiceResolver.Resolve(ref session, this, nameof(TutorialController)))
                {
                    Subscribe();
                    Sync(session.State);
                }
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
            UnsubscribeDirector();
            if (Phase != TutorialPhase.Inactive)
            {
                EndPractice();
            }
        }

        private void OnDestroy()
        {
            Unsubscribe();
            UnsubscribeDirector();
            if (AppServices.TryGet(out ITutorialStatus registered) && ReferenceEquals(registered, this))
            {
                AppServices.Unregister<ITutorialStatus>();
            }

            caption?.Destroy();
            if (ringMaterial != null)
            {
                Destroy(ringMaterial);
            }

            if (ringMesh != null)
            {
                Destroy(ringMesh);
            }
        }

        private void Update()
        {
            if (Phase == TutorialPhase.Inactive || item == null)
            {
                return;
            }

            float now = Time.unscaledTime;
            switch (Phase)
            {
                case TutorialPhase.Grab:
                    if (item.State == ProductItemState.Free && !item.IsReturning)
                    {
                        float bob = Mathf.Sin((now - phaseStart) * (2f * Mathf.PI / bobPeriod)) * bobAmplitude;
                        var position = floatPose.position + Vector3.up * bob;
                        var rotation = floatPose.rotation * Quaternion.AngleAxis((now - phaseStart) * 25f, Vector3.up);
                        item.transform.SetPositionAndRotation(position, rotation);
                        Glow(0.5f + 0.5f * Mathf.Sin((now - phaseStart) * 3f));
                        PlaceRing(position + Vector3.down * 0.02f);
                    }

                    break;
                case TutorialPhase.Place:
                    PlaceRing(SuitcaseTarget());
                    if (item.State == ProductItemState.Free && !item.IsReturning && now >= nextSettleCheck)
                    {
                        nextSettleCheck = now + 0.2f;
                        bool inside = suitcase != null && suitcase.IsInsideVolume(item);
                        bool resting = item.IsAtRest(0.05f, 0.6f);
                        if (!inside && resting)
                        {
                            strayTimer += 0.2f;
                            if (strayTimer >= strayReturnSeconds)
                            {
                                item.ReturnToSpawn(true);
                                BeginGrabPhase();
                            }
                        }
                        else
                        {
                            strayTimer = 0f;
                        }
                    }

                    break;
            }

            PulseRing(now);
        }

        private void LateUpdate()
        {
            if (caption != null && caption.IsShown)
            {
                caption.Tick(ResolveCamera());
            }
        }

        // ----- flow -----

        private void Sync(SessionState state)
        {
            if (state == SessionState.Instructions)
            {
                if (Phase == TutorialPhase.Inactive)
                {
                    BeginPractice();
                }
            }
            else if (Phase != TutorialPhase.Inactive)
            {
                EndPractice();
            }
        }

        private void BeginPractice()
        {
            SetPassed(false);
            item = null;
            if (director != null && director.BeginPractice())
            {
                item = director.PracticeItem;
            }

            if (item == null)
            {
                if (!warnedMissing)
                {
                    warnedMissing = true;
                    Debug.LogWarning(ServiceResolver.LogPrefix + "TutorialController: no practice item (GameplayDirector.ConfigurePractice); the tutorial only shows the caption.", this);
                }

                Phase = TutorialPhase.Passed;
                caption.Show(CaptionNoPractice, CaptionAnchor());
                return;
            }

            itemRenderers = item.GetComponentsInChildren<Renderer>(true);
            floatPose = item.SpawnPose;
            WatchItem();
            BeginGrabPhase();
        }

        private void BeginGrabPhase()
        {
            Phase = TutorialPhase.Grab;
            phaseStart = Time.unscaledTime;
            strayTimer = 0f;
            if (item.State == ProductItemState.Free)
            {
                Freeze();
                item.transform.SetPositionAndRotation(floatPose.position, floatPose.rotation);
            }

            caption.Show(CaptionGrab, floatPose.position + Vector3.up * 0.16f);
            ShowRing(true);
        }

        private void BeginPlacePhase()
        {
            Phase = TutorialPhase.Place;
            phaseStart = Time.unscaledTime;
            strayTimer = 0f;
            nextSettleCheck = Time.unscaledTime + 0.2f;
            Glow(0f);
            caption.Show(CaptionPlace, SuitcaseTarget() + Vector3.up * 0.22f);
            ShowRing(true);
        }

        private void Pass()
        {
            if (Phase == TutorialPhase.Passed)
            {
                return;
            }

            Phase = TutorialPhase.Passed;
            phaseStart = Time.unscaledTime;
            Glow(0f);
            ShowRing(false);
            caption.Show(CaptionPassed, CaptionAnchor());
            if (audioDirector != null)
            {
                audioDirector.PlayConfirm();
            }

            SetPassed(true);
            TutorialCompleted?.Invoke();
        }

        private void EndPractice()
        {
            Phase = TutorialPhase.Inactive;
            caption.Hide();
            ShowRing(false);
            if (item != null)
            {
                Glow(0f);
                UnwatchItem();
            }

            item = null;
            if (director != null)
            {
                director.EndPractice();
            }

            SetPassed(false);
        }

        private void SetPassed(bool value)
        {
            if (passed == value)
            {
                return;
            }

            passed = value;
            Changed?.Invoke();
        }

        // ----- events -----

        private void WatchItem()
        {
            if (item == null || itemSubscribed)
            {
                return;
            }

            item.Grabbed += OnItemGrabbed;
            item.Released += OnItemReleased;
            itemSubscribed = true;
        }

        private void UnwatchItem()
        {
            if (item != null && itemSubscribed)
            {
                item.Grabbed -= OnItemGrabbed;
                item.Released -= OnItemReleased;
            }

            itemSubscribed = false;
        }

        private void OnItemGrabbed(ProductItem grabbed, Transform interactor, ProductItemState previous)
        {
            if (Phase == TutorialPhase.Grab || Phase == TutorialPhase.Passed)
            {
                BeginPlacePhase();
            }
        }

        private void OnItemReleased(ProductItem released, Transform interactor, bool canceled)
        {
            if (Phase != TutorialPhase.Place)
            {
                return;
            }

            if (canceled)
            {
                // Forced release (lock, tracking loss): back to the handle.
                if (released.State == ProductItemState.Free && !released.IsReturning)
                {
                    released.ReturnToSpawn(true);
                }

                BeginGrabPhase();
                return;
            }

            // A release inside the volume is placed by the suitcase itself (PracticeItemPlaced → director.PracticePlaced).
            strayTimer = 0f;
            nextSettleCheck = Time.unscaledTime + 0.2f;
        }

        private void OnPracticePlaced(ProductItem placed)
        {
            if (placed == item && Phase != TutorialPhase.Inactive)
            {
                Pass();
            }
        }

        private void OnPracticeRemoved(ProductItem removed)
        {
            if (removed == item && Phase == TutorialPhase.Passed)
            {
                BeginPlacePhase();
            }
        }

        private void OnPracticeEnded(ProductItem ended)
        {
            if (Phase != TutorialPhase.Inactive)
            {
                EndPractice();
            }
        }

        // ----- helpers -----

        private void SetDirector(GameplayDirector gameplayDirector)
        {
            if (subscribedDirector != null && subscribedDirector != gameplayDirector)
            {
                UnsubscribeDirector();
            }

            director = gameplayDirector;
            SubscribeDirector();
        }

        private void SubscribeDirector()
        {
            if (director == null || subscribedDirector == director)
            {
                return;
            }

            director.PracticePlaced += OnPracticePlaced;
            director.PracticeRemoved += OnPracticeRemoved;
            director.PracticeEnded += OnPracticeEnded;
            subscribedDirector = director;
        }

        private void UnsubscribeDirector()
        {
            if (subscribedDirector != null)
            {
                subscribedDirector.PracticePlaced -= OnPracticePlaced;
                subscribedDirector.PracticeRemoved -= OnPracticeRemoved;
                subscribedDirector.PracticeEnded -= OnPracticeEnded;
                subscribedDirector = null;
            }
        }

        private Vector3 SuitcaseTarget()
        {
            if (suitcase != null && suitcase.PlacementVolume != null)
            {
                var volume = suitcase.PlacementVolume;
                return volume.transform.TransformPoint(volume.center) + Vector3.up * (volume.size.y * 0.5f * volume.transform.lossyScale.y);
            }

            return item != null ? item.SpawnPose.position : transform.position;
        }

        private Vector3 CaptionAnchor()
        {
            return SuitcaseTarget() + Vector3.up * 0.3f;
        }

        private void Freeze()
        {
            var body = item.Body;
            if (body == null)
            {
                return;
            }

            if (!body.isKinematic && item.gameObject.activeInHierarchy)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            body.isKinematic = true;
        }

        private void Glow(float amount)
        {
            if (itemRenderers == null)
            {
                return;
            }

            var emission = PresentationStyle.Teal * (amount * 1.6f);
            for (int i = 0; i < itemRenderers.Length; i++)
            {
                var renderer = itemRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (amount <= 0f)
                {
                    renderer.SetPropertyBlock(null);
                    continue;
                }

                renderer.GetPropertyBlock(block);
                block.SetColor(EmissionColorId, emission);
                renderer.SetPropertyBlock(block);
            }
        }

        private void BuildRing()
        {
            ringMesh = PresentationStyle.CreateRingMesh("MT_TutorialRing", ringRadius * 0.78f, ringRadius, 48);
            var go = new GameObject("Tutorial ring");
            go.transform.SetParent(transform, false);
            ring = go.transform;
            go.AddComponent<MeshFilter>().sharedMesh = ringMesh;
            ringRenderer = go.AddComponent<MeshRenderer>();
            ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ringRenderer.receiveShadows = false;
            ringMaterial = PresentationStyle.CreateMaterial("MT_TutorialRing", PresentationStyle.Teal, Color.black, true, true);
            PresentationStyle.MakeDoubleSided(ringMaterial);
            ringRenderer.sharedMaterial = ringMaterial;
            go.SetActive(false);
        }

        private void ShowRing(bool visible)
        {
            if (ring != null && ring.gameObject.activeSelf != visible)
            {
                ring.gameObject.SetActive(visible);
            }
        }

        private void PlaceRing(Vector3 position)
        {
            if (ring != null)
            {
                ring.position = position;
            }
        }

        private void PulseRing(float now)
        {
            if (ring == null || !ring.gameObject.activeSelf)
            {
                return;
            }

            float k = ((now - phaseStart) % ringPulseSeconds) / ringPulseSeconds;
            ring.localScale = Vector3.one * (0.8f + 0.6f * k);
            var color = PresentationStyle.Teal;
            color.a = 0.9f * (1f - k);
            ringRenderer.GetPropertyBlock(ringBlock);
            ringBlock.SetColor(PresentationStyle.BaseColorProperty, color);
            ringRenderer.SetPropertyBlock(ringBlock);
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

        private void Subscribe()
        {
            if (session == null || subscribed)
            {
                return;
            }

            session.StateChanged += OnStateChanged;
            session.SessionResetRequested += OnSessionResetRequested;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (session != null && subscribed)
            {
                session.StateChanged -= OnStateChanged;
                session.SessionResetRequested -= OnSessionResetRequested;
            }

            subscribed = false;
        }

        private void OnSessionResetRequested()
        {
            if (Phase != TutorialPhase.Inactive)
            {
                EndPractice();
            }
        }

        private void OnStateChanged(SessionState previous, SessionState next)
        {
            if (isActiveAndEnabled)
            {
                Sync(next);
            }
        }
    }
}
