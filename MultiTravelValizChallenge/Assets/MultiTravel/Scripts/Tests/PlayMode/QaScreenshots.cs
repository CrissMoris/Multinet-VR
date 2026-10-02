using System.Collections;
using System.IO;
using MultiTravel.Core.Services;
using MultiTravel.Core.Session;
using MultiTravel.Operator;
using MultiTravel.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MultiTravel.Tests.PlayMode
{
    /// <summary>Explicit QA run: plays a session and renders fixed-camera stills (QA_OUT env var or docs/qa/qa-run).</summary>
    public sealed class QaScreenshots
    {
        [UnityTest, Explicit("Renders QA stills; run on demand with -testFilter QaScreenshots")]
        public IEnumerator RenderFixedCameraStills()
        {
            string dir = System.Environment.GetEnvironmentVariable("QA_OUT");
            if (string.IsNullOrEmpty(dir))
            {
                dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/qa/qa-run"));
            }

            Directory.CreateDirectory(dir);
            string root = Path.Combine(Path.GetTempPath(), "mt-qa-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            AppBootstrap.DataRootOverride = root;
            AppBootstrap.BackendFactoryOverride = _ => new RecordingBackend();

            yield return SceneManager.LoadSceneAsync("Bootstrap");
            yield return GameplayTestScene.WaitUntil(() => Object.FindAnyObjectByType<OperatorScreen>()?.IsBound == true, 15);
            var session = AppServices.Get<SessionController>();
            session.BeginRegistration();
            session.SubmitRegistration(new ParticipantInput { FirstName = "Qa", LastName = "Run", Phone = "05320000000", Email = "qa@example.invalid" });
            session.SelectGender(Gender.Female);
            session.StartGame();
            yield return GameplayTestScene.WaitUntil(() => session.State == SessionState.Playing, 10);
            for (int i = 0; i < 20; i++)
            {
                yield return null;
            }

            foreach (var vignette in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
            {
                if (vignette.name == "TunnelingVignette")
                {
                    vignette.enabled = false;
                }
            }

            var camGo = new GameObject("QaCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 90f;
            cam.nearClipPlane = 0.05f;
            cam.depth = 10;
            var eye = new Vector3(0f, 1.70f, 0f);
            yield return Shot(cam, dir, "play-eye-front", eye, Quaternion.Euler(12f, 0f, 0f));
            yield return Shot(cam, dir, "play-eye-left", eye, Quaternion.Euler(12f, -65f, 0f));
            yield return Shot(cam, dir, "play-eye-right", eye, Quaternion.Euler(12f, 65f, 0f));
            yield return Shot(cam, dir, "play-eye-down", eye, Quaternion.Euler(55f, 0f, 0f));
            yield return Shot(cam, dir, "play-overview", new Vector3(0f, 2.5f, -3.0f), Quaternion.Euler(22f, 0f, 0f));
            Object.Destroy(camGo);
            AppBootstrap.DataRootOverride = null;
            AppBootstrap.BackendFactoryOverride = null;
            Assert.Pass();
        }

        private static IEnumerator Shot(Camera cam, string dir, string name, Vector3 pos, Quaternion rot)
        {
            cam.transform.SetPositionAndRotation(pos, rot);
            yield return null;
            var rt = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.Destroy(rt);
            Object.Destroy(tex);
        }
    }
}
