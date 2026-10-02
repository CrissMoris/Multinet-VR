using System;
using MultiTravel.Core.Backend;

namespace MultiTravel.Core.Session
{
    /// <summary>
    /// Per-participant state owned by <see cref="SessionController"/> (ARCHITECTURE.md §2.3).
    /// Created at registration and discarded by <see cref="SessionController.ResetForNextParticipant"/>.
    /// Mutations are internal to the Core assembly; other assemblies read only.
    /// </summary>
    public sealed class ParticipantSession
    {
        public ParticipantSession(Guid clientSessionId, ParticipantInput input, DateTime createdAtUtc)
        {
            if (clientSessionId == Guid.Empty)
            {
                throw new ArgumentException("Client session id must not be empty.", nameof(clientSessionId));
            }

            ClientSessionId = clientSessionId;
            Input = input ?? throw new ArgumentNullException(nameof(input));
            CreatedAtUtc = createdAtUtc;
            Outcome = SessionOutcome.InProgress;
        }

        /// <summary>Idempotency key for <c>register_participant</c>; created at registration.</summary>
        public Guid ClientSessionId { get; }

        /// <summary>Normalised registration data.</summary>
        public ParticipantInput Input { get; }

        public DateTime CreatedAtUtc { get; }

        /// <summary>Selected gender; only meaningful when <see cref="GenderSelected"/> is true.</summary>
        public Gender Gender { get; internal set; }

        public bool GenderSelected { get; internal set; }

        /// <summary>Server participant uuid; null until the backend acknowledged a registration or submission.</summary>
        public Guid? ParticipantId { get; internal set; }

        /// <summary>UTC time the <c>Playing</c> state was entered; null before that.</summary>
        public DateTime? StartedAtUtc { get; internal set; }

        /// <summary>Result snapshot; null until completed.</summary>
        public GameResult Result { get; internal set; }

        /// <summary>Idempotency key for <c>submit_result</c>; created when the result is finalised.</summary>
        public Guid? SubmissionId { get; internal set; }

        /// <summary>Number of HTTP submission attempts made for this session.</summary>
        public int SubmissionAttempts { get; internal set; }

        public SessionOutcome Outcome { get; internal set; }

        /// <summary>Backend acknowledgement (contains the rank when returned); null until submitted.</summary>
        public SubmissionReceipt Receipt { get; internal set; }

        /// <summary>Last submission error for the operator screen; null when none or after success.</summary>
        public BackendError LastSubmissionError { get; internal set; }

        public bool IsCompleted => Result != null;

        public bool IsSubmitted => Receipt != null;
    }
}
