using System;
using System.Collections.Generic;
using MultiTravel.Core.Products;
using MultiTravel.Gameplay.Audio;
using MultiTravel.Gameplay.Common;

namespace MultiTravel.Gameplay.Feedback
{
    /// <summary>
    /// Product-aware landing foley mapping (OVERHAUL_PLAN §5) on the shared <see cref="SoundKind"/> enum of the audio module.
    /// More specific than the category-only <see cref="SoundKinds.For(ProductCategory)"/>: books and documents are Paper,
    /// handle bags are Leather, the towel / pillow / hat / tie are Cloth. Used by
    /// <c>SuitcaseController.ItemLanded</c>.
    /// </summary>
    public static class ProductSoundKinds
    {
        private static readonly HashSet<string> SoftIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "beach-towel", "neck-pillow", "beach-hat", "tie", "socks"
        };

        private static readonly HashSet<string> PaperIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "notebook", "kids-book", "passport", "id-card"
        };

        private static readonly HashSet<string> LeatherIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "practice-tag"
        };

        /// <summary>
        /// Landing sound of <paramref name="definition"/>: Metal for jewellery, Leather for shoes and handle bags, Paper for
        /// documents and books, Cloth for clothing and soft goods, Hard for everything else (electronics, business items,
        /// hard toys). Null → Hard.
        /// </summary>
        public static SoundKind For(ProductDefinition definition)
        {
            if (definition == null)
            {
                return SoundKind.Hard;
            }

            string id = definition.Id ?? string.Empty;
            var grip = PresentationRules.EffectiveGrip(definition);
            if (definition.Category == ProductCategory.Jewellery)
            {
                return SoundKind.Metal;
            }

            if (definition.Category == ProductCategory.Shoes || grip == GripPreset.Handle || LeatherIds.Contains(id))
            {
                return SoundKind.Leather;
            }

            if (definition.Category == ProductCategory.Document || PaperIds.Contains(id))
            {
                return SoundKind.Paper;
            }

            if (definition.Category == ProductCategory.Clothing || SoftIds.Contains(id) ||
                grip == GripPreset.FoldedGarment || grip == GripPreset.Hanger)
            {
                return SoundKind.Cloth;
            }

            return SoundKind.Hard;
        }

        /// <summary>True for soft goods (Clothing / soft Leisure items): they squash briefly when they land in the suitcase.</summary>
        public static bool IsSoft(ProductDefinition definition)
        {
            if (definition == null || For(definition) != SoundKind.Cloth)
            {
                return false;
            }

            var grip = PresentationRules.EffectiveGrip(definition);
            return definition.Category == ProductCategory.Clothing || definition.Category == ProductCategory.Leisure ||
                   grip == GripPreset.FoldedGarment || grip == GripPreset.Hanger;
        }
    }
}
