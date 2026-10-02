namespace MultiTravel.Core.Scoring
{
    /// <summary>Payload of <see cref="ScoreService.Changed"/> (ARCHITECTURE.md §2.5).</summary>
    public readonly struct ScoreChange
    {
        public ScoreChange(string productId, int delta, int newTotal, ScoreChangeReason reason)
        {
            ProductId = productId;
            Delta = delta;
            NewTotal = newTotal;
            Reason = reason;
        }

        /// <summary>Product that caused the change; null for <see cref="ScoreChangeReason.Reset"/>.</summary>
        public string ProductId { get; }

        /// <summary>Signed delta applied to the total (negative for reverts and resets).</summary>
        public int Delta { get; }

        /// <summary>Score after the change.</summary>
        public int NewTotal { get; }

        /// <summary>True when the delta is greater than zero.</summary>
        public bool IsPositive => Delta > 0;

        public ScoreChangeReason Reason { get; }

        public override string ToString()
        {
            return $"{Reason} {ProductId ?? "-"} {(Delta >= 0 ? "+" : string.Empty)}{Delta} => {NewTotal}";
        }
    }
}
