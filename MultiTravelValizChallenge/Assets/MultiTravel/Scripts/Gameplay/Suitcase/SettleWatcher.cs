using MultiTravel.Gameplay.Common;
using MultiTravel.Gameplay.Items;
using UnityEngine;

namespace MultiTravel.Gameplay.Suitcase
{
    /// <summary>
    /// Places items that were dropped (or fell) into the suitcase without a release inside the volume
    /// (ARCHITECTURE.md §2.8): every <see cref="tickSeconds"/> each unheld Free item whose anchor is inside the
    /// placement volume and whose Rigidbody is asleep or slower than the thresholds is passed to
    /// <see cref="SuitcaseController.TryPlace"/> (practice items too while the suitcase accepts practice placements).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettleWatcher : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Suitcase receiving the settled items.")]
        private SuitcaseController suitcase;

        [SerializeField]
        [Tooltip("Pool whose items are checked. Defaults to the suitcase's pool.")]
        private ItemPool itemPool;

        [SerializeField]
        [Tooltip("Seconds between checks.")]
        [Min(0.02f)]
        private float tickSeconds = 0.2f;

        [SerializeField]
        [Tooltip("Linear speed below which an item counts as resting (m/s).")]
        [Min(0f)]
        private float restLinearSpeed = 0.05f;

        [SerializeField]
        [Tooltip("Angular speed below which an item counts as resting (rad/s).")]
        [Min(0f)]
        private float restAngularSpeed = 0.6f;

        private float nextTickTime;

        /// <summary>Generator / test API.</summary>
        public void Configure(SuitcaseController target, ItemPool pool)
        {
            suitcase = target;
            itemPool = pool;
        }

        /// <summary>Runs one check now. Returns the number of items placed.</summary>
        public int Tick()
        {
            if (suitcase == null || (!suitcase.AcceptPlacements && !suitcase.AcceptPracticePlacements))
            {
                return 0;
            }

            var pool = itemPool != null ? itemPool : suitcase.Pool;
            if (pool == null)
            {
                return 0;
            }

            int placed = 0;
            var items = pool.AllItems;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null || item.IsReturning || !suitcase.CanAccept(item))
                {
                    continue;
                }

                if (!suitcase.IsInsideVolume(item) || !item.IsAtRest(restLinearSpeed, restAngularSpeed))
                {
                    continue;
                }

                if (suitcase.TryPlace(item))
                {
                    placed++;
                }
            }

            return placed;
        }

        private void Start()
        {
            if (suitcase == null)
            {
                suitcase = GetComponent<SuitcaseController>();
            }

            if (suitcase == null)
            {
                Debug.LogError(ServiceResolver.LogPrefix + "SettleWatcher: no SuitcaseController assigned.", this);
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
            Tick();
        }
    }
}
