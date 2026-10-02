using System;

namespace MultiTravel.Core.Timing
{
    /// <summary>
    /// Game stopwatch driven by an <see cref="IClock"/> (ARCHITECTURE.md §2.6). Not a MonoBehaviour: the HUD polls
    /// <see cref="ElapsedMs"/>. Stopping freezes the value exactly; <see cref="Start"/> after <see cref="Stop"/>
    /// resumes accumulating; <see cref="Reset"/> stops and zeroes.
    /// </summary>
    public sealed class GameTimer
    {
        private readonly IClock clock;
        private double accumulatedSeconds;
        private double startedAtSeconds;

        public GameTimer(IClock clock)
        {
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public bool IsRunning { get; private set; }

        /// <summary>Elapsed time in whole milliseconds (frozen while stopped).</summary>
        public long ElapsedMs => (long)Math.Floor(ElapsedSeconds * 1000d + 1e-6);

        /// <summary>Elapsed time in seconds (frozen while stopped).</summary>
        public double ElapsedSeconds => accumulatedSeconds + (IsRunning ? clock.NowSeconds - startedAtSeconds : 0d);

        public event Action Started;

        public event Action Stopped;

        /// <summary>Starts (or resumes) the timer. No-op while running.</summary>
        public void Start()
        {
            if (IsRunning)
            {
                return;
            }

            startedAtSeconds = clock.NowSeconds;
            IsRunning = true;
            Started?.Invoke();
        }

        /// <summary>Stops the timer and freezes the elapsed value. No-op while stopped.</summary>
        public void Stop()
        {
            if (!IsRunning)
            {
                return;
            }

            accumulatedSeconds += clock.NowSeconds - startedAtSeconds;
            IsRunning = false;
            Stopped?.Invoke();
        }

        /// <summary>Stops the timer and clears the elapsed value. Raises <see cref="Stopped"/> only if it was running.</summary>
        public void Reset()
        {
            bool wasRunning = IsRunning;
            IsRunning = false;
            accumulatedSeconds = 0d;
            startedAtSeconds = 0d;
            if (wasRunning)
            {
                Stopped?.Invoke();
            }
        }
    }
}
