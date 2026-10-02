using System;
using MultiTravel.Gameplay.Common;
using UnityEngine;

namespace MultiTravel.Gameplay.Items
{
    /// <summary>
    /// Keeps every item reachable (ARCHITECTURE.md §2.8). Every <see cref="tickSeconds"/> it checks the Free items and
    /// returns an item to its spawn slot when it fell below <c>floorY - belowFloorMargin</c>, left <see cref="PlayBounds"/>,
    /// or has been resting on the floor for at least <see cref="floorRestSeconds"/>. Nothing is destroyed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ItemRecoveryService : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Pool whose items are watched.")]
        private ItemPool itemPool;

        [SerializeField]
        [Tooltip("World Y of the room floor.")]
        private float floorY;

        [SerializeField]
        [Tooltip("An item whose anchor is lower than floorY minus this margin is recovered immediately.")]
        [Min(0f)]
        private float belowFloorMargin = 0.2f;

        [SerializeField]
        [Tooltip("World-space play area. Items whose anchor leaves it are recovered.")]
        private Bounds playBounds = new Bounds(new Vector3(0f, 1.5f, 0f), new Vector3(10f, 5f, 10f));

        [SerializeField]
        [Tooltip("Physics layers of the floor. When set, 'on the floor' is a short downward raycast against these layers; " +
                 "when empty, an item counts as on the floor when its lowest point is within floorContactTolerance of floorY.")]
        private LayerMask floorLayers;

        [SerializeField]
        [Tooltip("Height tolerance / raycast slack used for the on-floor test (metres).")]
        [Min(0.001f)]
        private float floorContactTolerance = 0.05f;

        [SerializeField]
        [Tooltip("Seconds an item may rest on the floor before it is returned to its spawn slot.")]
        [Min(0f)]
        private float floorRestSeconds = 3f;

        [SerializeField]
        [Tooltip("Seconds between checks.")]
        [Min(0.05f)]
        private float tickSeconds = 0.5f;

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

        private float[] floorRestTimers = new float[0];
        private float nextTickTime;

        /// <summary>Raised after an item was sent back to its spawn slot.</summary>
        public event Action<ProductItem> ItemRecovered;

        /// <summary>World-space play area.</summary>
        public Bounds PlayBounds => playBounds;

        /// <summary>World Y of the floor.</summary>
        public float FloorY => floorY;

        /// <summary>Generator / test API.</summary>
        public void SetPool(ItemPool pool)
        {
            itemPool = pool;
        }

        /// <summary>Generator / test API: floor height and play area.</summary>
        public void Configure(float floorHeight, Bounds bounds)
        {
            floorY = floorHeight;
            playBounds = bounds;
        }

        /// <summary>Generator API: floor layers for the on-floor raycast (empty = height test).</summary>
        public void SetFloorLayers(LayerMask layers)
        {
            floorLayers = layers;
        }

        /// <summary>Runs one check immediately (also used by tests). Returns the number of recovered items.</summary>
        public int Tick(float elapsedSinceLastTick)
        {
            if (itemPool == null)
            {
                return 0;
            }

            var items = itemPool.AllItems;
            if (floorRestTimers.Length < items.Count)
            {
                Array.Resize(ref floorRestTimers, items.Count);
            }

            int recovered = 0;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null || item.State != ProductItemState.Free || item.IsReturning)
                {
                    floorRestTimers[i] = 0f;
                    continue;
                }

                if (NeedsRecovery(item, i, elapsedSinceLastTick))
                {
                    floorRestTimers[i] = 0f;
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

            if (floorRestSeconds <= 0f)
            {
                return false;
            }

            if (IsOnFloor(item) && item.IsAtRest(restLinearSpeed, restAngularSpeed))
            {
                floorRestTimers[index] += elapsed;
                return floorRestTimers[index] >= floorRestSeconds;
            }

            floorRestTimers[index] = 0f;
            return false;
        }

        private bool IsOnFloor(ProductItem item)
        {
            var transformOfItem = item.transform;
            var bounds = item.LocalBounds;
            var bottomLocal = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

            if (floorLayers.value != 0)
            {
                var origin = transformOfItem.TransformPoint(bounds.center);
                float distance = Vector3.Distance(origin, transformOfItem.TransformPoint(bottomLocal)) + floorContactTolerance;
                return Physics.Raycast(origin, Vector3.down, distance, floorLayers, QueryTriggerInteraction.Ignore);
            }

            float lowest = LowestWorldY(transformOfItem, bounds);
            return lowest <= floorY + floorContactTolerance;
        }

        private static float LowestWorldY(Transform itemTransform, Bounds localBounds)
        {
            var min = localBounds.min;
            var max = localBounds.max;
            float lowest = float.MaxValue;
            for (int corner = 0; corner < 8; corner++)
            {
                var local = new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z);
                float y = itemTransform.TransformPoint(local).y;
                if (y < lowest)
                {
                    lowest = y;
                }
            }

            return lowest;
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
