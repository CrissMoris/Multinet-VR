using System;
using System.Collections;
using System.Collections.Generic;
using MultiTravel.Core.Backend;
using MultiTravel.Core.Services;
using MultiTravel.Core.Session;
using MultiTravel.Gameplay.Suitcase;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MultiTravel.Tests.PlayMode.Mechanics
{
    /// <summary>Lid closes on Completed and reopens on reset (OVERHAUL_PLAN §5: <c>SuitcaseLid</c>); straps follow the stack.</summary>
    public sealed class SuitcaseLidTests
    {
        private GameplayTestScene scene;
        private SuitcaseLid lid;
        private Transform pivot;
        private Quaternion openRotation;
        private readonly List<string> events = new List<string>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AppServices.Clear();
            events.Clear();
            scene = new GameplayTestScene();
            pivot = new GameObject(SuitcaseLid.PivotNodeName).transform;
            pivot.SetParent(scene.Suitcase.transform, false);
            pivot.localPosition = new Vector3(0f, 0.3f, 0.2f);
            pivot.localRotation = Quaternion.Euler(10f, 0f, 0f);
            openRotation = pivot.localRotation;
            var lidMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.Destroy(lidMesh.GetComponent<Collider>());
            lidMesh.name = "Lid";
            lidMesh.transform.SetParent(pivot, false);
            lidMesh.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            lidMesh.transform.localScale = new Vector3(0.6f, 0.4f, 0.05f);

            lid = scene.Suitcase.gameObject.AddComponent<SuitcaseLid>();
            lid.SetDurations(0.25f, 0.15f, 0.02f);
            lid.Bind(scene.Session);
            lid.Closing += () => events.Add("closing");
            lid.LatchClicked += i => events.Add("latch" + i);
            lid.Closed += () => events.Add("closed");
            lid.Opening += () => events.Add("opening");
            lid.Opened += () => events.Add("opened");
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
        public IEnumerator Lid_ClosesOnCompleted_WithTwoLatches_AndReopensOnReset()
        {
            Assert.AreSame(pivot, lid.Pivot, "pivot found by its art node name");
            Assert.AreEqual(LidState.Open, lid.State);

            scene.EnterPlaying();
            var a = scene.CreateItem(scene.DefinitionA, scene.TableSpawn(0));
            var b = scene.CreateItem(scene.DefinitionB, scene.TableSpawn(1));
            yield return null;
            Assert.IsTrue(scene.Suitcase.TryPlace(a));
            Assert.IsTrue(scene.Suitcase.TryPlace(b));
            Assert.AreEqual(LidState.Open, lid.State, "placing items never closes the lid");

            Assert.IsTrue(scene.Session.TryCompleteIfDue());
            Assert.AreEqual(LidState.Closing, lid.State);
            yield return GameplayTestScene.WaitUntil(() => lid.State == LidState.Closed, 3f);

            Assert.AreEqual(LidState.Closed, lid.State);
            CollectionAssert.AreEqual(new[] { "closing", "latch0", "latch1", "closed" }, events);
            Assert.AreEqual(-100f, lid.CurrentAngle, 1e-3f);
            Assert.Less(Quaternion.Angle(openRotation * Quaternion.Euler(-100f, 0f, 0f), pivot.localRotation), 0.5f);

            scene.Session.OnSubmissionSucceeded(new SubmissionReceipt { ResultId = Guid.NewGuid(), ParticipantId = Guid.NewGuid(), Created = true, Rank = 1 });
            scene.Session.ResetForNextParticipant();
            Assert.AreEqual(LidState.Opening, lid.State);
            yield return GameplayTestScene.WaitUntil(() => lid.State == LidState.Open, 3f);

            Assert.AreEqual(LidState.Open, lid.State);
            CollectionAssert.AreEqual(new[] { "closing", "latch0", "latch1", "closed", "opening", "opened" }, events);
            Assert.AreEqual(0f, lid.CurrentAngle, 1e-3f);
            Assert.Less(Quaternion.Angle(openRotation, pivot.localRotation), 0.5f);
        }

        [UnityTest]
        public IEnumerator Lid_StaysOpen_WhenTheSessionIsAbandoned()
        {
            scene.EnterPlaying();
            scene.Session.AbandonSession();
            yield return new WaitForSecondsRealtime(0.4f);
            Assert.AreEqual(LidState.Open, lid.State);
            Assert.IsEmpty(events);
        }

        [UnityTest]
        public IEnumerator StrapLift_RisesWithTheStack_AndDropsWhenEmptied()
        {
            var straps = new GameObject(StrapLift.StrapsNodeName).transform;
            straps.SetParent(scene.Suitcase.transform, false);
            straps.localPosition = new Vector3(0f, 0.2f, 0f);
            var lift = scene.Suitcase.gameObject.AddComponent<StrapLift>();
            lift.Configure(scene.Suitcase, straps, 0.02f, 0.12f);
            var rest = straps.localPosition;

            var item = scene.CreateItem(scene.DefinitionWrong, scene.TableSpawn(0));
            yield return null;
            Assert.IsTrue(scene.Suitcase.TryPlace(item));
            Assert.Greater(lift.TargetLift, 0.05f);
            yield return new WaitForSecondsRealtime(1f);
            Assert.Greater(straps.localPosition.y, rest.y + 0.04f, "straps rose with the 10 cm stack");

            scene.Suitcase.Clear();
            yield return new WaitForSecondsRealtime(1f);
            Assert.AreEqual(0f, lift.TargetLift);
            Assert.AreEqual(rest.y, straps.localPosition.y, 0.005f);
        }
    }
}
