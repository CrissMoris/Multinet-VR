using System.Collections;
using MultiTravel.Core.Completion;
using MultiTravel.Core.Scoring;
using MultiTravel.Core.Services;
using MultiTravel.Core.Session;
using MultiTravel.Gameplay.Items;
using MultiTravel.Gameplay.Suitcase;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MultiTravel.Tests.PlayMode
{
    /// <summary>
    /// Suitcase, settle and recovery behaviour in a code-built scene (ARCHITECTURE.md §10). No HMD is required;
    /// every wait is bounded by a real-time deadline.
    /// </summary>
    public sealed class SuitcasePlayModeTests
    {
        private GameplayTestScene scene;
        private int placedEvents;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AppServices.Clear();
            scene = new GameplayTestScene();
            placedEvents = 0;
            scene.Score.Changed += CountPlacements;
            yield return null; // let Start() run on every component
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (scene != null)
            {
                scene.Score.Changed -= CountPlacements;
                scene.Dispose();
                scene = null;
            }

            AppServices.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator XriGrabAndReleaseInsideSuitcase_PlacesAndCanBeGrabbedAgain()
        {
            var item=scene.CreateItem(scene.DefinitionA,scene.TableSpawn(0));
            scene.Suitcase.Watch(item);
            var hand=scene.Track(new GameObject("Test physical interactor"));
            var trigger=hand.AddComponent<SphereCollider>();trigger.isTrigger=true;trigger.radius=.05f;
            var interactor=hand.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor>();
            interactor.interactionManager=scene.Manager;
            yield return null;
            scene.Manager.SelectEnter((UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor)interactor,item.Grab);
            Assert.That(item.State,Is.EqualTo(ProductItemState.Held));
            GameplayTestScene.Teleport(item,scene.InsideVolume);
            scene.Manager.SelectExit((UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor)interactor,item.Grab);
            Assert.That(item.State,Is.EqualTo(ProductItemState.Placed));
            Assert.That(scene.Score.Score,Is.EqualTo(10));
            scene.Manager.SelectEnter((UnityEngine.XR.Interaction.Toolkit.Interactors.IXRSelectInteractor)interactor,item.Grab);
            Assert.That(item.State,Is.EqualTo(ProductItemState.Held));
            Assert.That(scene.Score.Score,Is.Zero);
            scene.Manager.CancelInteractableSelection((UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable)item.Grab);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TryPlace_ScoresOnce_EvenUnderRepeatedCalls()
        {
            var item = scene.CreateItem(scene.DefinitionA, scene.TableSpawn(0));
            GameplayTestScene.Teleport(item, scene.InsideVolume);

            Assert.IsTrue(scene.Suitcase.TryPlace(item), "first placement must succeed");
            for (int i = 0; i < 5; i++)
            {
                Assert.IsFalse(scene.Suitcase.TryPlace(item), "trigger spam must never place twice");
            }

            yield return null;
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(10, scene.Score.Score);
            Assert.AreEqual(1, placedEvents);
            Assert.AreEqual(ProductItemState.Placed, item.State);
            Assert.IsTrue(item.Body.isKinematic, "placed items are kinematic");
            Assert.AreEqual(1, scene.Suitcase.PlacedCount);
            Assert.AreEqual(2, scene.Suitcase.FreeSlotCount);
            Assert.AreEqual(1, scene.Evaluator.RequiredPlacedCount);
        }

        [UnityTest]
        public IEnumerator PackingColumn_StacksBottomUp_AndCompactsWhenLowerItemIsRemoved()
        {
            // Turn the three slots into one packing column.
            scene.Slots[1].SetBelow(scene.Slots[0]);
            scene.Slots[2].SetBelow(scene.Slots[1]);
            var a = scene.CreateItem(scene.DefinitionA, scene.TableSpawn(0));
            var b = scene.CreateItem(scene.DefinitionB, scene.TableSpawn(1));
            var wrong = scene.CreateItem(scene.DefinitionWrong, scene.TableSpawn(2));
            foreach (var item in new[] { a, b, wrong })
            {
                GameplayTestScene.Teleport(item, scene.InsideVolume);
                Assert.IsTrue(scene.Suitcase.TryPlace(item));
            }

            yield return new WaitForSecondsRealtime(0.5f);
            Assert.IsTrue(scene.Suitcase.TryGetSlot(a, out var slotA) && slotA == scene.Slots[0], "first item takes the column bottom");
            Assert.IsTrue(scene.Suitcase.TryGetSlot(b, out var slotB) && slotB == scene.Slots[1]);
            Assert.IsTrue(scene.Suitcase.TryGetSlot(wrong, out var slotW) && slotW == scene.Slots[2]);
            Assert.Greater(b.transform.position.y, a.transform.position.y, "second item rests on top of the first");
            Assert.Greater(wrong.transform.position.y, b.transform.position.y);
            float bHeightBefore = b.transform.position.y;

            Assert.IsTrue(scene.Suitcase.Remove(a));
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.IsTrue(scene.Suitcase.TryGetSlot(b, out slotB) && slotB == scene.Slots[0], "items above drop into the freed slot");
            Assert.IsTrue(scene.Suitcase.TryGetSlot(wrong, out slotW) && slotW == scene.Slots[1]);
            Assert.Less(b.transform.position.y, bHeightBefore, "the stack settles down after the removal");
            Assert.IsFalse(scene.Slots[2].IsOccupied);
            Assert.AreEqual(5, scene.Score.Score, "A reverted (+10 -10), B +10, wrong -5");
        }

        [UnityTest]
        public IEnumerator RePlace_AlreadyPlacedItem_IsNoOp()
        {
            var item = scene.CreateItem(scene.DefinitionWrong, scene.TableSpawn(0));
            Assert.IsTrue(scene.Suitcase.TryPlace(item));
            Assert.AreEqual(-5, scene.Score.Score);

            yield return new WaitForSecondsRealtime(0.3f); // tween finished

            Assert.IsFalse(scene.Suitcase.TryPlace(item));
            Assert.AreEqual(-5, scene.Score.Score);
            Assert.AreEqual(1, placedEvents);
            Assert.AreEqual(1, scene.Suitcase.PlacedCount);
            Assert.IsTrue(scene.Suitcase.TryGetSlot(item, out var slot));
            Assert.IsTrue(slot.IsOccupied);
            Assert.AreSame(item, slot.Occupant);
            Assert.Less(Vector3.Distance(slot.PoseFor(item).position, item.transform.position), 0.01f, "item tweened into its slot");
        }

        [UnityTest]
        public IEnumerator Remove_RevertsScore_AndFreesSlot()
        {
            var item = scene.CreateItem(scene.DefinitionA, scene.TableSpawn(0));
            ProductItem removedItem = null;
            ScoreChange removedChange = default;
            scene.Suitcase.ItemRemoved += (i, c) =>
            {
                removedItem = i;
                removedChange = c;
            };

            Assert.IsTrue(scene.Suitcase.TryPlace(item));
            Assert.AreEqual(10, scene.Score.Score);
            yield return null;

            Assert.IsTrue(scene.Suitcase.Remove(item));
            Assert.AreEqual(0, scene.Score.Score);
            Assert.AreSame(item, removedItem);
            Assert.AreEqual(-10, removedChange.Delta);
            Assert.AreEqual(ProductItemState.Free, item.State);
            Assert.IsFalse(item.Body.isKinematic, "removed items are dynamic again");
            Assert.AreEqual(0, scene.Suitcase.PlacedCount);
            Assert.AreEqual(3, scene.Suitcase.FreeSlotCount);
            Assert.AreEqual(0, scene.Evaluator.RequiredPlacedCount);
            Assert.IsFalse(scene.Suitcase.Remove(item), "second removal is rejected");

            // Re-placing after a revert counts again exactly once.
            Assert.IsTrue(scene.Suitcase.TryPlace(item));
            Assert.AreEqual(10, scene.Score.Score);
        }

        [UnityTest]
        public IEnumerator BothRequiredPlaced_CompletesWithRequiredItemsPlaced()
        {
            scene.EnterPlaying();
            Assert.AreEqual(SessionState.Playing, scene.Session.State);

            var a = scene.CreateItem(scene.DefinitionA, scene.TableSpawn(0));
            var b = scene.CreateItem(scene.DefinitionB, scene.TableSpawn(1));
            var wrong = scene.CreateItem(scene.DefinitionWrong, scene.TableSpawn(2));
            yield return null;

            Assert.IsTrue(scene.Suitcase.TryPlace(wrong));
            Assert.IsTrue(scene.Suitcase.TryPlace(a));
            Assert.IsFalse(scene.Evaluator.TryGetCompletion(out _), "one required item is still missing");
            Assert.IsFalse(scene.Session.TryCompleteIfDue());

            scene.Clock.Advance(12.34);
            Assert.IsTrue(scene.Suitcase.TryPlace(b));
            Assert.IsTrue(scene.Evaluator.TryGetCompletion(out var reason));
            Assert.AreEqual(CompletionReason.RequiredItemsPlaced, reason);

            Assert.IsTrue(scene.Session.TryCompleteIfDue());
            Assert.AreEqual(SessionState.Submitting, scene.Session.State);
            var result = scene.Session.Current.Result;
            Assert.IsNotNull(result);
            Assert.AreEqual(CompletionReason.RequiredItemsPlaced, result.Reason);
            Assert.AreEqual(15, result.Score);
            Assert.AreEqual(2, result.CorrectCount);
            Assert.AreEqual(1, result.IncorrectCount);
            Assert.AreEqual(2, result.RequiredTotal);
            Assert.AreEqual(12340, result.CompletionMs);
        }

        [UnityTest]
        public IEnumerator Clear_EmptiesSuitcase_AndPoolResetLeavesNoState()
        {
            var a = scene.CreateItem(scene.DefinitionA, scene.TableSpawn(0));
            var wrong = scene.CreateItem(scene.DefinitionWrong, scene.TableSpawn(1));
            Assert.IsTrue(scene.Suitcase.TryPlace(a));
            Assert.IsTrue(scene.Suitcase.TryPlace(wrong));
            yield return null;

            scene.Suitcase.Clear();

            Assert.AreEqual(0, scene.Suitcase.PlacedCount);
            Assert.AreEqual(3, scene.Suitcase.FreeSlotCount);
            Assert.AreEqual(ProductItemState.Free, a.State);
            Assert.AreEqual(ProductItemState.Free, wrong.State);
            Assert.AreEqual(0, scene.Evaluator.RequiredPlacedCount);
            Assert.AreEqual(5, scene.Score.Score, "Clear does not touch the score; the session reset clears it");

            scene.Score.Reset();
            scene.Pool.DeactivateAll();
            Assert.AreEqual(0, scene.Score.Score);
            Assert.AreEqual(ProductItemState.Pooled, a.State);
            Assert.AreEqual(ProductItemState.Pooled, wrong.State);
            Assert.IsFalse(a.gameObject.activeSelf);
            Assert.IsFalse(wrong.gameObject.activeSelf);
            Assert.AreEqual(0, scene.Pool.ActiveItems.Count);
        }

        [UnityTest]
        public IEnumerator SettleWatcher_PlacesItemRestingInsideVolume()
        {
            var watcherGo = scene.Track(new GameObject("SettleWatcher"));
            var watcher = watcherGo.AddComponent<SettleWatcher>();
            watcher.Configure(scene.Suitcase, scene.Pool);

            var item = scene.CreateItem(scene.DefinitionB, scene.TableSpawn(0));
            yield return null;

            item.Body.useGravity = false;
            GameplayTestScene.Teleport(item, scene.InsideVolume);
            Assert.AreEqual(ProductItemState.Free, item.State);

            yield return GameplayTestScene.WaitUntil(() => item.State == ProductItemState.Placed, 3f);

            Assert.AreEqual(ProductItemState.Placed, item.State, "SettleWatcher must place an unheld item at rest inside the volume");
            Assert.AreEqual(10, scene.Score.Score);
            Assert.AreEqual(1, placedEvents);
        }

        [UnityTest]
        public IEnumerator SettleWatcher_IgnoresItemsWhilePlacementsAreClosed()
        {
            var watcherGo = scene.Track(new GameObject("SettleWatcher"));
            var watcher = watcherGo.AddComponent<SettleWatcher>();
            watcher.Configure(scene.Suitcase, scene.Pool);
            scene.Suitcase.AcceptPlacements = false;

            var item = scene.CreateItem(scene.DefinitionB, scene.TableSpawn(0));
            yield return null;
            item.Body.useGravity = false;
            GameplayTestScene.Teleport(item, scene.InsideVolume);

            yield return new WaitForSecondsRealtime(0.6f);

            Assert.AreEqual(ProductItemState.Free, item.State);
            Assert.AreEqual(0, scene.Score.Score);
        }

        [UnityTest]
        public IEnumerator Recovery_ReturnsItemBelowFloorToSpawn()
        {
            var recoveryGo = scene.Track(new GameObject("ItemRecovery"));
            var recovery = recoveryGo.AddComponent<ItemRecoveryService>();
            recovery.SetPool(scene.Pool);
            recovery.Configure(0f, new Bounds(new Vector3(0f, 1.5f, 0f), new Vector3(20f, 5f, 20f)));
            int recovered = 0;
            recovery.ItemRecovered += _ => recovered++;

            var spawn = scene.TableSpawn(1);
            var item = scene.CreateItem(scene.DefinitionA, spawn);
            yield return null;

            GameplayTestScene.Teleport(item, new Vector3(spawn.x, -5f, spawn.z));
            Assert.Less(item.AnchorPosition.y, -0.2f);

            yield return GameplayTestScene.WaitUntil(() => recovered > 0 && !item.IsReturning, 3f);

            Assert.Greater(recovered, 0, "the recovery service must detect the item below the floor");
            Assert.IsFalse(item.IsReturning);
            Assert.AreEqual(ProductItemState.Free, item.State);
            Assert.Less(Vector3.Distance(spawn, item.transform.position), 0.2f, "item is back at its spawn slot");
            Assert.AreEqual(Vector3.one, item.transform.localScale, "return animation restored the scale");
        }

        private void CountPlacements(ScoreChange change)
        {
            if (change.Reason == ScoreChangeReason.Placed)
            {
                placedEvents++;
            }
        }
    }
}
