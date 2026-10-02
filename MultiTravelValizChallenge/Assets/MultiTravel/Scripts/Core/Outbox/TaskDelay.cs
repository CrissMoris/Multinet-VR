using System;
using System.Threading;
using System.Threading.Tasks;

namespace MultiTravel.Core.Outbox
{
    /// <summary>Production <see cref="IDelay"/> based on <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.
    /// Continuations resume on the Unity main thread through the Unity synchronization context.</summary>
    public sealed class TaskDelay : IDelay
    {
        public static readonly TaskDelay Instance = new TaskDelay();

        public Task Delay(TimeSpan duration, CancellationToken cancellationToken)
        {
            if (duration <= TimeSpan.Zero)
            {
                return Task.CompletedTask;
            }

            return Task.Delay(duration, cancellationToken);
        }
    }
}
