using MultiTravel.Core.Completion;
using MultiTravel.Core.Products;
using MultiTravel.Core.Session;
using MultiTravel.Tests.EditMode.Fakes;
using NUnit.Framework;

namespace MultiTravel.Tests.EditMode
{
    public sealed class CompletionEvaluatorTests
    {
        private ScriptableObjectFactory so;
        private ProductSet set;

        [SetUp]
        public void SetUp()
        {
            so = new ScriptableObjectFactory();
            set = so.Set(
                Gender.Female,
                so.Product("laptop", true),
                so.Product("pen", true),
                so.Product("optional-correct", true, required: false),
                so.Product("beach-towel", false));
        }

        [TearDown]
        public void TearDown()
        {
            so.DestroyAll();
        }

        [Test]
        public void RequiredItemsPlaced_CompletesOnlyWhenEveryRequiredIsPlaced()
        {
            var evaluator = new CompletionEvaluator(set, CompletionMode.RequiredItemsPlaced, 0);
            Assert.AreEqual(2, evaluator.RequiredTotal);

            evaluator.NotifyPlaced("laptop");
            evaluator.NotifyPlaced("beach-towel");
            evaluator.NotifyPlaced("optional-correct");
            Assert.AreEqual(1, evaluator.RequiredPlacedCount);
            Assert.IsFalse(evaluator.TryGetCompletion(out _));

            evaluator.NotifyPlaced("pen");
            Assert.IsTrue(evaluator.TryGetCompletion(out var reason));
            Assert.AreEqual(CompletionReason.RequiredItemsPlaced, reason);
        }

        [Test]
        public void Removal_UncompletesUntilPlacedAgain()
        {
            var evaluator = new CompletionEvaluator(set, CompletionMode.RequiredItemsPlaced, 0);
            evaluator.NotifyPlaced("laptop");
            evaluator.NotifyPlaced("pen");
            evaluator.NotifyRemoved("pen");

            Assert.IsFalse(evaluator.TryGetCompletion(out _));
            Assert.AreEqual(1, evaluator.RequiredPlacedCount);

            evaluator.NotifyPlaced("pen");
            Assert.IsTrue(evaluator.TryGetCompletion(out _));
        }

        [Test]
        public void DuplicatePlacedNotifications_DoNotInflateCount()
        {
            var evaluator = new CompletionEvaluator(set, CompletionMode.RequiredItemsPlaced, 0);
            evaluator.NotifyPlaced("laptop");
            evaluator.NotifyPlaced("laptop");
            evaluator.NotifyPlaced("laptop");

            Assert.AreEqual(1, evaluator.RequiredPlacedCount);
            Assert.IsFalse(evaluator.TryGetCompletion(out _));
        }

        [Test]
        public void RequiredItemsPlaced_IgnoresManualConfirm()
        {
            var evaluator = new CompletionEvaluator(set, CompletionMode.RequiredItemsPlaced, 0);
            evaluator.NotifyManualConfirm();

            Assert.IsFalse(evaluator.ManualConfirmed);
            Assert.IsFalse(evaluator.TryGetCompletion(out _));
        }

        [Test]
        public void ManualConfirm_IgnoresItems_CompletesOnConfirm()
        {
            var evaluator = new CompletionEvaluator(set, CompletionMode.ManualConfirm, 0);
            evaluator.NotifyPlaced("laptop");
            evaluator.NotifyPlaced("pen");
            Assert.IsFalse(evaluator.TryGetCompletion(out _));

            evaluator.NotifyManualConfirm();
            Assert.IsTrue(evaluator.TryGetCompletion(out var reason));
            Assert.AreEqual(CompletionReason.ManualConfirm, reason);
        }

        [Test]
        public void RequiredItemsOrManual_CompletesOnEither()
        {
            var byItems = new CompletionEvaluator(set, CompletionMode.RequiredItemsOrManual, 0);
            byItems.NotifyPlaced("laptop");
            byItems.NotifyPlaced("pen");
            Assert.IsTrue(byItems.TryGetCompletion(out var itemsReason));
            Assert.AreEqual(CompletionReason.RequiredItemsPlaced, itemsReason);

            var byManual = new CompletionEvaluator(set, CompletionMode.RequiredItemsOrManual, 0);
            byManual.NotifyManualConfirm();
            Assert.IsTrue(byManual.TryGetCompletion(out var manualReason));
            Assert.AreEqual(CompletionReason.ManualConfirm, manualReason);
        }

        [Test]
        public void TimeLimit_CompletesWhenElapsedReachesLimit()
        {
            var evaluator = new CompletionEvaluator(set, CompletionMode.RequiredItemsPlaced, 90);

            evaluator.NotifyElapsed(89_999);
            Assert.IsFalse(evaluator.TryGetCompletion(out _));

            evaluator.NotifyElapsed(90_000);
            Assert.IsTrue(evaluator.TryGetCompletion(out var reason));
            Assert.AreEqual(CompletionReason.TimeLimit, reason);
        }

        [Test]
        public void TimeLimit_Zero_NeverCompletesByTime()
        {
            var evaluator = new CompletionEvaluator(set, CompletionMode.ManualConfirm, 0);
            evaluator.NotifyElapsed(long.MaxValue / 2);
            Assert.IsFalse(evaluator.TryGetCompletion(out _));
        }

        [Test]
        public void ItemsTakePrecedenceOverTimeLimit()
        {
            var evaluator = new CompletionEvaluator(set, CompletionMode.RequiredItemsPlaced, 1);
            evaluator.NotifyElapsed(5_000);
            evaluator.NotifyPlaced("laptop");
            evaluator.NotifyPlaced("pen");

            Assert.IsTrue(evaluator.TryGetCompletion(out var reason));
            Assert.AreEqual(CompletionReason.RequiredItemsPlaced, reason);
        }

        [Test]
        public void EmptyRequiredSet_NeverCompletesByItems()
        {
            var empty = new CompletionEvaluator(ProductSet.Empty(Gender.Male), CompletionMode.RequiredItemsPlaced, 0);
            Assert.AreEqual(0, empty.RequiredTotal);
            Assert.IsFalse(empty.TryGetCompletion(out _));

            var nullSet = new CompletionEvaluator(null, CompletionMode.RequiredItemsOrManual, 0);
            Assert.AreEqual(0, nullSet.RequiredTotal);
            Assert.IsFalse(nullSet.TryGetCompletion(out _));
        }

        [Test]
        public void Configure_SwapsSet_AndResetsProgress()
        {
            var evaluator = new CompletionEvaluator(ProductSet.Empty(Gender.Female), CompletionMode.RequiredItemsPlaced, 0);
            evaluator.NotifyManualConfirm();
            evaluator.NotifyElapsed(5_000);

            evaluator.Configure(set);
            Assert.AreEqual(2, evaluator.RequiredTotal);
            Assert.AreEqual(0, evaluator.RequiredPlacedCount);
            Assert.AreEqual(0, evaluator.ElapsedMs);
            Assert.IsFalse(evaluator.ManualConfirmed);

            evaluator.NotifyPlaced("laptop");
            evaluator.Reset();
            Assert.AreEqual(0, evaluator.RequiredPlacedCount);
            Assert.AreSame(set, evaluator.Set);
        }

        [Test]
        public void NullAndEmptyIds_AreIgnored()
        {
            var evaluator = new CompletionEvaluator(set, CompletionMode.RequiredItemsPlaced, 0);
            evaluator.NotifyPlaced(null);
            evaluator.NotifyPlaced(string.Empty);
            evaluator.NotifyRemoved(null);
            Assert.AreEqual(0, evaluator.RequiredPlacedCount);
        }
    }
}
