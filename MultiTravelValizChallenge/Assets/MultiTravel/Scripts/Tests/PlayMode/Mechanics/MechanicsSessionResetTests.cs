using System;
using System.Collections;
using MultiTravel.Core.Backend;
using MultiTravel.Core.Completion;
using MultiTravel.Core.Services;
using MultiTravel.Core.Session;
using MultiTravel.Gameplay.Items;
using MultiTravel.Gameplay.Suitcase;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MultiTravel.Tests.PlayMode.Mechanics
{
    /// <summary>
    /// Full director-driven session with the v2 mechanics (practice hand-off, garment variant, lid, straps) followed by a reset:
    /// nothing per-participant may survive (ARCHITECTURE.md §7, OVERHAUL_PLAN §7).
    /// </summary>
    public sealed class MechanicsSessionResetTests
    {
        private GameplayTestScene scene;
        private MechanicsFixtures.DirectorRig rig;
        private SuitcaseLid lid;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AppServices.Clear();
            scene = new GameplayTestScene();
            rig = MechanicsFixtures.DirectorRig.Create(scene);
            var pivot = new GameObject(SuitcaseLid.PivotNodeName).transform;
            pivot.SetParent(scene.Suitcase.transform, false);
            lid = scene.Suitcase.gameObject.AddComponent<SuitcaseLid>();
            lid.SetDurations(0.2f, 0.1f, 0.01f);
            lid.Bind(scene.Session);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            scene?.Dispose();
            scene = null;
            AppServices.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator FullSession_WithPracticeLidAndVariants_ThenReset_LeavesZeroState()
        {
            // Instructions: practice round.
            MechanicsFixtures.EnterInstructions(scene);
            yield return null;
            Assert.IsTrue(rig.Director.BeginPractice());
            var practice = rig.Director.PracticeItem;
            GameplayTestScene.Teleport(practice, scene.InsideVolume);
            Assert.IsTrue(scene.Suitcase.TryPlace(practice));

            // Loading → Playing.
            scene.Session.StartGame();
            yield return GameplayTestScene.WaitUntil(() => scene.Session.State == SessionState.Playing, 3f);
            Assert.AreEqual(SessionState.Playing, scene.Session.State);
            var garment = rig.Pool.Find(GameplayTestScene.RequiredA);
            var pen = rig.Pool.Find(GameplayTestScene.RequiredB);
            var wrong = rig.Pool.Find(GameplayTestScene.Wrong);
            Assert.IsNotNull(garment.Variant, "A is a hanging / folded garment");
            Assert.IsNotNull(garment.HomeSlot);

            Assert.IsTrue(scene.Suitcase.TryPlace(wrong));
            Assert.IsTrue(scene.Suitcase.TryPlace(garment));
            Assert.IsTrue(scene.Suitcase.TryPlace(pen));
            yield return GameplayTestScene.WaitUntil(() => scene.Session.State == SessionState.Submitting, 2f);
            Assert.AreEqual(CompletionReason.RequiredItemsPlaced, scene.Session.Current.Result.Reason);
            Assert.AreEqual(15, scene.Session.Current.Result.Score);
            yield return GameplayTestScene.WaitUntil(() => lid.State == LidState.Closed, 2f);
            Assert.AreEqual(LidState.Closed, lid.State);
            Assert.AreEqual(ItemVariant.Folded, garment.Variant.Current);
            Assert.Greater(scene.Suitcase.StackHeight, 0f);

            scene.Session.OnSubmissionSucceeded(new SubmissionReceipt { ResultId = Guid.NewGuid(), ParticipantId = Guid.NewGuid(), Created = true, Rank = 1 });
            scene.Session.ResetForNextParticipant();
            yield return GameplayTestScene.WaitUntil(() => lid.State == LidState.Open, 2f);

            // Core state.
            Assert.AreEqual(SessionState.Welcome, scene.Session.State);
            Assert.IsNull(scene.Session.Current);
            Assert.AreEqual(0, scene.Score.Score);
            Assert.AreEqual(0, scene.Score.CountedProductIds.Count);
            Assert.AreEqual(0, scene.Timer.ElapsedMs);
            Assert.IsFalse(scene.Timer.IsRunning);
            Assert.AreEqual(0, scene.Evaluator.RequiredPlacedCount);

            // Suitcase.
            Assert.AreEqual(0, scene.Suitcase.PlacedCount);
            Assert.AreEqual(scene.Suitcase.Slots.Count, scene.Suitcase.FreeSlotCount);
            Assert.AreEqual(0f, scene.Suitcase.StackHeight);
            Assert.IsFalse(scene.Suitcase.AcceptPlacements);
            Assert.IsFalse(scene.Suitcase.AcceptPracticePlacements);
            Assert.AreEqual(LidState.Open, lid.State);
            Assert.AreEqual(0f, lid.CurrentAngle, 1e-3f);

            // Items.
            Assert.AreEqual(0, rig.Pool.ActiveItems.Count);
            foreach (var item in rig.Pool.AllItems)
            {
                Assert.AreEqual(ProductItemState.Pooled, item.State, item.ProductId);
                Assert.IsFalse(item.gameObject.activeSelf, item.ProductId);
                Assert.IsFalse(item.IsReturning, item.ProductId);
                Assert.AreEqual(item.BaseLocalScale, item.transform.localScale, item.ProductId);
                if (item.Variant != null)
                {
                    Assert.AreEqual(ItemVariant.Hanging, item.Variant.Current, item.ProductId);
                }
            }

            // Practice and lock.
            Assert.AreEqual(ProductItemState.Pooled, practice.State);
            Assert.IsFalse(rig.Director.IsPracticeActive);
            Assert.IsNull(rig.Lock.PracticeItem);
            Assert.IsTrue(rig.Lock.IsLocked);
            Assert.IsNull(rig.Director.ActiveSet);
            Assert.AreEqual(0, rig.Director.CountdownRemaining);
        }
    }
}
