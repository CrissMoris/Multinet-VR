using System;
using MultiTravel.Gameplay.Common;
using MultiTravel.Gameplay.Suitcase;
using UnityEngine;

namespace MultiTravel.Gameplay.Items
{
    /// <summary>
    /// Keeps every item reachable (ARCHITECTURE.md §2.8, OVERHAUL_PLAN §5). Every <see cref="tickSeconds"/> it checks the
    /// Free items and returns an item to its home slot (short shrink / grow) when
    /// <list type="bullet">
    /// <item>its anchor fell below <c>floorY - belowFloorMargin</c> or left <see cref="PlayBounds"/> (immediately), or</item>
    /// <item>it has been resting for <see cref="RestSeconds"/> anywhere that is not its home slot and not inside the
    /// suitcase placement volume (floor, furniture tops, other zones).</item>
    /// </list>
    /// Docked items (resting at home) are only checked against the floor / play bounds; items resting inside the suitcase
    /// placement volume are never returned. Nothing is destroyed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ItemRecoveryService : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Pool whose items are watched.")]
        private ItemPool itemPool;

        [SerializeField]
        [Tooltip("Suitcase whose placement volume counts as a valid resting place. Found in the scene when empty.")]
        private SuitcaseController suitcase;

        [SerializeField]
        [Tooltip("World Y of the room floor.")]
        private float floorY;

        [SerializeField]
        [Tooltip("An item whose anchor is lower than floorY minus this margin is recovered immediately.")]
        [Min(0f)]
        private float belowFloorMargin = 0.2f;

        [SerializeField]
        [Tooltip("World-space play area. Items whose anchor leaves it are recovered immediately.")]
        private Bounds playBounds = new Bounds(new Vector3(0f, 1.5f, 0f), new Vector3(10f, 5f, 10f));

        [SerializeField]
        [Tooltip("Seconds an item may rest away from its home slot (and outside the suitcase) before it is returned.")]
        [Min(0f)]
        private float restSeconds = 1.2f;

        [SerializeField]
        [Tooltip("An item closer than this to its home pose counts as at home (metres).")]
        [Min(0f)]
        private float homeTolerance = 0.05f;

        [SerializeField]
        [Tooltip("Seconds between checks.")]
        [Min(0.05f)]
        private float tickSeconds = 0.2f;

        [SerializeField]
        [Tooltip("Speed below which an item counts as resting (m/s).")]
        [Min(0f)]
        private float restLinearSpeed = 0.05f;

        [SerializeField]
        [Tooltip("Angular speed below which an item counts as resting (rad/s).")]
        [Min(0f)]
        private float restAngularSpeed = 0.5f;

        [SerializeField]
        [Tooltip("Animate the return (shrink / grow) instead of teleporting.")]
        private bool animateReturn = true;

        private float[] restTimers = new float[0];
        private float nextTickTime;
        private bool suitcaseSearched;

        /// <summary>Raised after an item was sent back to its spawn slot.</summary>
        public event Action<ProductItem> ItemRecovered;

        /// <summary>World-space play area.</summary>
        public Bounds PlayBounds => playBounds;

        /// <summary>World Y of the floor.</summary>
        public float FloorY => floorY;

        /// <summary>Seconds an item may rest away from home before it is returned.</summary>
        public float RestSeconds => restSeconds;

        /// <summary>Generator / test API.</summary>
        public void SetPool(ItemPool pool)
        {
            itemPool = pool;
        }

        /// <summary>Generator / test API: suitcase whose placement volume is a valid resting place.</summary>
        public void SetSuitcase(SuitcaseController target)
        {
            suitcase = target;
            suitcaseSearched = true;
        }

        /// <summary>Generator / test API: floor height and play area.</summary>
        public void Configure(float floorHeight, Bounds bounds)
        {
            floorY = floorHeight;
            playBounds = bounds;
        }

        /// <summary>Test / tuning API: seconds of rest away from home before the return.</summary>
        public void SetRestSeconds(float seconds)
        {
            restSeconds = Mathf.Max(0f, seconds);
        }

        /// <summary>Runs one check immediately (also used by tests). Returns the number of recovered items.</summary>
        public int Tick(float elapsedSinceLastTick)
        {
            if (itemPool == null)
            {
                return 0;
            }

            ResolveSuitcase();
            var items = itemPool.AllItems;
            if (restTimers.Length < items.Count)
            {
                Array.Resize(ref restTimers, items.Count);
            }

            int recovered = 0;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null || item.State != ProductItemState.Free || item.IsReturning)
                {
                    restTimers[i] = 0f;
                    continue;
                }

                if (NeedsRecovery(item, i, elapsedSinceLastTick))
                {
                    restTimers[i] = 0f;
                    if (item.ReturnToSpawn(!animateReturn))
                    {
                        recovered++;
                        ItemRecovered?.Invoke(item);
                    }
                }
            }

            return recovered;
        }

        private bool NeedsRecovery(ProductItem item, int index, float elapsed)
        {
            var anchor = item.AnchorPosition;
            if (anchor.y < floorY - belowFloorMargin)
            {
                return true;
            }

            if (!playBounds.Contains(anchor))
            {
                return true;
            }

            if (item.IsDocked)
            {
                restTimers[index] = 0f;
                return false;
            }

            bool resting = item.IsAtRest(restLinearSpeed, restAngularSpeed);
            bool atHome = item.DistanceFromHome() <= homeTolerance;
            bool inSuitcase = suitcase != null && suitcase.IsInsideVolume(item);
            if (!resting || atHome || inSuitcase)
            {
                restTimers[index] = 0f;
                return false;
            }

            restTimers[index] += elapsed;
            return restTimers[index] >= restSeconds - 1e-4f;
        }

        private void ResolveSuitcase()
        {
            if (suitcase != null || suitcaseSearched)
            {
                return;
            }

            suitcaseSearched = true;
            suitcase = FindAnyObjectByType<SuitcaseController>();
        }

        private void Start()
        {
            if (itemPool == null)
            {
                Debug.LogError(ServiceResolver.LogPrefix + "ItemRecoveryService: no ItemPool assigned; items cannot be recovered.", this);
            }

            nextTickTime = Time.time + tickSeconds;
        }

        private void Update()
        {
            if (Time.time < nextTickTime)
            {
                return;
            }

            nextTickTime = Time.time + tickSeconds;
            Tick(tickSeconds);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.1f, 0.7f, 0.58f, 0.6f);
            Gizmos.DrawWireCube(playBounds.center, playBounds.size);
        }
    }
}
