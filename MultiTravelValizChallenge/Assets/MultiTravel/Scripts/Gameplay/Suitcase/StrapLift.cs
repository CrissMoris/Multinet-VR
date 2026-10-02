using MultiTravel.Gameplay.Common;
using UnityEngine;

namespace MultiTravel.Gameplay.Suitcase
{
    /// <summary>
    /// Raises the suitcase's compression straps with the packed stack (OVERHAUL_PLAN §5): the strap transform rises by
    /// <c>clamp(StackHeight - restHeight, 0, maxLift)</c> along its parent's up axis, smoothed. Purely visual.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StrapLift : MonoBehaviour
    {
        /// <summary>Art node name of the straps inside the suitcase model.</summary>
        public const string StrapsNodeName = "Straps";

        [SerializeField]
        [Tooltip("Suitcase whose StackHeight drives the straps. Defaults to a SuitcaseController in the parents.")]
        private SuitcaseController suitcase;

        [SerializeField]
        [Tooltip("Strap transform (art node 'Straps'). Found by name below the suitcase when empty.")]
        private Transform strap;

        [SerializeField]
        [Tooltip("Stack height (metres above the lowest slot) at which the straps start to rise.")]
        [Min(0f)]
        private float restHeight = 0.10f;

        [SerializeField]
        [Tooltip("Maximum rise of the straps (metres).")]
        [Min(0f)]
        private float maxLift = 0.12f;

        [SerializeField]
        [Tooltip("Smoothing time of the rise (seconds).")]
        [Min(0.01f)]
        private float smoothTime = 0.18f;

        private Vector3 restLocalPosition;
        private bool hasRest;
        private float current;
        private float velocity;

        /// <summary>Current rise of the straps in metres.</summary>
        public float CurrentLift => current;

        /// <summary>Rise the straps are moving towards (metres).</summary>
        public float TargetLift => suitcase == null ? 0f : Mathf.Clamp(suitcase.StackHeight - restHeight, 0f, maxLift);

        /// <summary>Generator / test API.</summary>
        public void Configure(SuitcaseController target, Transform strapTransform, float startHeight, float maximumLift)
        {
            suitcase = target;
            strap = strapTransform;
            restHeight = Mathf.Max(0f, startHeight);
            maxLift = Mathf.Max(0f, maximumLift);
            hasRest = false;
            CaptureRest();
        }

        /// <summary>Snaps the straps to their target (reset paths, tests).</summary>
        public void SnapToTarget()
        {
            CaptureRest();
            current = TargetLift;
            velocity = 0f;
            ApplyLift();
        }

        private void Awake()
        {
            if (suitcase == null)
            {
                suitcase = GetComponentInParent<SuitcaseController>();
            }

            if (strap == null)
            {
                var root = suitcase != null ? suitcase.transform : transform;
                strap = FindDeep(root, StrapsNodeName);
            }

            CaptureRest();
        }

        private void Start()
        {
            if (suitcase == null || strap == null)
            {
                Debug.LogWarning(ServiceResolver.LogPrefix + "StrapLift: suitcase or strap transform missing; straps stay static.", this);
            }
        }

        private void LateUpdate()
        {
            if (suitcase == null || strap == null)
            {
                return;
            }

            float target = TargetLift;
            if (Mathf.Abs(target - current) < 1e-4f && Mathf.Abs(velocity) < 1e-4f)
            {
                return;
            }

            current = Mathf.SmoothDamp(current, target, ref velocity, smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
            ApplyLift();
        }

        private void CaptureRest()
        {
            if (hasRest || strap == null)
            {
                return;
            }

            restLocalPosition = strap.localPosition;
            hasRest = true;
        }

        private void ApplyLift()
        {
            if (strap == null || !hasRest)
            {
                return;
            }

            var parent = strap.parent;
            var upLocal = parent != null ? parent.InverseTransformDirection(Vector3.up) : Vector3.up;
            var scale = parent != null ? parent.lossyScale.y : 1f;
            strap.localPosition = restLocalPosition + upLocal.normalized * (current / Mathf.Max(1e-4f, Mathf.Abs(scale)));
        }

        /// <summary>Depth-first search for a child named <paramref name="name"/>.</summary>
        public static Transform FindDeep(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
