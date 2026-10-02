using MultiTravel.Core.Products;
using UnityEngine;

namespace MultiTravel.Gameplay.Items
{
    /// <summary>Footprint class of a display slot / of an item resting on it (at display scale).</summary>
    public enum SlotSize
    {
        /// <summary>Up to <see cref="SpawnSlot.SmallMax"/> m wide: pens, jewellery, cards, phones.</summary>
        Small = 0,
        /// <summary>Up to <see cref="SpawnSlot.MediumMax"/> m wide: shoes, notebooks, glasses, t-shirts.</summary>
        Medium = 1,
        /// <summary>Wider items (up to about 0.26 m): laptop, bags, towel, hat.</summary>
        Wide = 2
    }

    /// <summary>
    /// A display position in the dressing room (hook, shelf, rack tier, tray, table …). The transform's position / rotation is
    /// the item's home pose (+Z = item forward, +Y = up). <see cref="Zone"/> decides which items may use the slot
    /// (<see cref="SpawnSlotLayout"/> is zone-strict); <see cref="PreferredCategories"/> is a legacy hint inside a zone.
    /// <para>
    /// Items rest on the slot (lowest point on the slot surface), except in <see cref="DisplayZone.Hanging"/> slots where the
    /// slot is the rail hook and the item hangs from it (<see cref="ProductItem.HangPointLocal"/> on the hook).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpawnSlot : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Display zone of this slot. Any = fallback slot for items whose zone has no free slot left.")]
        private DisplayZone zone = DisplayZone.Any;

        [SerializeField]
        [Tooltip("Categories that should spawn here first (within the zone). Empty = any category.")]
        private ProductCategory[] preferredCategories = new ProductCategory[0];

        [SerializeField]
        [Tooltip("Extra height above the slot transform so the item does not intersect the surface it spawns on.")]
        [Min(0f)]
        private float heightOffset = 0.02f;

        /// <summary>Widest item (m, at display scale) that counts as <see cref="SlotSize.Small"/>.</summary>
        public const float SmallMax = 0.15f;

        /// <summary>Widest item (m, at display scale) that counts as <see cref="SlotSize.Medium"/>.</summary>
        public const float MediumMax = 0.26f;

        /// <summary>Items taller than this (m, at display scale) need a tall slot (top row, open above).</summary>
        public const float TallMin = 0.24f;

        [SerializeField]
        [Tooltip("Widest item class this slot holds. Wide (default) = anything.")]
        private SlotSize size = SlotSize.Wide;

        [SerializeField]
        [Tooltip("True when the space above the slot is open, so tall items fit (default: yes).")]
        private bool acceptsTall = true;

        /// <summary>Size class of the slot.</summary>
        public SlotSize Size => size;

        /// <summary>True when tall items (football, gnome, bags) fit above this slot.</summary>
        public bool AcceptsTall => acceptsTall;

        /// <summary>Generator / test API: sets the size class and whether tall items fit.</summary>
        public void SetSize(SlotSize value, bool tall = true)
        {
            size = value;
            acceptsTall = tall;
        }

        /// <summary>Width class for an item width in metres (at display scale).</summary>
        public static SlotSize ClassFor(float width)
        {
            return width <= SmallMax ? SlotSize.Small : width <= MediumMax ? SlotSize.Medium : SlotSize.Wide;
        }

        /// <summary>True when an item of this height (m, at display scale) needs a tall slot.</summary>
        public static bool IsTall(float height)
        {
            return height > TallMin;
        }

        /// <summary>Display zone of the slot.</summary>
        public DisplayZone Zone => zone;

        /// <summary>Category hint (empty = any).</summary>
        public ProductCategory[] PreferredCategories => preferredCategories;

        /// <summary>True when the slot has no category preference.</summary>
        public bool AcceptsAnyCategory => preferredCategories == null || preferredCategories.Length == 0;

        /// <summary>Height added along the slot's up axis.</summary>
        public float HeightOffset => heightOffset;

        /// <summary>True when items hang from this slot (hanging rail hooks).</summary>
        public bool IsHook => zone == DisplayZone.Hanging;

        /// <summary>Generator / test API: sets the display zone.</summary>
        public void SetZone(DisplayZone value)
        {
            zone = value;
        }

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
        /// World pose for <paramref name="item"/> in its display variant: hanging from the hook for hanging slots, otherwise
        /// resting on the slot surface.
        /// </summary>
        public Pose PoseFor(ProductItem item)
        {
            if (IsHook)
            {
                return ItemPlacementMath.PoseHanging(transform.position, transform.rotation, item);
            }

            var bounds = item != null ? item.DisplayBounds : default;
            return ItemPlacementMath.PoseOnSurface(transform.position, transform.rotation, item, bounds, heightOffset);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = zone == DisplayZone.Any ? new Color(0.1f, 0.7f, 0.58f, 0.8f) : new Color(0.95f, 0.57f, 0f, 0.8f);
            Gizmos.matrix = transform.localToWorldMatrix;
            if (IsHook)
            {
                Gizmos.DrawWireCube(new Vector3(0f, -0.2f, 0f), new Vector3(0.4f, 0.4f, 0.05f));
            }
            else
            {
                Gizmos.DrawWireCube(new Vector3(0f, 0.05f, 0f), new Vector3(0.2f, 0.1f, 0.2f));
            }
        }
    }
}
