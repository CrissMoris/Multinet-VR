using System.Collections;
using MultiTravel.Core.Services;
using MultiTravel.Core.Session;
using MultiTravel.Gameplay.Items;
using MultiTravel.Gameplay.Tutorial;
using MultiTravel.Tests.PlayMode.Mechanics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MultiTravel.Tests.PlayMode.Presentation
{
    /// <summary>
    /// Tutorial over the director's practice hand-off: simulated grab and place with an XRI direct interactor passes the
    /// tutorial, publishes <see cref="ITutorialStatus"/> and never touches the score.
    /// </summary>
    public sealed class TutorialControllerTests
    {
        private GameplayTestScene scene;
        private MechanicsFixtures.DirectorRig rig;
        private TutorialController tutorial;
        private int changedEvents;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AppServices.Clear();
            scene = new GameplayTestScene();
            rig = MechanicsFixtures.DirectorRig.Create(scene);
            var go = scene.Track(new GameObject("Tutorial"));
            tutorial = go.AddComponent<TutorialController>();
            tutorial.Configure(rig.Director, scene.Suitcase, null);
            tutorial.Bind(scene.Session);
            changedEvents = 0;
            tutorial.Changed += () => changedEvents++;
            yield return null; // Start() on the pool / director (practice instance created)
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
        public IEnumerator GrabAndPlace_PassesTheTutorial_WithoutScoring()
        {
            Assert.AreEqual(TutorialPhase.Inactive, tutorial.Phase);
            Assert.AreSame(tutorial, AppServices.Get<ITutorialStatus>(), "status is published for the operator screen");
            Assert.IsFalse(tutorial.Passed);

            MechanicsFixtures.EnterInstructions(scene);
            yield return null;
            Assert.AreEqual(TutorialPhase.Grab, tutorial.Phase);
            var item = tutorial.PracticeItem;
            Assert.IsNotNull(item, "the director handed over its practice item");
            Assert.IsTrue(item.IsPractice);
            Assert.AreEqual(ProductItemState.Free, item.State);
            Assert.IsTrue(item.Grab.enabled, "the practice item is whitelisted while everything else is locked");
            Assert.AreEqual(TutorialController.CaptionGrab, tutorial.Caption.Text.text);
            Assert.IsTrue(tutorial.Caption.IsShown);
            Assert.IsTrue(rig.Lock.IsLocked);
            Assert.IsFalse(rig.Pool.Find(GameplayTestScene.RequiredA) != null && rig.Pool.Find(GameplayTestScene.RequiredA).Grab.enabled, "catalog items stay locked");

            var hand = MechanicsFixtures.CreateHand(scene);
            yield return null;
            MechanicsFixtures.Grab(scene, hand, item);
            Assert.AreEqual(ProductItemState.Held, item.State);
            Assert.AreEqual(TutorialPhase.Place, tutorial.Phase);
            Assert.AreEqual(TutorialController.CaptionPlace, tutorial.Caption.Text.text);

            MechanicsFixtures.ReleaseAt(scene, hand, item, scene.InsideVolume);
            yield return GameplayTestScene.WaitUntil(() => tutorial.Passed, 2f);
            Assert.IsTrue(tutorial.Passed, "release inside the suitcase passes the tutorial");
            Assert.AreEqual(TutorialPhase.Passed, tutorial.Phase);
            Assert.AreEqual(TutorialController.CaptionPassed, tutorial.Caption.Text.text);
            Assert.AreEqual(1, changedEvents);
            Assert.IsTrue(AppServices.Get<ITutorialStatus>().Passed);

            Assert.AreEqual(0, scene.Score.Score, "the practice item never scores");
            Assert.AreEqual(0, scene.Score.CountedProductIds.Count);
            Assert.AreEqual(0, scene.Evaluator.RequiredPlacedCount);
            Assert.AreEqual(SessionState.Instructions, scene.Session.State);

            // The operator starts the game: the practice item is withdrawn and the status resets.
            scene.Session.StartGame();
            yield return null;
            Assert.AreEqual(TutorialPhase.Inactive, tutorial.Phase);
            Assert.IsFalse(tutorial.Passed);
            Assert.AreEqual(2, changedEvents);
            Assert.AreEqual(ProductItemState.Pooled, item.State, "the practice item is removed on Loading");
            Assert.IsFalse(scene.Suitcase.Contains(item));
        }

        [UnityTest]
        public IEnumerator Abandon_DuringTutorial_ResetsEverything()
        {
            MechanicsFixtures.EnterInstructions(scene);
            yield return null;
            var item = tutorial.PracticeItem;
            Assert.IsNotNull(item);

            scene.Session.AbandonSession();
            scene.Session.ResetForNextParticipant();
            yield return null;
            Assert.AreEqual(TutorialPhase.Inactive, tutorial.Phase);
            Assert.IsFalse(tutorial.Passed);
            Assert.IsFalse(tutorial.Caption.IsShown);
            Assert.AreEqual(ProductItemState.Pooled, item.State);
            Assert.AreEqual(0, scene.Score.Score);
        }
    }
}
