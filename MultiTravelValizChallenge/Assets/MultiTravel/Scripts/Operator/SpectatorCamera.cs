using System.Collections;
using UnityEngine;

namespace MultiTravel.Operator
{
    /// <summary>
    /// Third-person "spectator" view for the operator monitor. Add this component to a GameObject in the Main scene and place
    /// that GameObject where the camera should stand (about 2.2 m behind and to the left of the player); the component creates
    /// the <see cref="Camera"/> if needed and renders it manually into a 1280x720 <see cref="RenderTexture"/> at 30 fps.
    /// <para>
    /// The camera never renders into the headset or a display: it is disabled, has a target texture and
    /// <c>stereoTargetEye = None</c>, and <see cref="Camera.Render"/> is only called while <see cref="Active"/> is true
    /// (the operator screen switches it on for Welcome, Loading, Countdown, Playing and the result states). The look direction
    /// follows <see cref="followTarget"/> (or the fixed <see cref="lookAtPoint"/>) with exponential smoothing.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpectatorCamera : MonoBehaviour
    {
        [Header("Look target")]
        [Tooltip("Optional transform (e.g. the player's head). The camera looks at its position + Follow Offset, smoothed.")]
        [SerializeField] private Transform followTarget;

        [Tooltip("World point to look at when no Follow Target is assigned (also the initial point).")]
        [SerializeField] private Vector3 lookAtPoint = new Vector3(0f, 1.1f, 0.45f);

        [Tooltip("Offset added to the Follow Target position (metres, world space).")]
        [SerializeField] private Vector3 followOffset = new Vector3(0f, -0.25f, 0.35f);

        [Tooltip("Smoothing time of the look-at point in seconds.")]
        [SerializeField, Min(0.01f)] private float smoothTime = 0.35f;

        [Header("Mirror the headset")]
        [Tooltip("Show exactly what the participant sees: the camera follows the headset camera (lightly smoothed).")]
        [SerializeField] private bool mirrorHeadset = true;

        [Tooltip("Vertical field of view used when mirroring the headset.")]
        [SerializeField, Range(40f, 110f)] private float headsetFieldOfView = 78f;

        [Header("Render")]
        [SerializeField, Range(5, 60)] private int framesPerSecond = 30;
        [SerializeField] private int width = 1280;
        [SerializeField] private int height = 720;
        [SerializeField, Range(30f, 90f)] private float fieldOfView = 62f;
        [SerializeField] private float nearClip = 0.1f;
        [SerializeField] private float farClip = 25f;
        [SerializeField] private LayerMask cullingMask = ~0;

        private static SpectatorCamera instance;

        private Camera cam;
        private RenderTexture texture;
        private Coroutine routine;
        private Vector3 smoothedTarget;
        private Vector3 velocity;
        private bool targetInitialised;
        private float lastRenderRealtime;

        /// <summary>The active spectator camera, or null when the scene has none.</summary>
        public static SpectatorCamera Instance => instance;

        /// <summary>The 1280x720 colour target (created lazily; null before the first Awake).</summary>
        public RenderTexture Texture => texture;

        /// <summary>True after the first frame was rendered into <see cref="Texture"/>.</summary>
        public bool HasFrame { get; private set; }

        /// <summary>When false the camera is idle and nothing is rendered.</summary>
        public bool Active { get; set; }

        /// <summary>Optional transform to follow (set by the scene builder or at runtime).</summary>
        public Transform FollowTarget
        {
            get => followTarget;
            set => followTarget = value;
        }

        /// <summary>Fixed look-at point used when there is no follow target.</summary>
        public Vector3 LookAtPoint
        {
            get => lookAtPoint;
            set => lookAtPoint = value;
        }

        private void Awake()
        {
            EnsureCamera();
        }

        private void OnEnable()
        {
            instance = this;
            EnsureCamera();
            if (routine == null)
            {
                routine = StartCoroutine(RenderLoop());
            }
        }

        private void OnDisable()
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }

            if (instance == this)
            {
                instance = null;
            }
        }

        private void OnDestroy()
        {
            if (texture != null)
            {
                if (cam != null && cam.targetTexture == texture)
                {
                    cam.targetTexture = null;
                }

                texture.Release();
                Destroy(texture);
                texture = null;
            }
        }

        private void EnsureCamera()
        {
            if (cam == null)
            {
                cam = GetComponent<Camera>();
                if (cam == null)
                {
                    cam = gameObject.AddComponent<Camera>();
                }
            }

            if (texture == null)
            {
                texture = new RenderTexture(Mathf.Max(64, width), Mathf.Max(64, height), 24, RenderTextureFormat.ARGB32)
                {
                    name = "MT_SpectatorView",
                    antiAliasing = 1,
                    useMipMap = false,
                    autoGenerateMips = false,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                texture.Create();
            }

            // Render only on demand into the texture; never to a display and never to the headset.
            cam.enabled = false;
            cam.targetTexture = texture;
#pragma warning disable CS0618
            cam.stereoTargetEye = StereoTargetEyeMask.None;
#pragma warning restore CS0618
            cam.cullingMask = cullingMask;
            cam.fieldOfView = fieldOfView;
            cam.nearClipPlane = nearClip;
            cam.farClipPlane = farClip;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.allowDynamicResolution = false;
            cam.useOcclusionCulling = false;
            cam.depth = -100f;
            cam.tag = "Untagged";
            var listener = GetComponent<AudioListener>();
            if (listener != null)
            {
                listener.enabled = false;
            }
        }

        private IEnumerator RenderLoop()
        {
            var wait = new WaitForSecondsRealtime(1f / Mathf.Max(1, framesPerSecond));
            while (true)
            {
                yield return wait;
                if (!Active || cam == null || texture == null)
                {
                    continue;
                }

                float now = Time.realtimeSinceStartup;
                float dt = Mathf.Clamp(now - lastRenderRealtime, 0.001f, 0.25f);
                lastRenderRealtime = now;
                AimCamera(dt);
                cam.Render();
                HasFrame = true;
            }
        }

        private void AimCamera(float deltaTime)
        {
            if (mirrorHeadset)
            {
                var head = Camera.main;
                if (head != null && head.isActiveAndEnabled && head.gameObject != gameObject)
                {
                    var t = head.transform;
                    float k = 1f - Mathf.Exp(-deltaTime * 16f);
                    if (!targetInitialised)
                    {
                        transform.SetPositionAndRotation(t.position, t.rotation);
                        targetInitialised = true;
                    }
                    else
                    {
                        transform.SetPositionAndRotation(t.position, Quaternion.Slerp(transform.rotation, t.rotation, k));
                    }

                    cam.fieldOfView = headsetFieldOfView;
                    return;
                }

                cam.fieldOfView = fieldOfView;
                targetInitialised = false;
            }

            Vector3 target = followTarget != null ? followTarget.position + followOffset : lookAtPoint;
            if (!targetInitialised)
            {
                smoothedTarget = target;
                velocity = Vector3.zero;
                targetInitialised = true;
            }
            else
            {
                smoothedTarget = Vector3.SmoothDamp(smoothedTarget, target, ref velocity, smoothTime, float.PositiveInfinity, deltaTime);
            }

            Vector3 direction = smoothedTarget - transform.position;
            if (direction.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            }
        }
    }
}
