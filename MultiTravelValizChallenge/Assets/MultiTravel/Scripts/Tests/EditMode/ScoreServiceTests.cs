using System;
using System.Collections.Generic;
using MultiTravel.Core.Scoring;
using NUnit.Framework;

namespace MultiTravel.Tests.EditMode
{
    public sealed class ScoreServiceTests
    {
        private ScoreService service;
        private List<ScoreChange> changes;

        [SetUp]
        public void SetUp()
        {
            service = new ScoreService(revertScoreOnRemoval: true);
            changes = new List<ScoreChange>();
            service.Changed += c => changes.Add(c);
        }

        [Test]
        public void PositivePlacement_AddsScore_AndRaisesPlaced()
        {
            Assert.IsTrue(service.TryApplyPlacement("laptop", 10));

            Assert.AreEqual(10, service.Score);
            Assert.AreEqual(1, service.PositiveCount);
            Assert.AreEqual(0, service.NegativeCount);
            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual("laptop", changes[0].ProductId);
            Assert.AreEqual(10, changes[0].Delta);
            Assert.AreEqual(10, changes[0].NewTotal);
            Assert.IsTrue(changes[0].IsPositive);
            Assert.AreEqual(ScoreChangeReason.Placed, changes[0].Reason);
        }

        [Test]
        public void NegativePlacement_SubtractsScore()
        {
            service.TryApplyPlacement("laptop", 10);
            Assert.IsTrue(service.TryApplyPlacement("beach-towel", -5));

            Assert.AreEqual(5, service.Score);
            Assert.AreEqual(1, service.NegativeCount);
            Assert.IsFalse(changes[1].IsPositive);
            Assert.AreEqual(-5, changes[1].Delta);
        }

        [Test]
        public void DuplicatePlacement_IsIgnored()
        {
            service.TryApplyPlacement("laptop", 10);
            Assert.IsFalse(service.TryApplyPlacement("laptop", 10));
            Assert.IsFalse(service.TryApplyPlacement("laptop", 999));

            Assert.AreEqual(10, service.Score);
            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual(1, service.CountedProductIds.Count);
        }

        [Test]
        public void Revert_SubtractsSameDelta_WhenPolicyOn()
        {
            service.TryApplyPlacement("laptop", 10);
            service.TryApplyPlacement("beach-towel", -5);

            Assert.IsTrue(service.TryRevertPlacement("beach-towel"));

            Assert.AreEqual(10, service.Score);
            Assert.AreEqual(0, service.NegativeCount);
            Assert.IsFalse(service.IsCounted("beach-towel"));
            Assert.AreEqual(ScoreChangeReason.Removed, changes[2].Reason);
            Assert.AreEqual(5, changes[2].Delta);
            Assert.AreEqual(10, changes[2].NewTotal);
        }

        [Test]
        public void Revert_ThenPlaceAgain_CountsAgain()
        {
            service.TryApplyPlacement("laptop", 10);
            service.TryRevertPlacement("laptop");
            Assert.IsTrue(service.TryApplyPlacement("laptop", 10));
            Assert.AreEqual(10, service.Score);
        }

        [Test]
        public void Revert_NotCounted_ReturnsFalse()
        {
            Assert.IsFalse(service.TryRevertPlacement("unknown"));
            Assert.IsFalse(service.TryRevertPlacement(null));
            Assert.AreEqual(0, changes.Count);
        }

        [Test]
        public void Revert_WhenPolicyOff_ReturnsFalse_AndKeepsScore()
        {
            var sticky = new ScoreService(revertScoreOnRemoval: false);
            sticky.TryApplyPlacement("laptop", 10);

            Assert.IsFalse(sticky.TryRevertPlacement("laptop"));
            Assert.AreEqual(10, sticky.Score);
            Assert.IsTrue(sticky.IsCounted("laptop"));
            Assert.IsFalse(sticky.TryApplyPlacement("laptop", 10), "re-placing a sticky item must not double count");
        }

        [Test]
        public void Reset_ClearsEverything_AndRaisesReset()
        {
            service.TryApplyPlacement("laptop", 10);
            service.TryApplyPlacement("pen", 10);
            service.Reset();

            Assert.AreEqual(0, service.Score);
            Assert.AreEqual(0, service.PositiveCount);
            Assert.AreEqual(0, service.CountedProductIds.Count);
            var last = changes[changes.Count - 1];
            Assert.AreEqual(ScoreChangeReason.Reset, last.Reason);
            Assert.AreEqual(-20, last.Delta);
            Assert.AreEqual(0, last.NewTotal);
            Assert.IsNull(last.ProductId);
            Assert.IsTrue(service.TryApplyPlacement("laptop", 10), "ids are countable again after reset");
        }

        [Test]
        public void Snapshot_PreservesPlacementOrder()
        {
            service.TryApplyPlacement("b", 10);
            service.TryApplyPlacement("a", 10);
            service.TryApplyPlacement("c", -5);

            CollectionAssert.AreEqual(new[] { "b", "a", "c" }, service.SnapshotCountedProductIds());
            Assert.AreEqual(10, service.CountedDeltaFor("a"));
            Assert.AreEqual(0, service.CountedDeltaFor("zzz"));
        }

        [Test]
        public void EmptyProductId_Throws()
        {
            Assert.Throws<ArgumentException>(() => service.TryApplyPlacement("", 10));
            Assert.Throws<ArgumentException>(() => service.TryApplyPlacement(null, 10));
        }
    }
}
