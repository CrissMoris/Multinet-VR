using System;
using System.Threading;
using System.Threading.Tasks;

namespace MultiTravel.Core.Outbox
{
    /// <summary>Injectable delay used by <see cref="SubmissionOutbox"/> backoff so tests can run without real waits.</summary>
    public interface IDelay
    {
        /// <summary>Completes after <paramref name="duration"/> or throws <see cref="OperationCanceledException"/> when cancelled.</summary>
        Task Delay(TimeSpan duration, CancellationToken cancellationToken);
    }
}
