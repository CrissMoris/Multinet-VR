using System;
using System.Collections.Generic;
using System.IO;
using MultiTravel.Core.Outbox;

namespace MultiTravel.Tests.EditMode.Fakes
{
    /// <summary>Dictionary-backed <see cref="ILocalStore"/> for deterministic outbox tests.</summary>
    public sealed class InMemoryLocalStore : ILocalStore
    {
        public Dictionary<Guid, string> Entries { get; } = new Dictionary<Guid, string>();

        public List<ResultLogLine> LogLines { get; } = new List<ResultLogLine>();

        public int SaveCount { get; private set; }

        public int DeleteCount { get; private set; }

        /// <summary>When true, <see cref="SaveOutboxEntry"/> throws an <see cref="IOException"/>.</summary>
        public bool ThrowOnSave { get; set; }

        public void SaveOutboxEntry(Guid submissionId, string json)
        {
            if (ThrowOnSave)
            {
                throw new IOException("disk full (simulated)");
            }

            Entries[submissionId] = json;
            SaveCount++;
        }

        public bool TryLoadOutboxEntry(Guid submissionId, out string json)
        {
            return Entries.TryGetValue(submissionId, out json);
        }

        public IReadOnlyList<Guid> ListOutboxEntries()
        {
            var ids = new List<Guid>(Entries.Keys);
            ids.Sort();
            return ids;
        }

        public bool DeleteOutboxEntry(Guid submissionId)
        {
            if (Entries.Remove(submissionId))
            {
                DeleteCount++;
                return true;
            }

            return false;
        }

        public void AppendResultLog(ResultLogLine line)
        {
            LogLines.Add(line);
        }
    }
}
