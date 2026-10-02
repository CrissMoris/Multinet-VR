using System.Collections;
using MultiTravel.Core.Services;
using MultiTravel.Core.Session;
using MultiTravel.Gameplay.Presentation;
using MultiTravel.Tests.PlayMode.Mechanics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MultiTravel.Tests.PlayMode.Presentation
{
    /// <summary>Stopwatch digits and needle driven by the Core timer with a ManualClock (no HMD, bounded waits).</summary>
    public sealed class StopwatchDisplayTests
    {
        private GameplayTestScene scene;
        private StopwatchDisplay display;
        private Transform needle;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AppServices.Clear();
            scene = new GameplayTestScene(countdownSeconds: 3);
            var go = scene.Track(new GameObject("Stopwatch"));
            needle = new GameObject("Needle").transform;
            needle.SetParent(go.transform, false);
            display = go.AddComponent<StopwatchDisplay>();
            display.Configure(needle, null, null, null);
            display.Bind(scene.Session, scene.Timer);
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
        public IEnumerator Idle_Countdown_Playing_Completed_ShowTheExpectedText()
        {
            Assert.AreEqual(StopwatchDisplay.IdleText, display.DigitsText, "Welcome shows the wordmark");
            Assert.AreEqual(0f, display.NeedleAngle, 1e-3f);

            MechanicsFixtures.EnterInstructions(scene);
            scene.Session.StartGame();
            scene.Evaluator.Configure(scene.Set);
            scene.Session.LoadingFinished();
            Assert.AreEqual(SessionState.Countdown, scene.Session.State);
            yield return null;
            Assert.AreEqual("3", display.DigitsText, "countdown starts at the configured seconds");

            display.OnCountdownTick(2);
            Assert.AreEqual("2", display.DigitsText);
            Assert.Greater(display.Digits.transform.localScale.x, 1.05f, "tick pops the digits");
            display.OnCountdownTick(1);
            Assert.AreEqual("1", display.DigitsText);
            display.OnCountdownTick(0);
            Assert.AreEqual(StopwatchDisplay.StartText, display.DigitsText);

            scene.Session.CountdownFinished();
            Assert.AreEqual(SessionState.Playing, scene.Session.State);
            yield return null;
            Assert.AreEqual("00:00.0", display.DigitsText);

            scene.Clock.Advance(65.3);
            yield return null;
            Assert.AreEqual("01:05.3", display.DigitsText);
            Assert.AreEqual(31.8f, display.NeedleAngle, 0.05f, "needle: 6 degrees per second, one revolution per minute");
            Assert.AreEqual(31.8f, Quaternion.Angle(Quaternion.identity, needle.localRotation), 0.05f);

            scene.Clock.Advance(0.25);
            yield return null;
            Assert.AreEqual("01:05.5", display.DigitsText);

            scene.Session.CompleteGame(CompletionReason.OperatorForced);
            yield return null;
            string frozen = display.DigitsText;
            Assert.AreEqual("01:05.5", frozen, "completion shows the final time");
            scene.Clock.Advance(10);
            yield return null;
            yield return null;
            Assert.AreEqual(frozen, display.DigitsText, "the final time stays frozen while the clock keeps running");
        }

        [UnityTest]
        public IEnumerator Reset_ReturnsToIdleAndZeroNeedle()
        {
            scene.EnterPlaying();
            scene.Clock.Advance(12.0);
            yield return null;
            Assert.AreEqual("00:12.0", display.DigitsText);

            scene.Session.AbandonSession();
            scene.Session.ResetForNextParticipant();
            yield return null;
            Assert.AreEqual(StopwatchDisplay.IdleText, display.DigitsText);
            Assert.AreEqual(0f, display.NeedleAngle, 1e-3f);
        }
    }
}
