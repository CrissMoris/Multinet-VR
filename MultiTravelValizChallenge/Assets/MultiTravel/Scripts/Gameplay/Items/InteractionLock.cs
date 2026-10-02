using MultiTravel.Gameplay.Common;
using UnityEngine;

namespace MultiTravel.Gameplay.Items
{
    /// <summary>
    /// Enables grabbing only while the session is <c>Playing</c> (ARCHITECTURE.md §2.8). Locking disables every
    /// item's <c>XRGrabInteractable</c>; items held at that moment are force-released and returned to their spawn slot.
    /// Placed items stay in the suitcase. Starts locked.
    /// <para>
    /// The tutorial practice item can be whitelisted with <see cref="AllowPractice"/>: it stays grabbable while everything
    /// else is locked (Instructions), until <see cref="ClearPractice"/>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteractionLock : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Pool whose items are locked / unlocked.")]
        private ItemPool itemPool;

        [SerializeField]
        [Tooltip("Animate held items back to their spawn slot (false = teleport).")]
        private bool animateReturn = true;

        private bool locked = true;
        private bool applied;
        private ProductItem practiceItem;

        /// <summary>True while grabbing is disabled.</summary>
        public bool IsLocked => locked;

        /// <summary>The whitelisted practice item (null when none).</summary>
        public ProductItem PracticeItem => practiceItem;

        /// <summary>Generator / test API.</summary>
        public void SetPool(ItemPool pool)
        {
            itemPool = pool;
        }

        /// <summary>Locks (true) or unlocks (false) every pooled item. Idempotent; re-applies to newly created items.</summary>
        public void SetLocked(bool value)
        {
            locked = value;
            applied = true;
            if (itemPool == null)
            {
                Debug.LogError(ServiceResolver.LogPrefix + "InteractionLock: no ItemPool assigned.", this);
                return;
            }

            itemPool.EnsureCreated();
            var items = itemPool.AllItems;
            for (int i = 0; i < items.Count; i++)
            {
                Apply(items[i]);
            }

            if (practiceItem != null && !Contains(items, practiceItem))
            {
                Apply(practiceItem);
            }
        }

        /// <summary>Shorthand for <c>SetLocked(true)</c>.</summary>
        public void Lock()
        {
            SetLocked(true);
        }

        /// <summary>Shorthand for <c>SetLocked(false)</c>.</summary>
        public void Unlock()
        {
            SetLocked(false);
        }

        /// <summary>Whitelists <paramref name="item"/>: it stays grabbable while the lock is on. Replaces a previous practice item.</summary>
        public void AllowPractice(ProductItem item)
        {
            if (practiceItem == item)
            {
                Apply(item);
                return;
            }

            ClearPractice();
            practiceItem = item;
            Apply(item);
        }

        /// <summary>Removes the whitelist; while locked the former practice item is disabled (and returned home when held).</summary>
        public void ClearPractice()
        {
            var item = practiceItem;
            practiceItem = null;
            Apply(item);
        }

        private void Apply(ProductItem item)
        {
            if (item == null)
            {
                return;
            }

            bool enable = !locked || item == practiceItem;
            if (!enable)
            {
                bool wasHeld = item.SetInteractionEnabled(false);
                if (wasHeld && item.State == ProductItemState.Free)
                {
                    item.ReturnToSpawn(!animateReturn);
                }
            }
            else
            {
                item.SetInteractionEnabled(true);
            }
        }

        private static bool Contains(System.Collections.Generic.IReadOnlyList<ProductItem> items, ProductItem item)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] == item)
                {
                    return true;
                }
            }

            return false;
        }

        private void Start()
        {
            if (!applied && itemPool != null)
            {
                SetLocked(locked);
            }
        }
    }
}
