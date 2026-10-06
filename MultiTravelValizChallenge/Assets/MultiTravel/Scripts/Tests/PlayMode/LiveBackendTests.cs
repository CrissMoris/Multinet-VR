using System;
using System.Collections;
using System.IO;
using System.Linq;
using MultiTravel.Core.Backend;
using MultiTravel.Core.Config;
using MultiTravel.Core.Services;
using MultiTravel.Core.Session;
using MultiTravel.Gameplay;
using MultiTravel.Gameplay.Items;
using MultiTravel.Gameplay.Suitcase;
using MultiTravel.Operator;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MultiTravel.Tests.PlayMode
{
    /// <summary>
    /// End-to-end run against a REAL Supabase backend through the real <see cref="SupabaseBackendClient"/>
    /// (UnityWebRequest): plays one session in the assembled game, waits for the server receipt and reads the
    /// leaderboard back. Opt-in only — it writes a result row. Enable with the environment variable
    /// MT_LIVE_BACKEND=1 or the file &lt;project&gt;/Temp/mt_live_backend.flag, and point the editor's
    /// StreamingAssets/multitravel.config.json at a local / staging stack (tools/Configure-Local.ps1). The test refuses
    /// to run against a non-local URL unless the flag file contains "allow-remote".
    /// </summary>
    public sealed class LiveBackendTests
    {
        private string dataRoot;

        private static string FlagPath => Path.Combine(Application.dataPath, "..", "Temp", "mt_live_backend.flag");

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            bool enabled = Environment.GetEnvironmentVariable("MT_LIVE_BACKEND") == "1" || File.Exists(FlagPath);
            if (!enabled)
            {
                Assert.Ignore("Live backend test is opt-in (MT_LIVE_BACKEND=1 or Temp/mt_live_backend.flag).");
            }

            dataRoot = Path.Combine(Path.GetTempPath(), "mt-live-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataRoot);
            AppBootstrap.DataRootOverride = dataRoot;
            AppBootstrap.BackendFactoryOverride = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator RealBackend_SessionIsSubmitted_AndAppearsOnTheLeaderboard()
        {
            yield return SceneManager.LoadSceneAsync("Bootstrap");
            yield return GameplayTestScene.WaitUntil(() => UnityEngine.Object.FindAnyObjectByType<OperatorScreen>()?.IsBound == true, 15);
            var config = AppServices.Get<RuntimeConfig>();
            string url = config.Backend.SupabaseUrl ?? string.Empty;
            bool local = url.Contains("127.0.0.1") || url.Contains("localhost");
            if (!local && !(File.Exists(FlagPath) && File.ReadAllText(FlagPath).Contains("allow-remote")))
            {
                Assert.Ignore($"Refusing to write test data to non-local backend {url}.");
            }

            var backend = AppServices.Get<IBackendClient>();
            var ping = backend.PingAsync();
            yield return new WaitUntil(() => ping.IsCompleted);
            Assert.IsTrue(ping.Result.Ok, "ping: " + ping.Result.Error?.Code);

            var session = AppServices.Get<SessionController>();
            var pool = UnityEngine.Object.FindAnyObjectByType<ItemPool>();
            var suitcase = UnityEngine.Object.FindAnyObjectByType<SuitcaseController>();
            string lastName = "Test" + UnityEngine.Random.Range(1000, 9999);
            session.BeginRegistration();
            Assert.IsTrue(session.SubmitRegistration(new ParticipantInput
            {
                FirstName = "Canlı",
                LastName = lastName,
                Title = "Müdür", Company = "Test A.Ş.", Location = "İstanbul / Şişli", Phone = "05320000000",
                Email = "canli.test@example.invalid"
            }).IsValid);
            session.SelectGender(Gender.Male);
            session.StartGame();
            yield return GameplayTestScene.WaitUntil(() => session.State == SessionState.Playing, 10);
            var position = suitcase.PlacementVolume.bounds.center;
            foreach (var item in pool.ActiveItems.Where(i => i.Definition.IsCorrect).ToArray())
            {
                GameplayTestScene.Teleport(item, position);
                Assert.IsTrue(suitcase.TryPlace(item), item.ProductId);
            }

            yield return GameplayTestScene.WaitUntil(() => session.State == SessionState.Finished || session.State == SessionState.SubmissionFailed, 60);
            Assert.AreEqual(SessionState.Finished, session.State, "the real backend accepted the submission");
            int score = session.Current.Result.Score;

            var board = backend.GetLeaderboardAsync(100);
            yield return new WaitUntil(() => board.IsCompleted);
            Assert.IsTrue(board.Result.Ok, "leaderboard: " + board.Result.Error?.Code);
            var row = board.Result.Value.FirstOrDefault(e => e.DisplayName != null && e.DisplayName.Contains("Canlı") && e.Score == score);
            Assert.IsNotNull(row, "the new result is on the shared leaderboard");
            Assert.Greater(row.Rank, 0);
            Debug.Log($"[MultiTravel] Live backend OK: {row.DisplayName} score {row.Score} rank {row.Rank}");
            session.ResetForNextParticipant();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var bootstrap = UnityEngine.Object.FindAnyObjectByType<AppBootstrap>();
            if (bootstrap != null)
            {
                UnityEngine.Object.Destroy(bootstrap.gameObject);
            }

            var empty = SceneManager.CreateScene("AfterLiveBackendTest" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            var main = SceneManager.GetSceneByName("Main");
            if (main.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(main);
            }

            yield return null;
            AppServices.Clear();
            AppBootstrap.DataRootOverride = null;
            try
            {
                if (dataRoot != null && Directory.Exists(dataRoot))
                {
                    Directory.Delete(dataRoot, true);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
