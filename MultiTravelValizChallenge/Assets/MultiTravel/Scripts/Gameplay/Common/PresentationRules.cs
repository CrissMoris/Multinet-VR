using MultiTravel.Core.Products;

namespace MultiTravel.Gameplay.Common
{
    /// <summary>
    /// Effective presentation values of a product. <see cref="ProductPresentation"/> is optional data: a zone left at
    /// <see cref="DisplayZone.Any"/> falls back to the category default, a missing definition behaves like a small loose item.
    /// </summary>
    public static class PresentationRules
    {
        /// <summary>The display zone used for spawn assignment: explicit zone, else the category default, else Any.</summary>
        public static DisplayZone EffectiveZone(ProductDefinition definition)
        {
            if (definition == null)
            {
                return DisplayZone.Any;
            }

            var presentation = definition.Presentation;
            if (presentation != null && presentation.Zone != DisplayZone.Any)
            {
                return presentation.Zone;
            }

            return ProductPresentation.DefaultZoneFor(definition.Category);
        }

        /// <summary>How the item packs into the suitcase (Flat when no data).</summary>
        public static PackedKind EffectivePacked(ProductDefinition definition)
        {
            return definition != null && definition.Presentation != null ? definition.Presentation.Packed : PackedKind.Flat;
        }

        /// <summary>Grip preset (Dynamic when no data).</summary>
        public static GripPreset EffectiveGrip(ProductDefinition definition)
        {
            return definition != null && definition.Presentation != null ? definition.Presentation.Grip : GripPreset.Dynamic;
        }

        /// <summary>True when the product has a hanging display visual and a folded packed visual.</summary>
        public static bool HasHangingVariant(ProductDefinition definition)
        {
            return definition != null && definition.Presentation != null && definition.Presentation.HasHangingVariant;
        }

        /// <summary>Lower-case zone name used by art node names (<c>SLOT.&lt;zone&gt;.&lt;nn&gt;</c>).</summary>
        public static string ZoneNodeName(DisplayZone zone)
        {
            return zone.ToString().ToLowerInvariant();
        }

        /// <summary>Lower-case packed-kind name used by art node names (<c>PACK.&lt;kind&gt;.&lt;nn&gt;</c>).</summary>
        public static string PackedNodeName(PackedKind kind)
        {
            return kind.ToString().ToLowerInvariant();
        }

        /// <summary>Parses a lower-case zone node name (case-insensitive). Returns false for unknown names.</summary>
        public static bool TryParseZone(string value, out DisplayZone zone)
        {
            zone = DisplayZone.Any;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            foreach (DisplayZone candidate in System.Enum.GetValues(typeof(DisplayZone)))
            {
                if (string.Equals(candidate.ToString(), value, System.StringComparison.OrdinalIgnoreCase))
                {
                    zone = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Parses a lower-case packed-kind node name (case-insensitive). Returns false for unknown names.</summary>
        public static bool TryParsePacked(string value, out PackedKind kind)
        {
            kind = PackedKind.Flat;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            foreach (PackedKind candidate in System.Enum.GetValues(typeof(PackedKind)))
            {
                if (string.Equals(candidate.ToString(), value, System.StringComparison.OrdinalIgnoreCase))
                {
                    kind = candidate;
                    return true;
                }
            }

            return false;
        }
    }
}
