namespace MultiTravel.Core.Session
{
    /// <summary>Why a game ended (ARCHITECTURE.md §2.3). Serialised to the backend as <c>completion_reason</c>.</summary>
    public enum CompletionReason
    {
        /// <summary>Every required product was placed in the suitcase.</summary>
        RequiredItemsPlaced,

        /// <summary>The participant pressed the "Valizi Tamamla" button (manual confirm modes only).</summary>
        ManualConfirm,

        /// <summary>The configured time limit was reached.</summary>
        TimeLimit,

        /// <summary>The operator forced completion from the operator screen.</summary>
        OperatorForced
    }
}
