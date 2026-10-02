using System;

namespace MultiTravel.Core.Timing
{
    /// <summary>Deterministic clock for tests: time only moves when <see cref="Advance"/> is called.</summary>
    public sealed class ManualClock : IClock
    {
        private double nowSeconds;

        public ManualClock(double startSeconds = 0d)
        {
            nowSeconds = startSeconds;
        }

        public double NowSeconds
        {
            get => nowSeconds;
            set
            {
                if (value < nowSeconds)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "A clock must be monotonic.");
                }

                nowSeconds = value;
            }
        }

        /// <summary>Moves the clock forward by the given number of seconds.</summary>
        public void Advance(double seconds)
        {
            if (seconds < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(seconds), "A clock must be monotonic.");
            }

            nowSeconds += seconds;
        }

        /// <summary>Moves the clock forward by the given number of milliseconds.</summary>
        public void AdvanceMs(long milliseconds)
        {
            Advance(milliseconds / 1000d);
        }
    }
}
