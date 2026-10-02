using System;
using MultiTravel.Core.Session;
using MultiTravel.Gameplay.Common;
using UnityEngine;

namespace MultiTravel.Gameplay.Presentation
{
    /// <summary>
    /// Camera-attached fade (OVERHAUL_PLAN §5): an unlit, alpha-blended quad parked a few centimetres in front of the
    /// headset in the Overlay queue, so it draws after everything else. Fades out / in over <see cref="fadeSeconds"/>.
    /// <para>
    /// Session hooks: <c>SessionResetRequested</c> cuts to black in the same frame (the reset re-poses and disables every
    /// item synchronously, so the black frame hides it) and fades back in; <c>Loading</c> cuts to black and the fade-in
    /// runs when Countdown / Playing is entered, hiding the item set popping into the wardrobe.
    /// </para>
    /// <para>
    /// URP's Unlit shader has no ZTest override, so "always on top" is achieved by distance (nearer than any scene
    /// geometry can be) plus the Overlay queue, which is equivalent for a quad glued to the camera.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScreenFade : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Camera the quad follows. Defaults to Camera.main.")]
        private Camera targetCamera;

        [SerializeField]
        [Min(0.02f)]
        private float fadeSeconds = 0.3f;

        [SerializeField]
        [Tooltip("Distance of the quad in front of the camera (metres). Must be beyond the near clip plane.")]
        [Min(0.02f)]
        private float distance = 0.06f;

        [SerializeField]
        private Color color = Color.black;

        [SerializeField]
        [Tooltip("Cut to black on session reset and on Loading, fade in on Countdown / Playing / Welcome.")]
        private bool autoSessionHooks = true;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private SessionController session;
        private bool subscribed;
        private MeshRenderer quadRenderer;
        private Transform quad;
        private Material material;
        private Mesh mesh;
        private MaterialPropertyBlock block;
        private int colorProperty;
        private float alpha;
        private float from;
        private float to;
        private float animStart = -10f;
        private float animSeconds;
        private bool animating;
        private float nextCameraLookup;
        private bool built;

        /// <summary>Current opacity (0 = clear, 1 = black).</summary>
        public float Alpha => alpha;

        /// <summary>True while a fade is in progress.</summary>
        public bool IsFading => animating;

        /// <summary>Raised when a fade-out reached full black.</summary>
        public event Action FadeOutCompleted;

        /// <summary>Raised when a fade-in reached fully clear.</summary>
        public event Action FadeInCompleted;

        /// <summary>Generator / test API.</summary>
        public void Configure(Camera camera)
        {
            targetCamera = camera;
            if (built)
            {
                Attach();
            }
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
        }

        /// <summary>Fades to black over <paramref name="seconds"/> (negative = default).</summary>
        public void FadeOut(float seconds = -1f)
        {
            StartFade(1f, seconds);
        }

        /// <summary>Fades to clear over <paramref name="seconds"/> (negative = default).</summary>
        public void FadeIn(float seconds = -1f)
        {
            StartFade(0f, seconds);
        }

        /// <summary>Cuts to black immediately (used before synchronous scene re-posing).</summary>
        public void SetOpaque()
        {
            animating = false;
            Apply(1f);
        }

        /// <summary>Cuts to clear immediately.</summary>
        public void SetClear()
        {
            animating = false;
            Apply(0f);
        }

        // ----- Unity -----

        private void Awake()
        {
            Build();
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void Start()
        {
            if (session == null && autoSessionHooks)
            {
                ServiceResolver.TryResolve(ref session);
                Subscribe();
            }

            Attach();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (material != null)
            {
                Destroy(material);
            }

            if (mesh != null)
            {
                Destroy(mesh);
            }
        }

        private void Update()
        {
            if (quad == null)
            {
                return;
            }

            if (targetCamera == null || !targetCamera.isActiveAndEnabled)
            {
                if (Time.unscaledTime >= nextCameraLookup)
                {
                    nextCameraLookup = Time.unscaledTime + 0.5f;
                    Attach();
                }
            }

            if (!animating)
            {
                return;
            }

            float k = animSeconds <= 0f ? 1f : (Time.unscaledTime - animStart) / animSeconds;
            if (k >= 1f)
            {
                animating = false;
                Apply(to);
                if (to >= 1f)
                {
                    FadeOutCompleted?.Invoke();
                }
                else if (to <= 0f)
                {
                    FadeInCompleted?.Invoke();
                }
            }
            else
            {
                Apply(Mathf.Lerp(from, to, PresentationStyle.SmoothStep(k)));
            }
        }

        // ----- internals -----

        private void Build()
        {
            if (built)
            {
                return;
            }

            built = true;
            block = new MaterialPropertyBlock();
            mesh = PresentationStyle.CreateQuadMesh("MT_FadeQuad");
            var go = new GameObject("Fade quad");
            go.transform.SetParent(transform, false);
            quad = go.transform;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            quadRenderer = go.AddComponent<MeshRenderer>();
            quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quadRenderer.receiveShadows = false;
            quadRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            quadRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            material = PresentationStyle.CreateMaterial("MT_ScreenFade", new Color(color.r, color.g, color.b, 0f), Color.black, true, true);
            PresentationStyle.MakeDoubleSided(material);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Overlay;
            quadRenderer.sharedMaterial = material;
            colorProperty = material.HasProperty(BaseColorId) ? BaseColorId : ColorId;
            Apply(0f);
        }

        private void Attach()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            if (targetCamera == null || quad == null)
            {
                return;
            }

            var cameraTransform = targetCamera.transform;
            float safeDistance = Mathf.Max(distance, targetCamera.nearClipPlane + 0.01f);
            quad.SetParent(cameraTransform, false);
            quad.localPosition = new Vector3(0f, 0f, safeDistance);
            quad.localRotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
            // Generously larger than the frustum at this distance (stereo eyes are offset ~3 cm each).
            float size = 2f * safeDistance * Mathf.Tan(Mathf.Max(60f, targetCamera.fieldOfView) * 0.5f * Mathf.Deg2Rad) * 4f + 0.4f;
            quad.localScale = new Vector3(size, size, 1f);
        }

        private void StartFade(float target, float seconds)
        {
            Build();
            from = alpha;
            to = target;
            animSeconds = seconds < 0f ? fadeSeconds : seconds;
            animStart = Time.unscaledTime;
            animating = true;
            if (animSeconds <= 0f)
            {
                animating = false;
                Apply(target);
            }
        }

        private void Apply(float value)
        {
            alpha = Mathf.Clamp01(value);
            bool visible = alpha > 0.001f;
            if (quadRenderer.enabled != visible)
            {
                quadRenderer.enabled = visible;
            }

            if (!visible)
            {
                return;
            }

            quadRenderer.GetPropertyBlock(block);
            block.SetColor(colorProperty, new Color(color.r, color.g, color.b, alpha));
            quadRenderer.SetPropertyBlock(block);
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
            if (!autoSessionHooks)
            {
                return;
            }

            // The director re-poses and disables every item synchronously in this very callback chain: black first.
            SetOpaque();
            FadeIn();
        }

        private void OnStateChanged(SessionState previous, SessionState next)
        {
            if (!autoSessionHooks)
            {
                return;
            }

            switch (next)
            {
                case SessionState.Loading:
                    SetOpaque();
                    break;
                case SessionState.Countdown:
                case SessionState.Playing:
                    if (alpha > 0f)
                    {
                        FadeIn();
                    }

                    break;
                case SessionState.Fatal:
                    FadeIn();
                    break;
            }
        }
    }
}
