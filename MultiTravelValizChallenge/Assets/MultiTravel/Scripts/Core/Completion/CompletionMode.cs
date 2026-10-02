namespace MultiTravel.Core.Completion
{
    /// <summary>How a game can be completed (ARCHITECTURE.md §2.7). Configured in <c>AppConfig.Gameplay.CompletionMode</c>.</summary>
    public enum CompletionMode
    {
        /// <summary>The game ends automatically when every required product is in the suitcase.</summary>
        RequiredItemsPlaced,

        /// <summary>The game ends only when the participant presses the manual confirm button.</summary>
        ManualConfirm,

        /// <summary>Either of the above ends the game.</summary>
        RequiredItemsOrManual
    }
}
