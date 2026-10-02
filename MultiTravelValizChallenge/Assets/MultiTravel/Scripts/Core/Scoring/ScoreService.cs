using System;
using System.Collections.Generic;

namespace MultiTravel.Core.Scoring
{
    /// <summary>
    /// Score accumulator with duplicate protection (ARCHITECTURE.md §2.5). Every product id is counted at most once
    /// regardless of how many placement callbacks physics produces; reverts subtract exactly the counted delta.
    /// Not thread-safe; main thread only.
    /// </summary>
    public sealed class ScoreService
    {
        private readonly Dictionary<string, int> countedDeltas = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<string> countedOrder = new List<string>();

        public ScoreService(bool revertScoreOnRemoval = true)
        {
            RevertScoreOnRemoval = revertScoreOnRemoval;
        }

        /// <summary>Policy from <c>AppConfig.Gameplay.RevertScoreOnRemoval</c>.</summary>
        public bool RevertScoreOnRemoval { get; }

        public int Score { get; private set; }

        /// <summary>Counted products with a positive delta.</summary>
        public int PositiveCount { get; private set; }

        /// <summary>Counted products with a negative delta.</summary>
        public int NegativeCount { get; private set; }

        /// <summary>Ids currently counted, in placement order (live view; copy with <see cref="SnapshotCountedProductIds"/>).</summary>
        public IReadOnlyCollection<string> CountedProductIds => countedOrder;

        public event Action<ScoreChange> Changed;

        /// <summary>True when the product id is currently counted.</summary>
        public bool IsCounted(string productId)
        {
            return productId != null && countedDeltas.ContainsKey(productId);
        }

        /// <summary>Delta that was counted for the product, or 0 when not counted.</summary>
        public int CountedDeltaFor(string productId)
        {
            return productId != null && countedDeltas.TryGetValue(productId, out var delta) ? delta : 0;
        }

        /// <summary>Applies the delta once per product id. Returns false (no change, no event) when already counted.</summary>
        public bool TryApplyPlacement(string productId, int delta)
        {
            if (string.IsNullOrEmpty(productId))
            {
                throw new ArgumentException("Product id is required.", nameof(productId));
            }

            if (countedDeltas.ContainsKey(productId))
            {
                return false;
            }

            countedDeltas.Add(productId, delta);
            countedOrder.Add(productId);
            Score += delta;
            if (delta > 0)
            {
                PositiveCount++;
            }
            else if (delta < 0)
            {
                NegativeCount++;
            }

            Changed?.Invoke(new ScoreChange(productId, delta, Score, ScoreChangeReason.Placed));
            return true;
        }

        /// <summary>
        /// Reverts a counted product when <see cref="RevertScoreOnRemoval"/> is on. Returns false when the policy is off
        /// or the product is not counted.
        /// </summary>
        public bool TryRevertPlacement(string productId)
        {
            if (!RevertScoreOnRemoval || productId == null || !countedDeltas.TryGetValue(productId, out var delta))
            {
                return false;
            }

            countedDeltas.Remove(productId);
            countedOrder.Remove(productId);
            Score -= delta;
            if (delta > 0)
            {
                PositiveCount--;
            }
            else if (delta < 0)
            {
                NegativeCount--;
            }

            Changed?.Invoke(new ScoreChange(productId, -delta, Score, ScoreChangeReason.Removed));
            return true;
        }

        /// <summary>Clears the score and every counted product. Always raises <see cref="Changed"/>.</summary>
        public void Reset()
        {
            int previous = Score;
            countedDeltas.Clear();
            countedOrder.Clear();
            Score = 0;
            PositiveCount = 0;
            NegativeCount = 0;
            Changed?.Invoke(new ScoreChange(null, -previous, 0, ScoreChangeReason.Reset));
        }

        /// <summary>Copy of the counted ids in placement order.</summary>
        public string[] SnapshotCountedProductIds()
        {
            return countedOrder.ToArray();
        }
    }
}
