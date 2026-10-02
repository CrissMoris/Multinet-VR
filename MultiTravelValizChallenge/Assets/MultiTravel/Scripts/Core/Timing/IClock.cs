namespace MultiTravel.Core.Timing
{
    /// <summary>Monotonic time source (ARCHITECTURE.md §2.6). Never frame based.</summary>
    public interface IClock
    {
        /// <summary>Seconds elapsed since the clock was created; must never decrease.</summary>
        double NowSeconds { get; }
    }
}
