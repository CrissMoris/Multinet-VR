using System;
using System.Collections.Generic;

namespace MultiTravel.Core.Outbox
{
    /// <summary>
    /// Persistent local storage used by <see cref="SubmissionOutbox"/> and <c>SessionController</c>
    /// (ARCHITECTURE.md §2.2 step 3). Production implementation: <see cref="FileLocalStore"/>.
    /// Entries are keyed by submission id; the JSON content is opaque to the store.
    /// Implementations may throw IO exceptions; callers log and continue.
    /// </summary>
    public interface ILocalStore
    {
        /// <summary>Writes (or overwrites) an outbox entry atomically.</summary>
        void SaveOutboxEntry(Guid submissionId, string json);

        /// <summary>Reads an outbox entry; false when it does not exist.</summary>
        bool TryLoadOutboxEntry(Guid submissionId, out string json);

        /// <summary>Lists the ids of every outbox entry currently stored.</summary>
        IReadOnlyList<Guid> ListOutboxEntries();

        /// <summary>Deletes an outbox entry; true when a file was removed.</summary>
        bool DeleteOutboxEntry(Guid submissionId);

        /// <summary>Appends one PII-free line to the results log (header written on first use).</summary>
        void AppendResultLog(ResultLogLine line);
    }
}
