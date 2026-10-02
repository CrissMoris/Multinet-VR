using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MultiTravel.Core.Backend;
using MultiTravel.Core.Products;
using MultiTravel.Core.Services;
using MultiTravel.Core.Session;
using MultiTravel.Gameplay.Director;
using MultiTravel.Gameplay.Items;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MultiTravel.Tests.PlayMode
{
    /// <summary>
    /// ItemPool creation from prefabs and a full director-driven session followed by a reset (ARCHITECTURE.md §7, §10).
    /// </summary>
    public sealed class DirectorPlayModeTests
    {
        private GameplayTestScene scene;
        private GameObject prefabRoot;
        private GameObject template;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AppServices.Clear();
            scene = new GameplayTestScene();

            // A scene object used as "prefab": kept under an inactive root so it never registers with XRI itself.
            prefabRoot = scene.Track(new GameObject("PrefabTemplates"));
            prefabRoot.SetActive(false);
            template = new GameObject("ItemTemplate");
            template.transform.SetParent(prefabRoot.transform, false);
            var collider = template.AddComponent<BoxCollider>();
            collider.size = Vector3.one * 0.1f;
            template.AddComponent<ProductItem>();

            scene.DefinitionA.VisualPrefab = template;
            scene.DefinitionB.VisualPrefab = template;
            scene.DefinitionWrong.VisualPrefab = template;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (scene != null)
            {
                scene.Dispose();
                scene = null;
            }

            AppServices.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ItemPool_CreatesOneDisabledInstancePerProduct_AndSkipsMissingPrefabs()
        {
            var noPrefab = scene.Track(ScriptableObject.CreateInstance<ProductDefinition>());
            noPrefab.Id = "test-no-prefab";
            noPrefab.DisplayName = "Eksik";
            noPrefab.IsCorrect = false;
            var catalog = scene.Track(ScriptableObject.CreateInstance<ProductCatalog>());
            catalog.Products = new List<ProductDefinition> { scene.DefinitionA, scene.DefinitionB, scene.DefinitionWrong, noPrefab };

            var poolGo = scene.Track(new GameObject("PoolUnderTest"));
            var pool = poolGo.AddComponent<ItemPool>();
            pool.SetCatalog(catalog);

            LogAssert.Expect(LogType.Error, new Regex("test-no-prefab.*no VisualPrefab"));
            pool.EnsureCreated();
            pool.EnsureCreated(); // idempotent

            Assert.AreEqual(3, pool.AllItems.Count);
            var seen = new HashSet<ProductDefinition>();
            for (int i = 0; i < pool.AllItems.Count; i++)
            {
                var item = pool.AllItems[i];
                Assert.IsTrue(seen.Add(item.Definition), "one instance per definition");
                Assert.AreEqual(ProductItemState.Pooled, item.State);
                Assert.IsFalse(item.gameObject.activeSelf);
                Assert.AreSame(pool.ItemsRoot, item.transform.parent);
            }

            var set = new ProductSetResolver().Resolve(catalog, Gender.Male);
            LogAssert.Expect(LogType.Error, new Regex("test-no-prefab.*has no instance"));
            var active = pool.Activate(set);
            Assert.AreEqual(3, active.Count);
            for (int i = 0; i < active.Count; i++)
            {
                Assert.AreEqual(ProductItemState.Free, active[i].State);
                Assert.IsTrue(active[i].gameObject.activeSelf);
            }

            yield return null;
            pool.DeactivateAll();
            Assert.AreEqual(0, pool.ActiveItems.Count);
            for (int i = 0; i < pool.AllItems.Count; i++)
            {
                Assert.AreEqual(ProductItemState.Pooled, pool.AllItems[i].State);
            }

            Assert.AreEqual(3, pool.AllItems.Count, "no instance is created or destroyed by activation cycles");
        }

        [UnityTest]
        public IEnumerator Director_FullSession_ThenReset_LeavesNoState()
        {
            // Scene-level gameplay objects.
            var poolGo = scene.Track(new GameObject("DirectorPool"));
            var pool = poolGo.AddComponent<ItemPool>();
            pool.SetCatalog(scene.Catalog);
            scene.Suitcase.SetPool(pool);

            var layoutGo = scene.Track(new GameObject("SpawnLayout"));
            layoutGo.transform.position = GameplayTestScene.TableTop;
            var layout = layoutGo.AddComponent<SpawnSlotLayout>();
            var spawnSlots = new List<SpawnSlot>();
            for (int i = 0; i < 3; i++)
            {
                var slotGo = new GameObject("Spawn_" + i);
                slotGo.transform.SetParent(layoutGo.transform, false);
                slotGo.transform.localPosition = new Vector3(-0.6f + 0.6f * i, 0f, 0f);
                spawnSlots.Add(slotGo.AddComponent<SpawnSlot>());
            }

            layout.SetSlots(spawnSlots);

            var lockGo = scene.Track(new GameObject("InteractionLock"));
            var interactionLock = lockGo.AddComponent<InteractionLock>();
            interactionLock.SetPool(pool);

            var directorGo = scene.Track(new GameObject("GameplayDirector"));
            var director = directorGo.AddComponent<GameplayDirector>();
            director.Configure(scene.Catalog, pool, layout, scene.Suitcase, interactionLock, null);
            director.Bind(scene.Session, scene.Config, scene.Score, scene.Evaluator, new ProductSetResolver());
            Assert.IsTrue(director.IsBound);
            yield return null;

            Assert.IsTrue(interactionLock.IsLocked, "Welcome is locked");

            // Operator flow up to Loading; the director finishes Loading (countdown 0 → Playing).
            scene.Session.BeginRegistration();
            Assert.IsTrue(scene.Session.SubmitRegistration(GameplayTestScene.ValidInput()).IsValid);
            scene.Session.SelectGender(Gender.Female);
            scene.Session.StartGame();
            yield return GameplayTestScene.WaitUntil(() => scene.Session.State == SessionState.Playing, 3f);
            Assert.AreEqual(SessionState.Playing, scene.Session.State);
            Assert.IsFalse(interactionLock.IsLocked, "Playing unlocks interaction");
            Assert.AreEqual(3, pool.ActiveItems.Count);
            Assert.AreSame(director.ActiveSet, scene.Evaluator.Set, "the director configures the evaluator with the session set");
            Assert.AreEqual(2, scene.Evaluator.RequiredTotal);
            for (int i = 0; i < pool.ActiveItems.Count; i++)
            {
                Assert.AreEqual(ProductItemState.Free, pool.ActiveItems[i].State);
                Assert.IsTrue(pool.ActiveItems[i].Grab.enabled);
            }

            // Place both required items; the director completes on the next frame.
            Assert.IsTrue(scene.Suitcase.TryPlace(pool.Find(GameplayTestScene.RequiredA)));
            Assert.IsTrue(scene.Suitcase.TryPlace(pool.Find(GameplayTestScene.RequiredB)));
            yield return GameplayTestScene.WaitUntil(() => scene.Session.State == SessionState.Submitting, 2f);
            Assert.AreEqual(SessionState.Submitting, scene.Session.State);
            Assert.AreEqual(CompletionReason.RequiredItemsPlaced, scene.Session.Current.Result.Reason);
            Assert.AreEqual(20, scene.Session.Current.Result.Score);
            Assert.IsTrue(interactionLock.IsLocked, "interaction is locked after completion");
            Assert.IsFalse(scene.Suitcase.AcceptPlacements);
            Assert.IsFalse(pool.Find(GameplayTestScene.Wrong).Grab.enabled);

            scene.Session.OnSubmissionSucceeded(new SubmissionReceipt
            {
                ResultId = Guid.NewGuid(),
                ParticipantId = Guid.NewGuid(),
                Created = true,
                Rank = 1
            });
            Assert.AreEqual(SessionState.Finished, scene.Session.State);

            scene.Session.ResetForNextParticipant();
            yield return null;

            Assert.AreEqual(SessionState.Welcome, scene.Session.State);
            Assert.IsNull(scene.Session.Current);
            Assert.AreEqual(0, scene.Score.Score);
            Assert.AreEqual(0, scene.Score.CountedProductIds.Count);
            Assert.IsFalse(scene.Timer.IsRunning);
            Assert.AreEqual(0, scene.Timer.ElapsedMs);
            Assert.AreEqual(0, scene.Evaluator.RequiredPlacedCount);
            Assert.AreEqual(0, scene.Suitcase.PlacedCount);
            Assert.AreEqual(3, scene.Suitcase.FreeSlotCount);
            Assert.AreEqual(0, pool.ActiveItems.Count);
            for (int i = 0; i < pool.AllItems.Count; i++)
            {
                Assert.AreEqual(ProductItemState.Pooled, pool.AllItems[i].State);
                Assert.IsFalse(pool.AllItems[i].gameObject.activeSelf);
            }

            Assert.IsTrue(interactionLock.IsLocked);
            Assert.AreEqual(0, director.CountdownRemaining);
            Assert.IsNull(director.ActiveSet);
        }
    }
}
