using MultiTravel.Core.Products;
using UnityEngine;

namespace MultiTravel.Gameplay.Items
{
    /// <summary>
    /// A spawn position in the room (shelf, wardrobe, table, bed …). The transform's position / rotation is the item's
    /// spawn pose. <see cref="PreferredCategories"/> is a hint: <see cref="SpawnSlotLayout"/> fills matching slots first.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpawnSlot : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Categories that should spawn here. Empty = any category.")]
        private ProductCategory[] preferredCategories = new ProductCategory[0];

        [SerializeField]
        [Tooltip("Extra height above the slot transform so the item does not intersect the surface it spawns on.")]
        [Min(0f)]
        private float heightOffset = 0.02f;

        /// <summary>Category hint (empty = any).</summary>
        public ProductCategory[] PreferredCategories => preferredCategories;

        /// <summary>True when the slot has no category preference.</summary>
        public bool AcceptsAnyCategory => preferredCategories == null || preferredCategories.Length == 0;

        /// <summary>Height added along the slot's up axis.</summary>
        public float HeightOffset => heightOffset;

        /// <summary>Generator / test API: sets the category hint.</summary>
        public void SetPreferredCategories(params ProductCategory[] categories)
        {
            preferredCategories = categories ?? new ProductCategory[0];
        }

        /// <summary>Generator / test API: sets the height offset.</summary>
        public void SetHeightOffset(float offset)
        {
            heightOffset = Mathf.Max(0f, offset);
        }

        /// <summary>True when <paramref name="category"/> is explicitly preferred by this slot.</summary>
        public bool Prefers(ProductCategory category)
        {
            if (preferredCategories == null)
            {
                return false;
            }

            for (int i = 0; i < preferredCategories.Length; i++)
            {
                if (preferredCategories[i] == category)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// World pose for <paramref name="item"/>: the slot pose lifted so the item's collider bounds rest on the slot surface.
        /// </summary>
        public Pose PoseFor(ProductItem item)
        {
            return ItemPlacementMath.PoseOnSurface(transform, item, heightOffset);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = AcceptsAnyCategory ? new Color(0.1f, 0.7f, 0.58f, 0.8f) : new Color(0.95f, 0.57f, 0f, 0.8f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(0f, 0.05f, 0f), new Vector3(0.2f, 0.1f, 0.2f));
        }
    }
}
