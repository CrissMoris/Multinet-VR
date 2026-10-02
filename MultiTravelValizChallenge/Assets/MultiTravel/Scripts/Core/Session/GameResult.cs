using System;

namespace MultiTravel.Core.Session
{
    /// <summary>Immutable result snapshot built by <see cref="SessionController.CompleteGame"/> (ARCHITECTURE.md §2.3).</summary>
    public sealed class GameResult
    {
        public GameResult(
            int score,
            long completionMs,
            int correctCount,
            int incorrectCount,
            int requiredTotal,
            string[] placedProductIds,
            DateTime completedAtUtc,
            CompletionReason reason)
        {
            Score = score;
            CompletionMs = completionMs;
            CorrectCount = correctCount;
            IncorrectCount = incorrectCount;
            RequiredTotal = requiredTotal;
            PlacedProductIds = placedProductIds ?? Array.Empty<string>();
            CompletedAtUtc = completedAtUtc;
            Reason = reason;
        }

        /// <summary>Final score (sum of counted placement deltas).</summary>
        public int Score { get; }

        /// <summary>Elapsed game time in milliseconds (always at least 1, the backend rejects 0).</summary>
        public long CompletionMs { get; }

        /// <summary>Number of counted products with a positive delta.</summary>
        public int CorrectCount { get; }

        /// <summary>Number of counted products with a negative delta.</summary>
        public int IncorrectCount { get; }

        /// <summary>Number of products required for completion in the resolved product set.</summary>
        public int RequiredTotal { get; }

        /// <summary>Ids of the products that were counted, in placement order.</summary>
        public string[] PlacedProductIds { get; }

        /// <summary>UTC time of completion.</summary>
        public DateTime CompletedAtUtc { get; }

        /// <summary>Why the game ended.</summary>
        public CompletionReason Reason { get; }
    }
}
