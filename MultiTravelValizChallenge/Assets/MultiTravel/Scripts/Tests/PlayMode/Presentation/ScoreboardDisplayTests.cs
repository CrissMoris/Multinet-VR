using System.Collections;
using MultiTravel.Core.Services;
using MultiTravel.Gameplay.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MultiTravel.Tests.PlayMode.Presentation
{
    /// <summary>Scoreboard roll-up, greeting and required-item pips.</summary>
    public sealed class ScoreboardDisplayTests
    {
        private GameplayTestScene scene;
        private ScoreboardDisplay board;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AppServices.Clear();
            scene = new GameplayTestScene();
            var go = scene.Track(new GameObject("Scoreboard"));
            board = go.AddComponent<ScoreboardDisplay>();
            board.Bind(scene.Session, scene.Score, scene.Evaluator);
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
        public IEnumerator RollUp_ReachesTheFinalScore_AndPipsFollowTheEvaluator()
        {
            Assert.IsTrue(board.IsAttract, "Welcome shows attract content");
            scene.EnterPlaying();
            yield return null;
            Assert.IsFalse(board.IsAttract);
            Assert.AreEqual(ScoreboardDisplay.GreetingPrefix + "Ayşe", board.Headline.text);
            Assert.AreEqual("0", board.Score.text);
            Assert.AreEqual("0/2", board.Progress.text);

            float started = Time.realtimeSinceStartup;
            Assert.IsTrue(scene.Score.TryApplyPlacement(GameplayTestScene.RequiredA, 10));
            Assert.IsTrue(board.IsRolling, "a score change starts the roll-up");
            yield return GameplayTestScene.WaitUntil(() => !board.IsRolling, 2f);
            Assert.IsFalse(board.IsRolling);
            Assert.GreaterOrEqual(Time.realtimeSinceStartup - started, 0.3f, "the roll-up is animated, not a snap");
            Assert.AreEqual(10, board.DisplayedScore);
            Assert.AreEqual("10", board.Score.text);

            scene.Evaluator.NotifyPlaced(GameplayTestScene.RequiredA);
            yield return null;
            Assert.AreEqual("1/2", board.Progress.text);
            StringAssert.Contains("●", board.Pips.text, "filled pip for the placed required item");
            StringAssert.Contains("○", board.Pips.text, "empty pip for the missing one");
            StringAssert.Contains(PresentationStyle.TealHex, board.Pips.text);

            Assert.IsTrue(scene.Score.TryApplyPlacement(GameplayTestScene.Wrong, -5));
            yield return GameplayTestScene.WaitUntil(() => !board.IsRolling, 2f);
            Assert.AreEqual(5, board.DisplayedScore);
            Assert.AreEqual("5", board.Score.text);
        }

        [UnityTest]
        public IEnumerator Reset_ClearsScoreAndReturnsToAttract()
        {
            scene.EnterPlaying();
            scene.Score.TryApplyPlacement(GameplayTestScene.RequiredA, 10);
            yield return GameplayTestScene.WaitUntil(() => !board.IsRolling, 2f);
            Assert.AreEqual(10, board.DisplayedScore);

            scene.Session.AbandonSession();
            scene.Session.ResetForNextParticipant();
            yield return null;
            Assert.IsTrue(board.IsAttract);
            Assert.AreEqual(0, board.DisplayedScore);
            Assert.AreEqual(string.Empty, board.Pips.text);
        }
    }
}
