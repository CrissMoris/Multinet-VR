using System;
using UnityEngine;

namespace MultiTravel.Gameplay.Items
{
    /// <summary>Visual variant of a garment: displayed on a hanger in the wardrobe, folded inside the suitcase.</summary>
    public enum ItemVariant
    {
        Hanging = 0,
        Folded = 1
    }

    /// <summary>
    /// Switches a garment prefab between its <c>Hanging</c> (display) and <c>Folded</c> (packed) child visuals
    /// (OVERHAUL_PLAN §5). The item keeps one root <see cref="BoxCollider"/> that XRI registers once; it is resized to the
    /// bounds of the active variant, so grabbing works in both variants.
    /// <para>
    /// <see cref="SuitcaseController"/> swaps to <see cref="ItemVariant.Folded"/> half-way through the settle tween;
    /// <see cref="ProductItem"/> swaps back to <see cref="ItemVariant.Hanging"/> whenever the item returns home or to the pool.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ItemVisualVariant : MonoBehaviour
    {
        /// <summary>Child name of the display visual.</summary>
        public const string HangingChildName = "Hanging";

        /// <summary>Child name of the packed visual.</summary>
        public const string FoldedChildName = "Folded";

        [SerializeField]
        [Tooltip("Display visual (on the hanger). Found by name when empty.")]
        private Transform hangingVisual;

        [SerializeField]
        [Tooltip("Packed visual (folded). Found by name when empty.")]
        private Transform foldedVisual;

        [SerializeField]
        [Tooltip("Root collider resized to the active variant. Defaults to the BoxCollider on this GameObject.")]
        private BoxCollider bodyCollider;

        [SerializeField]
        [Tooltip("Collider bounds (item-root space) of the hanging visual. Computed from the meshes when empty.")]
        private Bounds hangingBounds;

        [SerializeField]
        [Tooltip("Collider bounds (item-root space) of the folded visual. Computed from the meshes when empty.")]
        private Bounds foldedBounds;

        [SerializeField]
        [Tooltip("Optional anchor (inside-suitcase test point) moved to the centre of the active variant.")]
        private Transform anchor;

        [SerializeField]
        private Vector3 hangingAnchorPosition;

        [SerializeField]
        private Vector3 foldedAnchorPosition;

        private bool initialized;
        private ItemVariant current = ItemVariant.Hanging;

        /// <summary>Raised after the active variant changed (variant component, new variant).</summary>
        public event Action<ItemVisualVariant, ItemVariant> Changed;

        /// <summary>Active variant.</summary>
        public ItemVariant Current
        {
            get
            {
                EnsureInitialized();
                return current;
            }
        }

        /// <summary>The display visual.</summary>
        public Transform HangingVisual
        {
            get
            {
                EnsureInitialized();
                return hangingVisual;
            }
        }

        /// <summary>The packed visual.</summary>
        public Transform FoldedVisual
        {
            get
            {
                EnsureInitialized();
                return foldedVisual;
            }
        }

        /// <summary>Collider bounds of <paramref name="variant"/> in item-root space.</summary>
        public Bounds BoundsFor(ItemVariant variant)
        {
            EnsureInitialized();
            return variant == ItemVariant.Hanging ? hangingBounds : foldedBounds;
        }

        /// <summary>Generator API: wires both visuals, the root collider and the per-variant collider bounds.</summary>
        public void Configure(Transform hanging, Transform folded, BoxCollider collider, Bounds hangingColliderBounds, Bounds foldedColliderBounds)
        {
            hangingVisual = hanging;
            foldedVisual = folded;
            bodyCollider = collider;
            hangingBounds = hangingColliderBounds;
            foldedBounds = foldedColliderBounds;
            initialized = false;
            EnsureInitialized();
            Apply(current, false);
        }

        /// <summary>Generator API: anchor transform and its local position per variant (usually each variant's bounds centre).</summary>
        public void SetAnchor(Transform anchorTransform, Vector3 hangingLocal, Vector3 foldedLocal)
        {
            anchor = anchorTransform;
            hangingAnchorPosition = hangingLocal;
            foldedAnchorPosition = foldedLocal;
            if (initialized)
            {
                Apply(current, false);
            }
        }

        /// <summary>Activates <paramref name="variant"/> and resizes the root collider. Idempotent.</summary>
        public void SetVariant(ItemVariant variant)
        {
            EnsureInitialized();
            if (variant == current)
            {
                return;
            }

            Apply(variant, true);
        }

        /// <summary>
        /// Bounds of every <see cref="MeshFilter"/> below <paramref name="visual"/> expressed in <paramref name="root"/> space
        /// (works while <paramref name="visual"/> itself is inactive). Children named <see cref="ProductItem.OutlineChildName"/> and
        /// meshes deactivated below <paramref name="visual"/> are ignored.
        /// Returns a 10 cm box at the root when there is no mesh.
        /// </summary>
        public static Bounds ComputeVisualBounds(Transform root, Transform visual)
        {
            var result = new Bounds(Vector3.zero, Vector3.one * 0.1f);
            if (root == null || visual == null)
            {
                return result;
            }

            bool any = false;
            var filters = visual.GetComponentsInChildren<MeshFilter>(true);
            var toRoot = root.worldToLocalMatrix;
            for (int i = 0; i < filters.Length; i++)
            {
                var filter = filters[i];
                if (filter == null || filter.sharedMesh == null || filter.gameObject.name == ProductItem.OutlineChildName ||
                    IsDisabledBelow(filter.transform, visual))
                {
                    continue;
                }

                var meshBounds = filter.sharedMesh.bounds;
                var matrix = toRoot * filter.transform.localToWorldMatrix;
                var min = meshBounds.min;
                var max = meshBounds.max;
                for (int corner = 0; corner < 8; corner++)
                {
                    var local = new Vector3(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);
                    var point = matrix.MultiplyPoint3x4(local);
                    if (!any)
                    {
                        result = new Bounds(point, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        result.Encapsulate(point);
                    }
                }
            }

            return result;
        }

        /// <summary>True when <paramref name="node"/> or an ancestor below <paramref name="top"/> is deactivated (activeSelf false).</summary>
        private static bool IsDisabledBelow(Transform node, Transform top)
        {
            for (var t = node; t != null && t != top; t = t.parent)
            {
                if (!t.gameObject.activeSelf)
                {
                    return true;
                }
            }

            return false;
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            if (hangingVisual == null)
            {
                hangingVisual = transform.Find(HangingChildName);
            }

            if (foldedVisual == null)
            {
                foldedVisual = transform.Find(FoldedChildName);
            }

            if (bodyCollider == null)
            {
                bodyCollider = GetComponent<BoxCollider>();
            }

            if (hangingBounds.size == Vector3.zero)
            {
                hangingBounds = ComputeVisualBounds(transform, hangingVisual);
            }

            if (foldedBounds.size == Vector3.zero)
            {
                foldedBounds = ComputeVisualBounds(transform, foldedVisual);
            }

            // The active child decides the starting variant (prefabs are saved with the display variant active).
            current = foldedVisual != null && foldedVisual.gameObject.activeSelf &&
                      (hangingVisual == null || !hangingVisual.gameObject.activeSelf)
                ? ItemVariant.Folded
                : ItemVariant.Hanging;
            Apply(current, false);
        }

        private void Apply(ItemVariant variant, bool notify)
        {
            current = variant;
            if (hangingVisual != null)
            {
                hangingVisual.gameObject.SetActive(variant == ItemVariant.Hanging);
            }

            if (foldedVisual != null)
            {
                foldedVisual.gameObject.SetActive(variant == ItemVariant.Folded);
            }

            if (bodyCollider != null)
            {
                var bounds = variant == ItemVariant.Hanging ? hangingBounds : foldedBounds;
                bodyCollider.center = bounds.center;
                bodyCollider.size = Vector3.Max(bounds.size, Vector3.one * 0.01f);
            }

            if (anchor != null && anchor != transform)
            {
                anchor.localPosition = variant == ItemVariant.Hanging ? hangingAnchorPosition : foldedAnchorPosition;
            }

            if (notify)
            {
                Changed?.Invoke(this, variant);
            }
        }
    }
}
