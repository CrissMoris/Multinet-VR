using System.Diagnostics;

namespace MultiTravel.Core.Timing
{
    /// <summary>Production clock backed by <see cref="Stopwatch"/> (high resolution, monotonic, independent of frame rate).</summary>
    public sealed class StopwatchClock : IClock
    {
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();

        public double NowSeconds => stopwatch.Elapsed.TotalSeconds;
    }
}
