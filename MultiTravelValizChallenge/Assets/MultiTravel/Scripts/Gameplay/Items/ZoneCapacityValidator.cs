using System;
using System.Collections.Generic;
using MultiTravel.Core.Products;
using MultiTravel.Core.Session;
using MultiTravel.Gameplay.Common;

namespace MultiTravel.Gameplay.Items
{
    /// <summary>
    /// Pure check of the display-zone mapping against the number of spawn slots per zone (OVERHAUL_PLAN §3: "each zone has
    /// ≥ 20 % spare slots"). Used by the EditMode tests (planned slot counts) and by the scene builder / validator with the
    /// real slot counts (<see cref="SpawnSlotLayout.CountSlotsPerZone"/>).
    /// </summary>
    public static class ZoneCapacityValidator
    {
        /// <summary>Maximum fraction of a zone's slots that the items of one gender may fill.</summary>
        public const float DefaultMaxFill = 0.8f;

        /// <summary>Zone and availability of one product (lets callers validate data that is not a ProductDefinition).</summary>
        public readonly struct Entry
        {
            public Entry(string id, DisplayZone zone, GenderAvailability availability)
            {
                Id = id;
                Zone = zone;
                Availability = availability;
            }

            public string Id { get; }

            public DisplayZone Zone { get; }

            public GenderAvailability Availability { get; }
        }

        /// <summary>Entries for product definitions using their effective zone (<see cref="PresentationRules.EffectiveZone"/>).</summary>
        public static List<Entry> EntriesFor(IEnumerable<ProductDefinition> products)
        {
            var entries = new List<Entry>();
            if (products == null)
            {
                return entries;
            }

            foreach (var product in products)
            {
                if (product != null)
                {
                    entries.Add(new Entry(product.Id, PresentationRules.EffectiveZone(product), product.Availability));
                }
            }

            return entries;
        }

        /// <summary>Number of items per zone that one gender sees.</summary>
        public static Dictionary<DisplayZone, int> CountItemsPerZone(IEnumerable<Entry> entries, Gender gender)
        {
            var result = new Dictionary<DisplayZone, int>();
            if (entries == null)
            {
                return result;
            }

            foreach (var entry in entries)
            {
                if (!entry.Availability.Includes(gender))
                {
                    continue;
                }

                result.TryGetValue(entry.Zone, out int count);
                result[entry.Zone] = count + 1;
            }

            return result;
        }

        /// <summary>Smallest slot count that keeps <paramref name="itemCount"/> items at or below <paramref name="maxFill"/>.</summary>
        public static int RequiredSlots(int itemCount, float maxFill = DefaultMaxFill)
        {
            if (itemCount <= 0)
            {
                return 0;
            }

            if (maxFill <= 0f || maxFill > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(maxFill));
            }

            return (int)Math.Ceiling(itemCount / (double)maxFill - 1e-9);
        }

        /// <summary>Minimum slot count per zone so both genders fit with the spare-slot rule.</summary>
        public static Dictionary<DisplayZone, int> MinimumSlotCounts(IEnumerable<Entry> entries, float maxFill = DefaultMaxFill)
        {
            var list = entries as IList<Entry> ?? new List<Entry>(entries ?? Array.Empty<Entry>());
            var result = new Dictionary<DisplayZone, int>();
            foreach (Gender gender in Enum.GetValues(typeof(Gender)))
            {
                foreach (var pair in CountItemsPerZone(list, gender))
                {
                    int required = RequiredSlots(pair.Value, maxFill);
                    result.TryGetValue(pair.Key, out int existing);
                    result[pair.Key] = Math.Max(existing, required);
                }
            }

            return result;
        }

        /// <summary>Appends an error for every product whose zone is <see cref="DisplayZone.Any"/>. Returns true when none.</summary>
        public static bool ValidateZonesAssigned(IEnumerable<Entry> entries, List<string> errors)
        {
            if (errors == null)
            {
                throw new ArgumentNullException(nameof(errors));
            }

            int before = errors.Count;
            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    if (entry.Zone == DisplayZone.Any)
                    {
                        errors.Add($"{entry.Id}: display zone is 'Any'; set ProductDefinition.Presentation.Zone.");
                    }
                }
            }

            return errors.Count == before;
        }

        /// <summary>
        /// For each gender and each zone used by that gender's items: the zone must have slots and
        /// <c>items ≤ maxFill × slots</c>. Items in <see cref="DisplayZone.Any"/> are checked against the Any slots.
        /// Appends developer-facing messages to <paramref name="errors"/>; returns true when nothing was added.
        /// </summary>
        public static bool Validate(IEnumerable<Entry> entries, IReadOnlyDictionary<DisplayZone, int> slotCounts, List<string> errors, float maxFill = DefaultMaxFill)
        {
            if (errors == null)
            {
                throw new ArgumentNullException(nameof(errors));
            }

            if (slotCounts == null)
            {
                throw new ArgumentNullException(nameof(slotCounts));
            }

            int before = errors.Count;
            var list = entries as IList<Entry> ?? new List<Entry>(entries ?? Array.Empty<Entry>());
            foreach (Gender gender in Enum.GetValues(typeof(Gender)))
            {
                foreach (var pair in CountItemsPerZone(list, gender))
                {
                    slotCounts.TryGetValue(pair.Key, out int slots);
                    if (slots <= 0)
                    {
                        errors.Add($"{gender}: zone {pair.Key} has {pair.Value} item(s) but no spawn slot.");
                        continue;
                    }

                    if (pair.Value > maxFill * slots + 1e-4f)
                    {
                        errors.Add($"{gender}: zone {pair.Key} has {pair.Value} item(s) for {slots} slot(s); at most {maxFill:P0} may be used " +
                                   $"(needs ≥ {RequiredSlots(pair.Value, maxFill)} slots).");
                    }
                }
            }

            return errors.Count == before;
        }

        /// <summary><see cref="Validate(IEnumerable{Entry}, IReadOnlyDictionary{DisplayZone, int}, List{string}, float)"/> for product definitions.</summary>
        public static bool Validate(IEnumerable<ProductDefinition> products, IReadOnlyDictionary<DisplayZone, int> slotCounts, List<string> errors, float maxFill = DefaultMaxFill)
        {
            return Validate(EntriesFor(products), slotCounts, errors, maxFill);
        }
    }
}
