using System;
using System.Collections;
using MultiTravel.Core.Backend;
using MultiTravel.Core.Services;
using MultiTravel.Core.Session;
using MultiTravel.Gameplay.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MultiTravel.Tests.PlayMode.Presentation
{
    /// <summary>Screen fade timing and its session hooks (bounded real-time waits).</summary>
    public sealed class ScreenFadeTests
    {
        private GameplayTestScene scene;
        private ScreenFade fade;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AppServices.Clear();
            scene = new GameplayTestScene();
            var cameraGo = scene.Track(new GameObject("Test camera"));
            var camera = cameraGo.AddComponent<Camera>();
            var go = scene.Track(new GameObject("Fade"));
            fade = go.AddComponent<ScreenFade>();
            fade.Configure(camera);
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
        public IEnumerator FadeOut_ThenFadeIn_Complete()
        {
            Assert.AreEqual(0f, fade.Alpha);
            int outs = 0;
            int ins = 0;
            fade.FadeOutCompleted += () => outs++;
            fade.FadeInCompleted += () => ins++;

            float started = Time.realtimeSinceStartup;
            fade.FadeOut(0.3f);
            Assert.IsTrue(fade.IsFading);
            yield return GameplayTestScene.WaitUntil(() => !fade.IsFading, 2f);
            Assert.IsFalse(fade.IsFading);
            Assert.AreEqual(1f, fade.Alpha, 1e-4f);
            Assert.AreEqual(1, outs);
            Assert.GreaterOrEqual(Time.realtimeSinceStartup - started, 0.25f, "the fade is animated");

            fade.FadeIn(0.3f);
            yield return GameplayTestScene.WaitUntil(() => !fade.IsFading, 2f);
            Assert.AreEqual(0f, fade.Alpha, 1e-4f);
            Assert.AreEqual(1, ins);
        }

        [UnityTest]
        public IEnumerator SessionReset_CutsToBlack_ThenFadesIn()
        {
            fade.Bind(scene.Session);
            scene.EnterPlaying();
            scene.Session.CompleteGame(CompletionReason.OperatorForced);
            scene.Session.OnSubmissionSucceeded(new SubmissionReceipt { ResultId = Guid.NewGuid(), ParticipantId = Guid.NewGuid(), Created = true, Rank = 3 });
            Assert.AreEqual(SessionState.Finished, scene.Session.State);

            scene.Session.ResetForNextParticipant();
            Assert.AreEqual(1f, fade.Alpha, 1e-4f, "the reset frame is black before anything re-poses");
            Assert.IsTrue(fade.IsFading, "and the fade-in has started");
            yield return GameplayTestScene.WaitUntil(() => !fade.IsFading, 2f);
            Assert.AreEqual(0f, fade.Alpha, 1e-4f);
        }
    }
}
