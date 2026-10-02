namespace MultiTravel.Core.Scoring
{
    /// <summary>Why <see cref="ScoreService.Changed"/> fired.</summary>
    public enum ScoreChangeReason
    {
        /// <summary>A product was counted for the first time.</summary>
        Placed,

        /// <summary>A counted product was removed and its delta reverted.</summary>
        Removed,

        /// <summary>The score was reset to zero.</summary>
        Reset
    }
}
