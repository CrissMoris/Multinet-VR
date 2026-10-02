using System;
using UnityEngine;

namespace MultiTravel.Core.Products
{
    /// <summary>Where a product is displayed in the dressing room before it is picked up.</summary>
    public enum DisplayZone
    {
        /// <summary>No preference: any free slot (fallback only).</summary>
        Any = 0,
        /// <summary>Hanging rail with hangers (shirts, blouses, jackets, dresses, swimwear on clip hangers).</summary>
        Hanging = 1,
        /// <summary>Folded-clothes shelves.</summary>
        Folded = 2,
        /// <summary>Shoe rack on the inside of the left wardrobe door.</summary>
        Shoes = 3,
        /// <summary>Accessory drawer / hat peg on the right wardrobe door.</summary>
        Accessories = 4,
        /// <summary>Jewellery valet tray on the right wardrobe door.</summary>
        Jewellery = 5,
        /// <summary>Business console table (front left).</summary>
        Business = 6,
        /// <summary>Leisure / holiday table and basket (front right).</summary>
        Leisure = 7
    }

    /// <summary>How a product settles inside the suitcase. Visual only — scoring never depends on it.</summary>
    public enum PackedKind
    {
        /// <summary>Laid flat and stacked (folded clothes, towel, books).</summary>
        Flat = 0,
        /// <summary>Shoes: heel-to-toe in a base corner.</summary>
        ShoeCorner = 1,
        /// <summary>Standing upright (toiletry bag, bags).</summary>
        Upright = 2,
        /// <summary>Into the mesh pocket of the lid (documents, flat electronics).</summary>
        LidPocket = 3,
        /// <summary>Into the front organiser tray (small items, jewellery, cables, pen).</summary>
        Organiser = 4,
        /// <summary>Dropped on top of the pile (bulky / odd items).</summary>
        Top = 5
    }

    /// <summary>Grip preset used when the item is grabbed.</summary>
    public enum GripPreset
    {
        /// <summary>Dynamic attach at the touched point (small objects).</summary>
        Dynamic = 0,
        /// <summary>Folded garment: held at the top edge, kept level.</summary>
        FoldedGarment = 1,
        /// <summary>Hanging garment: held at the hanger hook, hangs straight down.</summary>
        Hanger = 2,
        /// <summary>Shoes: held at the heel.</summary>
        Shoe = 3,
        /// <summary>Flat rigid object (laptop, book, notebook): held at the nearest edge.</summary>
        FlatEdge = 4,
        /// <summary>Handle (bags, suitcase-like items): held at the handle.</summary>
        Handle = 5
    }

    /// <summary>
    /// Presentation data of a product: where it is displayed, how it is held and how it packs. Data-only and optional;
    /// the gameplay layer falls back to category-based defaults when values are left at <c>Any</c>/defaults.
    /// </summary>
    [Serializable]
    public sealed class ProductPresentation
    {
        [Tooltip("Display zone in the dressing room.")]
        public DisplayZone Zone = DisplayZone.Any;

        [Tooltip("How the item settles in the suitcase (visual only).")]
        public PackedKind Packed = PackedKind.Flat;

        [Tooltip("Grip preset used when grabbed.")]
        public GripPreset Grip = GripPreset.Dynamic;

        [Tooltip("True when the prefab has a 'Hanging' display visual and a 'Folded' packed visual.")]
        public bool HasHangingVariant;

        /// <summary>Category-based default used when no explicit presentation is configured.</summary>
        public static DisplayZone DefaultZoneFor(ProductCategory category)
        {
            switch (category)
            {
                case ProductCategory.Clothing: return DisplayZone.Folded;
                case ProductCategory.Shoes: return DisplayZone.Shoes;
                case ProductCategory.Accessory: return DisplayZone.Accessories;
                case ProductCategory.Jewellery: return DisplayZone.Jewellery;
                case ProductCategory.Business:
                case ProductCategory.Electronics:
                case ProductCategory.Document:
                    return DisplayZone.Business;
                case ProductCategory.Toiletry: return DisplayZone.Accessories;
                case ProductCategory.Leisure: return DisplayZone.Leisure;
                default: return DisplayZone.Any;
            }
        }
    }
}
