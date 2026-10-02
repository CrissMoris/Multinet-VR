using System.Collections;
using MultiTravel.Core.Scoring;
using MultiTravel.Core.Services;
using MultiTravel.Core.Session;
using MultiTravel.Gameplay.Items;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MultiTravel.Tests.PlayMode.Mechanics
{
    /// <summary>
    /// Tutorial practice item (OVERHAUL_PLAN §5): grabbable during Instructions while everything else is locked, settles in the
    /// suitcase without ever touching the score or completion, and is withdrawn on Loading.
    /// </summary>
    public sealed class PracticeItemTests
    {
        private GameplayTestScene scene;
        private MechanicsFixtures.DirectorRig rig;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AppServices.Clear();
            scene = new GameplayTestScene();
            rig = MechanicsFixtures.DirectorRig.Create(scene);
            yield return null; // Start: pool + practice instance
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
        public IEnumerator PracticeItem_NeverScores_AndIsWithdrawnOnLoading()
        {
            var practice = rig.Director.PracticeItem;
            Assert.IsNotNull(practice, "the director creates the practice instance at start");
            Assert.IsTrue(practice.IsPractice);
            Assert.AreEqual(ProductItemState.Pooled, practice.State);
            Assert.IsFalse(rig.Director.BeginPractice(), "practice is only offered during Instructions");

            int scoreChanges = 0;
            int scoringPlacements = 0;
            int practicePlaced = 0;
            int practiceEnded = 0;
            scene.Score.Changed += _ => scoreChanges++;
            scene.Suitcase.ItemPlaced += (i, c) => scoringPlacements++;
            rig.Director.PracticePlaced += _ => practicePlaced++;
            rig.Director.PracticeEnded += _ => practiceEnded++;

            MechanicsFixtures.EnterInstructions(scene);
            yield return null;
            Assert.AreEqual(SessionState.Instructions, scene.Session.State);
            Assert.IsTrue(rig.Director.BeginPractice());
            Assert.IsTrue(rig.Director.IsPracticeActive);
            Assert.AreEqual(ProductItemState.Free, practice.State);
            Assert.IsTrue(practice.Grab.enabled, "the practice item is whitelisted");
            Assert.IsTrue(rig.Lock.IsLocked, "everything else stays locked");
            Assert.AreSame(practice, rig.Lock.PracticeItem);
            Assert.IsFalse(scene.Suitcase.AcceptPlacements);
            Assert.IsTrue(scene.Suitcase.AcceptPracticePlacements);

            GameplayTestScene.Teleport(practice, scene.InsideVolume);
            Assert.IsTrue(scene.Suitcase.TryPlace(practice), "the suitcase accepts the practice item during Instructions");
            Assert.AreEqual(ProductItemState.Placed, practice.State);
            Assert.AreEqual(1, practicePlaced);
            Assert.AreEqual(0, scoringPlacements, "no ItemPlaced for the practice item");
            Assert.AreEqual(0, scoreChanges);
            Assert.AreEqual(0, scene.Score.Score);
            Assert.AreEqual(0, scene.Score.CountedProductIds.Count);
            Assert.AreEqual(0, scene.Evaluator.RequiredPlacedCount);
            Assert.IsTrue(rig.Director.IsPracticePlaced);

            yield return new WaitForSecondsRealtime(0.5f); // settles visually like any item
            Assert.IsTrue(scene.Suitcase.TryGetSlot(practice, out var slot));
            Assert.Less(Vector3.Distance(slot.PoseFor(practice).position, practice.transform.position), 0.005f);

            // Removing it again does not touch the score either.
            Assert.IsTrue(scene.Suitcase.Remove(practice));
            Assert.AreEqual(0, scoreChanges);
            practice.ReturnToSpawn(true);
            Assert.IsTrue(scene.Suitcase.TryPlace(practice));

            scene.Session.StartGame();
            Assert.AreEqual(SessionState.Loading, scene.Session.State);
            Assert.AreEqual(ProductItemState.Pooled, practice.State, "withdrawn as soon as Loading starts");
            Assert.IsFalse(scene.Suitcase.Contains(practice));
            Assert.IsNull(rig.Lock.PracticeItem);
            Assert.IsFalse(scene.Suitcase.AcceptPracticePlacements);
            Assert.IsFalse(rig.Director.IsPracticeActive);
            Assert.AreEqual(1, practiceEnded);

            yield return GameplayTestScene.WaitUntil(() => scene.Session.State == SessionState.Playing, 3f);
            Assert.AreEqual(SessionState.Playing, scene.Session.State);
            CollectionAssert.DoesNotContain(rig.Pool.ActiveItems, practice, "the practice item is not part of the session set");
            Assert.IsFalse(scene.Suitcase.TryPlace(practice));

            Assert.IsTrue(scene.Suitcase.TryPlace(rig.Pool.Find(GameplayTestScene.RequiredA)));
            Assert.IsTrue(scene.Suitcase.TryPlace(rig.Pool.Find(GameplayTestScene.RequiredB)));
            yield return GameplayTestScene.WaitUntil(() => scene.Session.State == SessionState.Submitting, 2f);
            Assert.AreEqual(20, scene.Session.Current.Result.Score, "only the two real items scored");
            CollectionAssert.DoesNotContain(scene.Session.Current.Result.PlacedProductIds, MechanicsFixtures.PracticeId);
        }

        [UnityTest]
        public IEnumerator PracticeItem_IsRejected_WhenPracticeIsNotOpen()
        {
            var practice = rig.Director.PracticeItem;
            MechanicsFixtures.EnterInstructions(scene);
            yield return null;
            Assert.IsTrue(rig.Director.BeginPractice());
            scene.Suitcase.AcceptPracticePlacements = false;
            GameplayTestScene.Teleport(practice, scene.InsideVolume);
            Assert.IsFalse(scene.Suitcase.TryPlace(practice));

            scene.Session.AbandonSession();
            yield return null;
            Assert.AreEqual(ProductItemState.Pooled, practice.State, "abandon withdraws the practice item");
            Assert.IsNull(rig.Lock.PracticeItem);
        }
    }
}
