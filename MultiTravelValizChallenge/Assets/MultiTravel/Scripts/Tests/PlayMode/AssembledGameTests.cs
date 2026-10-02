using System.Collections;
using System.Linq;
using MultiTravel.Core.Completion;
using MultiTravel.Core.Scoring;
using MultiTravel.Core.Services;
using MultiTravel.Core.Session;
using MultiTravel.Core.Timing;
using MultiTravel.Gameplay;
using MultiTravel.Gameplay.Director;
using MultiTravel.Gameplay.Items;
using MultiTravel.Gameplay.Suitcase;
using MultiTravel.Operator;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MultiTravel.Tests.PlayMode
{
    public class AssembledGameTests
    {
        private string dataRoot;
        private RecordingBackend backend;

        [UnitySetUp]
        public IEnumerator Isolate()
        {
            // Never touch the real outbox or the configured event backend from an automated run.
            dataRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mt-assembled-test-" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dataRoot);
            backend = new RecordingBackend();
            AppBootstrap.DataRootOverride = dataRoot;
            AppBootstrap.BackendFactoryOverride = _ => backend;
            yield return null;
        }

        [UnityTest]
        public IEnumerator GeneratedScenes_PlayBothGenders_ScoreCompleteAndReset()
        {
            yield return SceneManager.LoadSceneAsync("Bootstrap");
            yield return GameplayTestScene.WaitUntil(()=>Object.FindAnyObjectByType<OperatorScreen>()?.IsBound==true,15);
            var screen=Object.FindAnyObjectByType<OperatorScreen>();
            Assert.That(screen,Is.Not.Null);
            Assert.That(screen.IsBound,Is.True);
            var session=AppServices.Get<SessionController>();
            var score=AppServices.Get<ScoreService>();
            var timer=AppServices.Get<GameTimer>();
            var pool=Object.FindAnyObjectByType<ItemPool>();
            var suitcase=Object.FindAnyObjectByType<SuitcaseController>();
            var director=Object.FindAnyObjectByType<GameplayDirector>();
            var layout=Object.FindAnyObjectByType<SpawnSlotLayout>();
            yield return null;
            var rig=Object.FindAnyObjectByType<MultiTravel.Gameplay.Xr.XrRigController>();
            Assert.That(rig,Is.Not.Null);
            Assert.That(rig.FarGrabDisabled,Is.True,"products must be grabbed by reaching, not by ray");
            foreach(var nearFar in Object.FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Interactors.NearFarInteractor>(FindObjectsInactive.Include))
                Assert.That(nearFar.enableFarCasting,Is.False,nearFar.name);
            Assert.That(Object.FindAnyObjectByType<MultiTravel.Gameplay.Feedback.ProductNameTags>(),Is.Not.Null);
            foreach(var gender in new[]{Gender.Female,Gender.Male})
            {
                Assert.That(session.State,Is.EqualTo(SessionState.Welcome));
                session.BeginRegistration();
                Assert.That(session.SubmitRegistration(new ParticipantInput()).IsValid,Is.False);
                Assert.That(session.State,Is.EqualTo(SessionState.Registration));
                Assert.That(session.SubmitRegistration(new ParticipantInput {FirstName="Integration",LastName="Test",Phone="05320000000",Email="integration@example.invalid"}).IsValid,Is.True);
                session.SelectGender(gender);
                session.StartGame();
                yield return GameplayTestScene.WaitUntil(()=>session.State==SessionState.Playing,10);
                Assert.That(session.State,Is.EqualTo(SessionState.Playing));
                Assert.That(timer.IsRunning,Is.True);
                Assert.That(pool.ActiveItems.Count,Is.EqualTo(director.ActiveSet.Items.Count));
                Assert.That(layout.Slots.Count,Is.GreaterThanOrEqualTo(pool.ActiveItems.Count));
                foreach(var item in pool.ActiveItems)
                {
                    Assert.That(item.Grab.enabled,Is.True,item.ProductId);
                    Assert.That(item.HasSpawnPose,Is.True,item.ProductId);
                    Assert.That(item.GetComponent<Collider>(),Is.Not.Null,item.ProductId);
                    Assert.That(item.GetComponentsInChildren<MeshRenderer>().Length,Is.GreaterThan(0),item.ProductId);
                    var mask=gender==Gender.Female?MultiTravel.Core.Products.GenderAvailability.Female:MultiTravel.Core.Products.GenderAvailability.Male;
                    Assert.That((item.Definition.Availability & mask)!=0,Is.True,item.ProductId);
                }
                var wrong=pool.ActiveItems.First(i=>!i.Definition.IsCorrect);
                var position=suitcase.PlacementVolume.bounds.center;
                GameplayTestScene.Teleport(wrong,position);
                Assert.That(suitcase.TryPlace(wrong),Is.True);
                Assert.That(score.Score,Is.LessThan(0));
                int negative=score.Score;
                Assert.That(suitcase.TryPlace(wrong),Is.False);
                Assert.That(score.Score,Is.EqualTo(negative));
                Assert.That(suitcase.Remove(wrong),Is.True);
                Assert.That(score.Score,Is.Zero);
                wrong.ReturnToSpawn(false);
                foreach(var item in pool.ActiveItems.Where(i=>i.Definition.IsCorrect).ToArray())
                {
                    GameplayTestScene.Teleport(item,position);
                    Assert.That(suitcase.TryPlace(item),Is.True,item.ProductId);
                }
                yield return GameplayTestScene.WaitUntil(()=>session.State!=SessionState.Playing,2);
                Assert.That(session.Current.Result,Is.Not.Null);
                Assert.That(session.Current.Result.Score,Is.GreaterThan(0));
                Assert.That(timer.IsRunning,Is.False);
                long stopped=timer.ElapsedMs;
                yield return null;
                Assert.That(timer.ElapsedMs,Is.EqualTo(stopped));
                yield return GameplayTestScene.WaitUntil(()=>session.State==SessionState.Finished || session.State==SessionState.SubmissionFailed,120);
                Assert.That(session.State,Is.EqualTo(SessionState.Finished),"the isolated backend accepts every submission");
                Assert.That(backend.Submissions.Count(p=>p.Gender==(gender==Gender.Female?"female":"male")),Is.EqualTo(1),"exactly one submission per session");
                session.ResetForNextParticipant();
                yield return null;
                Assert.That(session.Current,Is.Null);
                Assert.That(score.Score,Is.Zero);
                Assert.That(timer.ElapsedMs,Is.Zero);
                Assert.That(pool.ActiveItems.Count,Is.Zero);
                Assert.That(AppServices.Get<CompletionEvaluator>().RequiredPlacedCount,Is.Zero);
            }
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            var bootstrap=Object.FindAnyObjectByType<AppBootstrap>();
            if(bootstrap!=null)Object.Destroy(bootstrap.gameObject);
            var empty=SceneManager.CreateScene("AfterAssembledGameTest");
            SceneManager.SetActiveScene(empty);
            var main=SceneManager.GetSceneByName("Main");
            if(main.isLoaded)yield return SceneManager.UnloadSceneAsync(main);
            yield return null;
            AppServices.Clear();
            AppBootstrap.DataRootOverride = null;
            AppBootstrap.BackendFactoryOverride = null;
            try
            {
                if (dataRoot != null && System.IO.Directory.Exists(dataRoot))
                {
                    System.IO.Directory.Delete(dataRoot, true);
                }
            }
            catch (System.IO.IOException)
            {
                // a locked temp file must not fail the run; the OS cleans the temp folder
            }
        }
    }
}
