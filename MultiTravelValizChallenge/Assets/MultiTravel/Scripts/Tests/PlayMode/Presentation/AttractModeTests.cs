using System.Collections;
using MultiTravel.Core.Services;
using MultiTravel.Core.Session;
using MultiTravel.Gameplay.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MultiTravel.Tests.PlayMode.Presentation
{
    /// <summary>Attract mode leaderboard fetch, stale-response guard across a reset, and the unconfigured-backend case.</summary>
    public sealed class AttractModeTests
    {
        private GameplayTestScene scene;
        private ScriptedLeaderboardBackend backend;
        private ScoreboardDisplay board;
        private AttractMode attract;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AppServices.Clear();
            scene = new GameplayTestScene();
            backend = new ScriptedLeaderboardBackend();
            var boardGo = scene.Track(new GameObject("Scoreboard"));
            board = boardGo.AddComponent<ScoreboardDisplay>();
            board.Bind(scene.Session, scene.Score, scene.Evaluator);
            var go = scene.Track(new GameObject("Attract"));
            attract = go.AddComponent<AttractMode>();
            attract.Configure(null, board);

            // Start runs before the test binds explicit services; with no AppServices it logs and stays idle.
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("AttractMode: service 'SessionController' is not registered"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("AttractMode: service 'RuntimeConfig' is not registered"));
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
        public IEnumerator StaleLeaderboardResponse_AfterReset_IsIgnored_AndFreshOneIsShown()
        {
            attract.Bind(scene.Session, scene.Config, backend);
            Assert.IsTrue(attract.IsAttracting);
            Assert.AreEqual(1, backend.RequestCount, "Welcome fetches the leaderboard");
            Assert.AreEqual(5, backend.LastLimit);
            int firstEpoch = attract.Epoch;

            // A participant cycle starts and is abandoned; the reset returns to Welcome and fetches again.
            scene.Session.BeginRegistration();
            Assert.IsFalse(attract.IsAttracting);
            scene.Session.AbandonSession();
            scene.Session.ResetForNextParticipant();
            Assert.AreEqual(SessionState.Welcome, scene.Session.State);
            Assert.IsTrue(attract.IsAttracting);
            Assert.Greater(attract.Epoch, firstEpoch);
            Assert.AreEqual(2, backend.RequestCount);

            // The first (stale) response arrives late.
            backend.Complete(0, new[] { ScriptedLeaderboardBackend.Entry(1, "Eski K.", 50, 90000) });
            yield return GameplayTestScene.WaitUntil(() => attract.PendingRequests == 1, 2f);
            yield return null;
            Assert.IsNull(attract.CurrentEntries, "a response from the previous participant cycle is discarded");

            // The fresh response is applied and cycled on the scoreboard.
            var fresh = new[]
            {
                ScriptedLeaderboardBackend.Entry(1, "Ayşe Y.", 170, 65300),
                ScriptedLeaderboardBackend.Entry(2, "Mehmet K.", 160, 70000)
            };
            backend.Complete(1, fresh);
            yield return GameplayTestScene.WaitUntil(() => attract.CurrentEntries != null, 2f);
            Assert.AreSame(fresh, attract.CurrentEntries);
            yield return GameplayTestScene.WaitUntil(() => attract.CycleIndex >= 0, 2f);
            Assert.AreEqual(AttractMode.LeaderboardHeadline, board.Headline.text);
            StringAssert.Contains("Ayşe Y.", board.Score.text);
            StringAssert.Contains("170 puan", board.Score.text);
            StringAssert.Contains("01:05.3", board.Score.text);
            Assert.AreEqual(0, attract.PendingRequests);
        }

        [UnityTest]
        public IEnumerator UnconfiguredBackend_FetchesNothing_AndShowsWelcomeTexts()
        {
            var unconfigured = new MultiTravel.Core.Config.RuntimeConfig(
                new MultiTravel.Core.Config.BackendConfig(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 10, 0),
                scene.Config.Gameplay, scene.Config.Texts, scene.Config.Privacy, scene.Config.Branding, scene.Config.Debug,
                "0.0.0-test", null, null);
            attract.Bind(scene.Session, unconfigured, backend);
            yield return null;
            Assert.IsTrue(attract.IsAttracting);
            Assert.AreEqual(0, backend.RequestCount, "no endpoint: nothing is requested");
            Assert.IsNull(attract.CurrentEntries);
            Assert.AreEqual(scene.Config.Texts.WelcomeTitle, board.Headline.text);
            Assert.AreEqual(scene.Config.Texts.WelcomeSubtitle, board.Score.text);
        }
    }
}
