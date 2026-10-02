using MultiTravel.Gameplay.Common;
using UnityEngine;

namespace MultiTravel.Gameplay.Items
{
    /// <summary>
    /// Enables grabbing only while the session is <c>Playing</c> (ARCHITECTURE.md §2.8). Locking disables every
    /// item's <c>XRGrabInteractable</c>; items held at that moment are force-released and returned to their spawn slot.
    /// Placed items stay in the suitcase. Starts locked.
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

        /// <summary>True while grabbing is disabled.</summary>
        public bool IsLocked => locked;

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
                var item = items[i];
                if (item == null)
                {
                    continue;
                }

                if (locked)
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

        private void Start()
        {
            if (!applied && itemPool != null)
            {
                SetLocked(locked);
            }
        }
    }
}
