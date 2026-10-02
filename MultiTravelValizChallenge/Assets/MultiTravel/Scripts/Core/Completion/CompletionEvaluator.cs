using System;
using System.Collections.Generic;
using MultiTravel.Core.Products;
using MultiTravel.Core.Session;

namespace MultiTravel.Core.Completion
{
    /// <summary>
    /// Decides when a game is complete (ARCHITECTURE.md §2.7). Deterministic and allocation-free in the notify path.
    /// <para>
    /// One instance is registered at bootstrap before any gender is known, so the gameplay director calls
    /// <see cref="Configure"/> with the resolved <see cref="ProductSet"/> on every <c>Loading</c>. The evaluator
    /// never ends the game itself: gameplay (or <c>SessionController.TryCompleteIfDue</c>) polls <see cref="TryGetCompletion"/>.
    /// </para>
    /// </summary>
    public sealed class CompletionEvaluator
    {
        private readonly HashSet<string> placedRequired = new HashSet<string>(StringComparer.Ordinal);
        private ProductSet set;
        private bool manualConfirmed;
        private long elapsedMs;

        public CompletionEvaluator(ProductSet set, CompletionMode mode, int timeLimitSeconds)
        {
            this.set = set ?? ProductSet.Empty(Gender.Female);
            Mode = mode;
            TimeLimitSeconds = Math.Max(0, timeLimitSeconds);
        }

        public CompletionMode Mode { get; }

        /// <summary>0 disables the time limit.</summary>
        public int TimeLimitSeconds { get; }

        /// <summary>The product set currently evaluated.</summary>
        public ProductSet Set => set;

        public int RequiredTotal => set.RequiredIds.Count;

        public int RequiredPlacedCount => placedRequired.Count;

        public bool AllowsItemCompletion => Mode == CompletionMode.RequiredItemsPlaced || Mode == CompletionMode.RequiredItemsOrManual;

        public bool AllowsManualConfirm => Mode == CompletionMode.ManualConfirm || Mode == CompletionMode.RequiredItemsOrManual;

        public bool ManualConfirmed => manualConfirmed;

        public long ElapsedMs => elapsedMs;

        /// <summary>True when every required product is currently placed (and there is at least one).</summary>
        public bool AllRequiredPlaced => RequiredTotal > 0 && placedRequired.Count >= RequiredTotal;

        /// <summary>Swaps the product set and clears all progress.</summary>
        public void Configure(ProductSet newSet)
        {
            set = newSet ?? ProductSet.Empty(set.Gender);
            Reset();
        }

        /// <summary>Clears placed products, the manual confirmation and the elapsed time (keeps the set).</summary>
        public void Reset()
        {
            placedRequired.Clear();
            manualConfirmed = false;
            elapsedMs = 0;
        }

        /// <summary>Records a placement; ids that are not required are ignored.</summary>
        public void NotifyPlaced(string productId)
        {
            if (!string.IsNullOrEmpty(productId) && set.IsRequired(productId))
            {
                placedRequired.Add(productId);
            }
        }

        /// <summary>Records a removal.</summary>
        public void NotifyRemoved(string productId)
        {
            if (!string.IsNullOrEmpty(productId))
            {
                placedRequired.Remove(productId);
            }
        }

        /// <summary>Records the manual confirm button; ignored when the mode does not allow it.</summary>
        public void NotifyManualConfirm()
        {
            if (AllowsManualConfirm)
            {
                manualConfirmed = true;
            }
        }

        /// <summary>Records the current elapsed game time.</summary>
        public void NotifyElapsed(long milliseconds)
        {
            elapsedMs = Math.Max(0, milliseconds);
        }

        /// <summary>
        /// Returns true with the reason when the game should end. Precedence: required items, manual confirm, time limit.
        /// </summary>
        public bool TryGetCompletion(out CompletionReason reason)
        {
            if (AllowsItemCompletion && AllRequiredPlaced)
            {
                reason = CompletionReason.RequiredItemsPlaced;
                return true;
            }

            if (manualConfirmed && AllowsManualConfirm)
            {
                reason = CompletionReason.ManualConfirm;
                return true;
            }

            if (TimeLimitSeconds > 0 && elapsedMs >= TimeLimitSeconds * 1000L)
            {
                reason = CompletionReason.TimeLimit;
                return true;
            }

            reason = default;
            return false;
        }
    }
}
