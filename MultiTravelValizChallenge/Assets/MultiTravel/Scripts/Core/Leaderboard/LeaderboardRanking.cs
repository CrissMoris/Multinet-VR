using System.Collections.Generic;

namespace MultiTravel.Core.Leaderboard
{
    /// <summary>
    /// Ranking order mirrored from SQL (ARCHITECTURE.md §5): score DESC, completion_ms ASC, completed_at ASC.
    /// Used for local display and tests; the server remains the source of truth for ranks.
    /// </summary>
    public sealed class LeaderboardRanking : IComparer<LeaderboardEntry>
    {
        public static readonly LeaderboardRanking Instance = new LeaderboardRanking();

        public int Compare(LeaderboardEntry x, LeaderboardEntry y)
        {
            return CompareEntries(x, y);
        }

        /// <summary>Negative when <paramref name="a"/> ranks before <paramref name="b"/>. Nulls sort last.</summary>
        public static int CompareEntries(LeaderboardEntry a, LeaderboardEntry b)
        {
            if (ReferenceEquals(a, b))
            {
                return 0;
            }

            if (a == null)
            {
                return 1;
            }

            if (b == null)
            {
                return -1;
            }

            int result = b.Score.CompareTo(a.Score);
            if (result != 0)
            {
                return result;
            }

            result = a.CompletionMs.CompareTo(b.CompletionMs);
            if (result != 0)
            {
                return result;
            }

            if (a.CompletedAt.HasValue && b.CompletedAt.HasValue)
            {
                return a.CompletedAt.Value.CompareTo(b.CompletedAt.Value);
            }

            if (a.CompletedAt.HasValue)
            {
                return -1;
            }

            return b.CompletedAt.HasValue ? 1 : 0;
        }

        /// <summary>
        /// Returns a new list sorted by the ranking order (stable for full ties) with <see cref="LeaderboardEntry.Rank"/>
        /// assigned sequentially from 1. Null entries are dropped. The input entries are mutated (Rank only).
        /// </summary>
        public static List<LeaderboardEntry> Rank(IEnumerable<LeaderboardEntry> entries)
        {
            var indexed = new List<KeyValuePair<int, LeaderboardEntry>>();
            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    if (entry != null)
                    {
                        indexed.Add(new KeyValuePair<int, LeaderboardEntry>(indexed.Count, entry));
                    }
                }
            }

            indexed.Sort((x, y) =>
            {
                int result = CompareEntries(x.Value, y.Value);
                return result != 0 ? result : x.Key.CompareTo(y.Key);
            });

            var ranked = new List<LeaderboardEntry>(indexed.Count);
            for (int i = 0; i < indexed.Count; i++)
            {
                var entry = indexed[i].Value;
                entry.Rank = i + 1;
                ranked.Add(entry);
            }

            return ranked;
        }
    }
}
