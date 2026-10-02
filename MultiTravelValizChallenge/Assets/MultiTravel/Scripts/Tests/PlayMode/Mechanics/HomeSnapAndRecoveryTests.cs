using System.Collections;
using MultiTravel.Core.Services;
using MultiTravel.Gameplay.Items;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace MultiTravel.Tests.PlayMode.Mechanics
{
    /// <summary>
    /// Release behaviour (OVERHAUL_PLAN §5): home-snap within 0.22 m, release-velocity clamp, recovery after 1.2 s resting away
    /// from home (but never inside the suitcase).
    /// </summary>
    public sealed class HomeSnapAndRecoveryTests
    {
        private GameplayTestScene scene;
        private XRDirectInteractor hand;
        private ProductItem item;
        private Vector3 home;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AppServices.Clear();
            scene = new GameplayTestScene();
            hand = MechanicsFixtures.CreateHand(scene);
            home = scene.TableSpawn(0) - new Vector3(0f, 0.05f, 0f); // 10 cm cube resting on the table top
            item = scene.CreateItem(scene.DefinitionA, home);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (item != null && item.IsHeld)
            {
                item.ForceRelease();
            }

            scene?.Dispose();
            scene = null;
            AppServices.Clear();
            yield return null;
        }

        private ItemRecoveryService Recovery()
        {
            var go = scene.Track(new GameObject("Recovery"));
            var recovery = go.AddComponent<ItemRecoveryService>();
            recovery.SetPool(scene.Pool);
            recovery.SetSuitcase(scene.Suitcase);
            recovery.Configure(0f, new Bounds(new Vector3(0f, 1.5f, 0f), new Vector3(20f, 5f, 20f)));
            return recovery;
        }

        [UnityTest]
        public IEnumerator Item_StartsDockedAtHome()
        {
            Assert.AreEqual(ProductItemState.Free, item.State);
            Assert.IsTrue(item.IsDocked);
            Assert.IsTrue(item.Body.isKinematic, "docked items rest kinematically (hangers, shelves)");
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.Less(item.DistanceFromHome(), 1e-3f, "a docked item never drifts");
        }

        [UnityTest]
        public IEnumerator ReleaseNearHome_TweensBackIntoTheSlot()
        {
            int snaps = 0;
            item.HomeSnapStarted += _ => snaps++;
            MechanicsFixtures.Grab(scene, hand, item);
            Assert.IsFalse(item.IsDocked);
            MechanicsFixtures.ReleaseAt(scene, hand, item, home + new Vector3(0.15f, 0.08f, 0f));

            Assert.AreEqual(ProductItemState.Free, item.State);
            Assert.AreEqual(1, snaps);
            Assert.IsTrue(item.IsReturning, "home-snap tween running");
            yield return GameplayTestScene.WaitUntil(() => !item.IsReturning, 2f);

            Assert.Less(item.DistanceFromHome(), 1e-3f);
            Assert.IsTrue(item.IsDocked);
            Assert.IsTrue(item.Body.isKinematic);
        }

        [UnityTest]
        public IEnumerator ReleaseFarFromHome_DropsDynamically_WithoutSnap()
        {
            int snaps = 0;
            item.HomeSnapStarted += _ => snaps++;
            MechanicsFixtures.Grab(scene, hand, item);
            MechanicsFixtures.ReleaseAt(scene, hand, item, home + new Vector3(0.5f, 0.1f, 0f));

            Assert.AreEqual(0, snaps);
            Assert.IsFalse(item.IsReturning);
            Assert.IsFalse(item.Body.isKinematic, "released away from home: dynamic");
            Assert.IsFalse(item.IsDocked);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReleaseVelocity_IsClampedTo1Point5()
        {
            MechanicsFixtures.Grab(scene, hand, item);
            // Swing the hand fast (≈ 6 m/s); the held item follows it.
            for (int i = 0; i < 12; i++)
            {
                hand.transform.position += new Vector3(0.1f, 0f, 0f);
                yield return null;
            }

            scene.Manager.SelectExit((UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor)hand,
                (UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable)item.Grab);
            Assert.AreEqual(ProductItemState.Free, item.State);
            Assert.LessOrEqual(item.Body.linearVelocity.magnitude, ProductItem.MaxReleaseSpeed + 1e-3f);
        }

        [UnityTest]
        public IEnumerator RestingAwayFromHome_IsRecoveredAfter1Point2Seconds()
        {
            var recovery = Recovery();
            Assert.AreEqual(1.2f, recovery.RestSeconds, 1e-4f);
            float recoveredAt = -1f;
            recovery.ItemRecovered += _ => recoveredAt = Time.realtimeSinceStartup;

            MechanicsFixtures.Grab(scene, hand, item);
            MechanicsFixtures.ReleaseAt(scene, hand, item, home + new Vector3(0.5f, 0.02f, 0f));
            float releasedAt = Time.realtimeSinceStartup;

            yield return GameplayTestScene.WaitUntil(() => recoveredAt > 0f && !item.IsReturning, 6f);

            Assert.Greater(recoveredAt, 0f, "an item resting on the table away from its slot is returned");
            Assert.GreaterOrEqual(recoveredAt - releasedAt, 1.1f, "not before ~1.2 s of rest");
            Assert.Less(item.DistanceFromHome(), 1e-3f);
            Assert.IsTrue(item.IsDocked);
            Assert.AreEqual(item.BaseLocalScale, item.transform.localScale);
        }

        [UnityTest]
        public IEnumerator RestingInsideTheSuitcase_IsNotRecovered()
        {
            var recovery = Recovery();
            int recovered = 0;
            recovery.ItemRecovered += _ => recovered++;
            scene.Suitcase.AcceptPlacements = false; // keep it Free inside the volume

            MechanicsFixtures.Grab(scene, hand, item);
            MechanicsFixtures.ReleaseAt(scene, hand, item, scene.InsideVolume);
            yield return new WaitForSecondsRealtime(2.2f);

            Assert.AreEqual(0, recovered);
            Assert.AreEqual(ProductItemState.Free, item.State);
            Assert.IsTrue(scene.Suitcase.IsInsideVolume(item));
        }

        [UnityTest]
        public IEnumerator BelowTheFloor_IsRecoveredImmediately()
        {
            var recovery = Recovery();
            MechanicsFixtures.Grab(scene, hand, item);
            MechanicsFixtures.ReleaseAt(scene, hand, item, new Vector3(home.x, -3f, home.z));
            Assert.AreEqual(1, recovery.Tick(0.2f), "one tick is enough below the floor");
            yield return GameplayTestScene.WaitUntil(() => !item.IsReturning, 2f);
            Assert.Less(item.DistanceFromHome(), 1e-3f);
        }
    }
}
