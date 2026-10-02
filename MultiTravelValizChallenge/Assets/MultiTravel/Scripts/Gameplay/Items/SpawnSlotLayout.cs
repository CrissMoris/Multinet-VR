using System.Collections.Generic;
using MultiTravel.Gameplay.Common;
using UnityEngine;

namespace MultiTravel.Gameplay.Items
{
    /// <summary>
    /// Deterministic assignment of active items to <see cref="SpawnSlot"/>s (ARCHITECTURE.md §2.8).
    /// <para>
    /// Algorithm: items keep their input order and slots their list order; when shuffling is on, both orders are
    /// permuted with a Fisher–Yates shuffle driven by <c>System.Random(seed)</c> (same seed → same layout).
    /// Pass 1 gives every item the first free slot that prefers its category, pass 2 the first free slot without a
    /// preference, pass 3 any free slot. Items left without a slot (more items than slots) are lined up above the
    /// layout transform and an error is logged.
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

        /// <summary>
        /// Assigns a spawn pose to every item and moves Free items there instantly. Returns the number of items that got a slot.
        /// </summary>
        public int Assign(IReadOnlyList<ProductItem> items, bool shuffle, int seed)
        {
            CollectChildSlotsIfEmpty();
            itemOrder.Clear();
            slotOrder.Clear();
            assignedSlots.Clear();
            usedSlots.Clear();

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

            for (int i = 0; i < itemOrder.Count; i++)
            {
                assignedSlots.Add(null);
            }

            // Pass 1: preferred category.
            for (int i = 0; i < itemOrder.Count; i++)
            {
                var definition = itemOrder[i].Definition;
                if (definition == null)
                {
                    continue;
                }

                assignedSlots[i] = TakeSlot(s => s.Prefers(definition.Category));
            }

            // Pass 2: slots without a preference. Pass 3: any free slot.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < itemOrder.Count; i++)
                {
                    if (assignedSlots[i] != null)
                    {
                        continue;
                    }

                    assignedSlots[i] = pass == 0 ? TakeSlot(s => s.AcceptsAnyCategory) : TakeSlot(s => true);
                }
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

                item.SetSpawnPose(pose);
                if (item.State == ProductItemState.Free)
                {
                    item.ReturnToSpawn(true);
                }
            }

            if (overflow > 0)
            {
                Debug.LogError(
                    ServiceResolver.LogPrefix + "SpawnSlotLayout: " + overflow + " item(s) had no spawn slot (" + slotOrder.Count +
                    " slots for " + itemOrder.Count + " items). Add SpawnSlots in the scene generator.",
                    this);
            }

            return assigned;
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
