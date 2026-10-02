using System.Collections.Generic;
using MultiTravel.Core.Products;
using MultiTravel.Gameplay.Common;
using UnityEngine;

namespace MultiTravel.Gameplay.Items
{
    /// <summary>
    /// Deterministic, zone-strict assignment of active items to <see cref="SpawnSlot"/>s (ARCHITECTURE.md §2.8,
    /// OVERHAUL_PLAN §3/§5).
    /// <para>
    /// Algorithm: items keep their input order and slots their list order; when shuffling is on, both orders are permuted
    /// with a Fisher–Yates shuffle driven by <c>System.Random(seed)</c> (same seed → same layout), so items only shuffle
    /// among the slots of their own zone. The zone of an item is <see cref="PresentationRules.EffectiveZone"/>.
    /// Pass 1: every item with a named zone takes a free slot of that zone (a slot that prefers the item's category first).
    /// Pass 2: named-zone items still without a slot take a free <see cref="DisplayZone.Any"/> slot (one warning per call);
    /// they never take a slot of a different named zone.
    /// Pass 3: items whose zone is <see cref="DisplayZone.Any"/> take a free Any slot, then any free slot.
    /// Items left without a slot are lined up above the layout transform and an error is logged.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpawnSlotLayout : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Spawn slots in priority order. When empty, SpawnSlot children of this object are collected in Awake.")]
        private List<SpawnSlot> slots = new List<SpawnSlot>();

        [SerializeField]
        [Tooltip("Spacing of the overflow line used when there are more items than slots.")]
        [Min(0.05f)]
        private float overflowSpacing = 0.3f;

        private readonly List<SpawnSlot> slotOrder = new List<SpawnSlot>();
        private readonly List<ProductItem> itemOrder = new List<ProductItem>();
        private readonly List<SpawnSlot> assignedSlots = new List<SpawnSlot>();
        private readonly HashSet<SpawnSlot> usedSlots = new HashSet<SpawnSlot>();

        /// <summary>Configured slots.</summary>
        public IReadOnlyList<SpawnSlot> Slots => slots;

        /// <summary>Slot assigned to each item by the last <see cref="Assign"/> call (same order as <see cref="LastItemOrder"/>; null for overflow).</summary>
        public IReadOnlyList<SpawnSlot> LastAssignedSlots => assignedSlots;

        /// <summary>Items in the order used by the last <see cref="Assign"/> call.</summary>
        public IReadOnlyList<ProductItem> LastItemOrder => itemOrder;

        /// <summary>Number of named-zone items that had to use an Any slot in the last <see cref="Assign"/> call.</summary>
        public int LastFallbackCount { get; private set; }

        /// <summary>Number of items without any slot in the last <see cref="Assign"/> call.</summary>
        public int LastOverflowCount { get; private set; }

        /// <summary>Generator / test API: replaces the slot list.</summary>
        public void SetSlots(IList<SpawnSlot> newSlots)
        {
            slots.Clear();
            if (newSlots == null)
            {
                return;
            }

            for (int i = 0; i < newSlots.Count; i++)
            {
                if (newSlots[i] != null)
                {
                    slots.Add(newSlots[i]);
                }
            }
        }

        /// <summary>Number of configured slots per zone (for <see cref="ZoneCapacityValidator"/>).</summary>
        public Dictionary<DisplayZone, int> CountSlotsPerZone()
        {
            CollectChildSlotsIfEmpty();
            var result = new Dictionary<DisplayZone, int>();
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] == null)
                {
                    continue;
                }

                result.TryGetValue(slots[i].Zone, out int count);
                result[slots[i].Zone] = count + 1;
            }

            return result;
        }

        /// <summary>
        /// Assigns a home slot and pose to every item and moves Free items there instantly. Returns the number of items that
        /// got a slot.
        /// </summary>
        public int Assign(IReadOnlyList<ProductItem> items, bool shuffle, int seed)
        {
            CollectChildSlotsIfEmpty();
            itemOrder.Clear();
            slotOrder.Clear();
            assignedSlots.Clear();
            usedSlots.Clear();
            LastFallbackCount = 0;
            LastOverflowCount = 0;

            if (items == null || items.Count == 0)
            {
                return 0;
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null)
                {
                    itemOrder.Add(items[i]);
                }
            }

            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null)
                {
                    slotOrder.Add(slots[i]);
                }
            }

            if (shuffle)
            {
                var random = new System.Random(seed);
                Shuffle(itemOrder, random);
                Shuffle(slotOrder, random);
            }

            var zones = new DisplayZone[itemOrder.Count];
            for (int i = 0; i < itemOrder.Count; i++)
            {
                assignedSlots.Add(null);
                zones[i] = PresentationRules.EffectiveZone(itemOrder[i].Definition);
            }

            // Pass 1: own named zone. Biggest items first so they get the roomy slots; each takes the smallest slot of its
            // zone that fits (category preference first), and any slot of the zone when none fits.
            var bySize = new List<int>(itemOrder.Count);
            for (int i = 0; i < itemOrder.Count; i++)
            {
                bySize.Add(i);
            }

            bySize.Sort((a, b) =>
            {
                int t = itemOrder[b].NeedsTallSlot.CompareTo(itemOrder[a].NeedsTallSlot);
                if (t != 0)
                {
                    return t;
                }

                int c = ((int)itemOrder[b].RequiredSlotSize).CompareTo((int)itemOrder[a].RequiredSlotSize);
                return c != 0 ? c : a.CompareTo(b);
            });

            for (int n = 0; n < bySize.Count; n++)
            {
                int i = bySize[n];
                var zone = zones[i];
                if (zone == DisplayZone.Any)
                {
                    continue;
                }

                var definition = itemOrder[i].Definition;
                int need = (int)itemOrder[i].RequiredSlotSize;
                bool tall = itemOrder[i].NeedsTallSlot;
                SpawnSlot taken = null;
                // Smallest class that fits; items that are not tall keep the open top-row slots free for the tall ones.
                for (int pass = 0; pass < 2 && taken == null; pass++)
                {
                    bool allowTall = tall || pass == 1;
                    for (int fit = need; fit <= (int)SlotSize.Wide && taken == null; fit++)
                    {
                        int f = fit;
                        taken = TakeSlot(s => s.Zone == zone && (int)s.Size == f && (tall ? s.AcceptsTall : (allowTall || !s.AcceptsTall)) &&
                                              definition != null && s.Prefers(definition.Category))
                                ?? TakeSlot(s => s.Zone == zone && (int)s.Size == f && (tall ? s.AcceptsTall : (allowTall || !s.AcceptsTall)));
                    }
                }

                assignedSlots[i] = taken ?? TakeSlot(s => s.Zone == zone);
            }

            // Pass 2: named-zone items fall back to Any slots only (never a different named zone).
            List<string> fallbackIds = null;
            for (int i = 0; i < itemOrder.Count; i++)
            {
                if (assignedSlots[i] != null || zones[i] == DisplayZone.Any)
                {
                    continue;
                }

                assignedSlots[i] = TakeSlot(s => s.Zone == DisplayZone.Any);
                if (assignedSlots[i] != null)
                {
                    LastFallbackCount++;
                    (fallbackIds ??= new List<string>()).Add(itemOrder[i].ProductId + " (" + zones[i] + ")");
                }
            }

            // Pass 3: items without a zone: Any slots first, then whatever is left.
            for (int i = 0; i < itemOrder.Count; i++)
            {
                if (assignedSlots[i] != null || zones[i] != DisplayZone.Any)
                {
                    continue;
                }

                assignedSlots[i] = TakeSlot(s => s.Zone == DisplayZone.Any) ?? TakeSlot(s => true);
            }

            if (fallbackIds != null)
            {
                Debug.LogWarning(
                    ServiceResolver.LogPrefix + "SpawnSlotLayout: " + fallbackIds.Count + " item(s) had no free slot in their display zone " +
                    "and use an 'Any' slot: " + string.Join(", ", fallbackIds) + ". Add slots to those zones.",
                    this);
            }

            int assigned = 0;
            int overflow = 0;
            for (int i = 0; i < itemOrder.Count; i++)
            {
                var item = itemOrder[i];
                var slot = assignedSlots[i];
                Pose pose;
                if (slot != null)
                {
                    pose = slot.PoseFor(item);
                    assigned++;
                }
                else
                {
                    pose = new Pose(transform.position + transform.up * 0.5f + transform.right * (overflow * overflowSpacing), transform.rotation);
                    overflow++;
                }

                item.SetHomeSlot(slot);
                item.SetSpawnPose(pose);
                if (item.State == ProductItemState.Free)
                {
                    item.ReturnToSpawn(true);
                }
            }

            LastOverflowCount = overflow;
            if (overflow > 0)
            {
                Debug.LogError(
                    ServiceResolver.LogPrefix + "SpawnSlotLayout: " + overflow + " item(s) had no spawn slot (" + slotOrder.Count +
                    " slots for " + itemOrder.Count + " items, zone-strict). Add SpawnSlots in the scene generator.",
                    this);
            }

            return assigned;
        }

        /// <summary>Slot assigned to <paramref name="item"/> by the last <see cref="Assign"/> call, or null.</summary>
        public SpawnSlot SlotOf(ProductItem item)
        {
            int index = itemOrder.IndexOf(item);
            return index >= 0 ? assignedSlots[index] : null;
        }

        private SpawnSlot TakeSlot(System.Predicate<SpawnSlot> match)
        {
            for (int i = 0; i < slotOrder.Count; i++)
            {
                var slot = slotOrder[i];
                if (!usedSlots.Contains(slot) && match(slot))
                {
                    usedSlots.Add(slot);
                    return slot;
                }
            }

            return null;
        }

        private static void Shuffle<T>(List<T> list, System.Random random)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                var tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        private void Awake()
        {
            CollectChildSlotsIfEmpty();
        }

        private void CollectChildSlotsIfEmpty()
        {
            if (slots.Count > 0)
            {
                return;
            }

            GetComponentsInChildren(true, slots);
        }
    }
}
