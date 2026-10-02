using System;
using System.Collections.Generic;
using MultiTravel.Core.Leaderboard;
using NUnit.Framework;

namespace MultiTravel.Tests.EditMode
{
    public sealed class LeaderboardRankingTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);

        private static LeaderboardEntry Entry(string name, int score, long ms, DateTime? completedAt = null)
        {
            return new LeaderboardEntry { DisplayName = name, Score = score, CompletionMs = ms, CompletedAt = completedAt ?? T0 };
        }

        private static string[] Names(IEnumerable<LeaderboardEntry> entries)
        {
            var names = new List<string>();
            foreach (var e in entries)
            {
                names.Add(e.DisplayName);
            }

            return names.ToArray();
        }

        [Test]
        public void HigherScore_RanksFirst_RegardlessOfTime()
        {
            var slowHigh = Entry("slow-high", 50, 90000);
            var fastLow = Entry("fast-low", 40, 1000);

            Assert.Less(LeaderboardRanking.CompareEntries(slowHigh, fastLow), 0);
            Assert.Greater(LeaderboardRanking.CompareEntries(fastLow, slowHigh), 0);
        }

        [Test]
        public void EqualScore_FasterTimeRanksFirst()
        {
            var fast = Entry("fast", 30, 20000);
            var slow = Entry("slow", 30, 20001);

            Assert.Less(LeaderboardRanking.CompareEntries(fast, slow), 0);
            Assert.Greater(LeaderboardRanking.CompareEntries(slow, fast), 0);
        }

        [Test]
        public void EqualScoreAndTime_EarlierCompletionRanksFirst()
        {
            var early = Entry("early", 30, 20000, T0);
            var late = Entry("late", 30, 20000, T0.AddMilliseconds(1));

            Assert.Less(LeaderboardRanking.CompareEntries(early, late), 0);
            Assert.Greater(LeaderboardRanking.CompareEntries(late, early), 0);
        }

        [Test]
        public void MissingCompletedAt_SortsAfterKnownTimestamp()
        {
            var known = Entry("known", 30, 20000, T0.AddDays(1));
            var unknown = new LeaderboardEntry { DisplayName = "unknown", Score = 30, CompletionMs = 20000, CompletedAt = null };

            Assert.Less(LeaderboardRanking.CompareEntries(known, unknown), 0);
            Assert.Greater(LeaderboardRanking.CompareEntries(unknown, known), 0);
            Assert.AreEqual(0, LeaderboardRanking.CompareEntries(unknown, new LeaderboardEntry { Score = 30, CompletionMs = 20000 }));
        }

        [Test]
        public void FullTie_ComparesEqual_AndNullsSortLast()
        {
            var a = Entry("a", 10, 1000);
            var b = Entry("b", 10, 1000);

            Assert.AreEqual(0, LeaderboardRanking.CompareEntries(a, b));
            Assert.AreEqual(0, LeaderboardRanking.CompareEntries(a, a));
            Assert.Less(LeaderboardRanking.CompareEntries(a, null), 0);
            Assert.Greater(LeaderboardRanking.CompareEntries(null, a), 0);
            Assert.AreEqual(0, LeaderboardRanking.CompareEntries(null, null));
        }

        [Test]
        public void NegativeScores_StillOrderDescending()
        {
            var zero = Entry("zero", 0, 5000);
            var negative = Entry("negative", -5, 1000);

            Assert.Less(LeaderboardRanking.CompareEntries(zero, negative), 0);
        }

        [Test]
        public void Rank_SortsByAllTiebreaks_AndAssignsSequentialRanks()
        {
            var input = new List<LeaderboardEntry>
            {
                Entry("e-30-slow", 30, 25000),
                Entry("a-50", 50, 60000),
                null,
                Entry("d-30-fast-late", 30, 20000, T0.AddMinutes(5)),
                Entry("f-10", 10, 1000),
                Entry("c-30-fast-early", 30, 20000, T0),
                Entry("b-40", 40, 1000)
            };

            var ranked = LeaderboardRanking.Rank(input);

            CollectionAssert.AreEqual(
                new[] { "a-50", "b-40", "c-30-fast-early", "d-30-fast-late", "e-30-slow", "f-10" },
                Names(ranked));
            for (int i = 0; i < ranked.Count; i++)
            {
                Assert.AreEqual(i + 1, ranked[i].Rank);
            }
        }

        [Test]
        public void Rank_FullTies_KeepInputOrder()
        {
            var first = Entry("first", 20, 3000);
            var second = Entry("second", 20, 3000);
            var third = Entry("third", 20, 3000);

            var ranked = LeaderboardRanking.Rank(new[] { first, second, third });

            CollectionAssert.AreEqual(new[] { "first", "second", "third" }, Names(ranked));
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, new[] { ranked[0].Rank, ranked[1].Rank, ranked[2].Rank });
        }

        [Test]
        public void Rank_NullOrEmptyInput_ReturnsEmptyList()
        {
            Assert.AreEqual(0, LeaderboardRanking.Rank(null).Count);
            Assert.AreEqual(0, LeaderboardRanking.Rank(new LeaderboardEntry[0]).Count);
        }

        [Test]
        public void Comparer_WorksWithListSort()
        {
            var list = new List<LeaderboardEntry>
            {
                Entry("slow", 10, 9000),
                Entry("best", 99, 9000),
                Entry("fast", 10, 1000)
            };

            list.Sort(LeaderboardRanking.Instance);

            CollectionAssert.AreEqual(new[] { "best", "fast", "slow" }, Names(list));
        }
    }
}
