using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace MultiTravel.Core.Outbox
{
    /// <summary>
    /// <see cref="ILocalStore"/> backed by files under a root directory (normally <c>Application.persistentDataPath</c>):
    /// <list type="bullet">
    /// <item><c>{root}/outbox/{submissionId}.json</c> — one file per pending submission, written atomically (temp file + move).</item>
    /// <item><c>{root}/results-log.csv</c> — append-only PII-free log for the event team.</item>
    /// </list>
    /// </summary>
    public sealed class FileLocalStore : ILocalStore
    {
        public const string OutboxDirectoryName = "outbox";
        public const string ResultsLogFileName = "results-log.csv";
        private const string EntryExtension = ".json";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public FileLocalStore(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                throw new ArgumentException("Root path is required.", nameof(rootPath));
            }

            RootPath = rootPath;
            OutboxDirectory = Path.Combine(rootPath, OutboxDirectoryName);
            ResultsLogPath = Path.Combine(rootPath, ResultsLogFileName);
        }

        /// <summary>Creates the store rooted at <see cref="Application.persistentDataPath"/>.</summary>
        public static FileLocalStore CreateDefault()
        {
            return new FileLocalStore(Application.persistentDataPath);
        }

        public string RootPath { get; }

        public string OutboxDirectory { get; }

        public string ResultsLogPath { get; }

        /// <summary>Full path of the entry file for a submission id.</summary>
        public string GetEntryPath(Guid submissionId)
        {
            return Path.Combine(OutboxDirectory, submissionId.ToString("D") + EntryExtension);
        }

        public void SaveOutboxEntry(Guid submissionId, string json)
        {
            Directory.CreateDirectory(OutboxDirectory);
            var finalPath = GetEntryPath(submissionId);
            var tempPath = finalPath + ".tmp";

            File.WriteAllText(tempPath, json ?? string.Empty, Utf8NoBom);
            if (File.Exists(finalPath))
            {
                File.Delete(finalPath);
            }

            File.Move(tempPath, finalPath);
        }

        public bool TryLoadOutboxEntry(Guid submissionId, out string json)
        {
            var path = GetEntryPath(submissionId);
            if (!File.Exists(path))
            {
                json = null;
                return false;
            }

            json = File.ReadAllText(path, Encoding.UTF8);
            return true;
        }

        public IReadOnlyList<Guid> ListOutboxEntries()
        {
            var ids = new List<Guid>();
            if (!Directory.Exists(OutboxDirectory))
            {
                return ids;
            }

            var files = Directory.GetFiles(OutboxDirectory, "*" + EntryExtension);
            Array.Sort(files, StringComparer.Ordinal);
            foreach (var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (Guid.TryParseExact(name, "D", out var id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }

        public bool DeleteOutboxEntry(Guid submissionId)
        {
            var path = GetEntryPath(submissionId);
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }

        public void AppendResultLog(ResultLogLine line)
        {
            if (line == null)
            {
                throw new ArgumentNullException(nameof(line));
            }

            Directory.CreateDirectory(RootPath);
            bool writeHeader = !File.Exists(ResultsLogPath) || new FileInfo(ResultsLogPath).Length == 0;

            using (var stream = new FileStream(ResultsLogPath, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, Utf8NoBom))
            {
                if (writeHeader)
                {
                    writer.WriteLine(ResultLogLine.Header);
                }

                writer.WriteLine(line.ToCsv());
            }
        }
    }
}
