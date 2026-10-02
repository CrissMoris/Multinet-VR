using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MultiTravel.Core.Outbox;

namespace MultiTravel.Tests.EditMode.Fakes
{
    /// <summary><see cref="IDelay"/> that records the requested durations and completes immediately.</summary>
    public sealed class ImmediateDelay : IDelay
    {
        public List<TimeSpan> Delays { get; } = new List<TimeSpan>();

        public Task Delay(TimeSpan duration, CancellationToken cancellationToken)
        {
            Delays.Add(duration);
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            return Task.CompletedTask;
        }
    }
}
