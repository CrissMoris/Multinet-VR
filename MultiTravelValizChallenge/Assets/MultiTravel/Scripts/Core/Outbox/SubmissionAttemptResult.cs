using MultiTravel.Core.Backend;

namespace MultiTravel.Core.Outbox
{
    /// <summary>Outcome of one <see cref="SubmissionOutbox.TrySubmitAsync"/> run (possibly several HTTP attempts).</summary>
    public sealed class SubmissionAttemptResult
    {
        private SubmissionAttemptResult(bool succeeded, SubmissionReceipt receipt, BackendError error, int attempts)
        {
            Succeeded = succeeded;
            Receipt = receipt;
            Error = error;
            Attempts = attempts;
        }

        public bool Succeeded { get; }

        /// <summary>Server acknowledgement when <see cref="Succeeded"/>.</summary>
        public SubmissionReceipt Receipt { get; }

        /// <summary>Last error when not <see cref="Succeeded"/>.</summary>
        public BackendError Error { get; }

        /// <summary>Number of HTTP attempts made during this run.</summary>
        public int Attempts { get; }

        public static SubmissionAttemptResult Success(SubmissionReceipt receipt, int attempts)
        {
            return new SubmissionAttemptResult(true, receipt, null, attempts);
        }

        public static SubmissionAttemptResult Failed(BackendError error, int attempts)
        {
            return new SubmissionAttemptResult(false, null, error ?? BackendError.Transport("unknown"), attempts);
        }
    }
}
