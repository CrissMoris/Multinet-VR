namespace MultiTravel.Gameplay.Items
{
    /// <summary>Lifecycle of a pooled product instance (ARCHITECTURE.md §2.8).</summary>
    public enum ProductItemState
    {
        /// <summary>Disabled and parked in the pool; not part of the current session.</summary>
        Pooled,

        /// <summary>Active, physics-driven and not held.</summary>
        Free,

        /// <summary>Selected by an interactor (hand or controller).</summary>
        Held,

        /// <summary>Inside the suitcase; kinematic and counted by the suitcase.</summary>
        Placed
    }
}
