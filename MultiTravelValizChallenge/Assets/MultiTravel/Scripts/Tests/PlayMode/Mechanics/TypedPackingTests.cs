using System.Collections;
using System.Collections.Generic;
using MultiTravel.Core.Products;
using MultiTravel.Core.Services;
using MultiTravel.Gameplay.Audio;
using MultiTravel.Gameplay.Items;
using MultiTravel.Gameplay.Suitcase;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MultiTravel.Tests.PlayMode.Mechanics
{
    /// <summary>Typed packing (OVERHAUL_PLAN §5: own kind → Flat → Top → nearest), landing events and stack height.</summary>
    public sealed class TypedPackingTests
    {
        private GameplayTestScene scene;
        private Dictionary<PackedKind, SuitcaseSlot> slots;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AppServices.Clear();
            scene = new GameplayTestScene();
            slots = new Dictionary<PackedKind, SuitcaseSlot>
            {
                { PackedKind.Flat, MechanicsFixtures.SuitcaseSlot(scene, PackedKind.Flat, new Vector3(-0.2f, 0.05f, 0f)) },
                { PackedKind.ShoeCorner, MechanicsFixtures.SuitcaseSlot(scene, PackedKind.ShoeCorner, new Vector3(-0.1f, 0.05f, 0f)) },
                { PackedKind.Organiser, MechanicsFixtures.SuitcaseSlot(scene, PackedKind.Organiser, new Vector3(0f, 0.05f, 0f)) },
                { PackedKind.Top, MechanicsFixtures.SuitcaseSlot(scene, PackedKind.Top, new Vector3(0.1f, 0.05f, 0f)) },
                { PackedKind.Upright, MechanicsFixtures.SuitcaseSlot(scene, PackedKind.Upright, new Vector3(0.2f, 0.05f, 0f)) },
            };
            scene.Suitcase.Configure(scene.Volume, new List<SuitcaseSlot>(slots.Values));
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

        private ProductItem Item(string id, PackedKind packed, int index, ProductCategory category = ProductCategory.Other)
        {
            var definition = MechanicsFixtures.Product(scene, id, false, DisplayZone.Leisure, packed, GripPreset.Dynamic, category);
            var item = scene.CreateItem(definition, scene.TableSpawn(index));
            GameplayTestScene.Teleport(item, scene.InsideVolume);
            return item;
        }

        private SuitcaseSlot SlotOf(ProductItem item)
        {
            Assert.IsTrue(scene.Suitcase.TryGetSlot(item, out var slot), item.ProductId + " has a slot");
            return slot;
        }

        [UnityTest]
        public IEnumerator TryPlace_PicksOwnKind_ThenFlat_ThenTop_ThenNearest()
        {
            var shoe = Item("shoe", PackedKind.ShoeCorner, 0, ProductCategory.Shoes);
            var ring = Item("ring", PackedKind.Organiser, 1, ProductCategory.Jewellery);
            var cardA = Item("card-a", PackedKind.LidPocket, 2);
            var cardB = Item("card-b", PackedKind.LidPocket, 3);
            var cardC = Item("card-c", PackedKind.LidPocket, 4);
            yield return null;

            Assert.IsTrue(scene.Suitcase.TryPlace(shoe));
            Assert.AreEqual(PackedKind.ShoeCorner, SlotOf(shoe).Kind);
            Assert.IsTrue(scene.Suitcase.TryPlace(ring));
            Assert.AreEqual(PackedKind.Organiser, SlotOf(ring).Kind);

            Assert.IsTrue(scene.Suitcase.TryPlace(cardA));
            Assert.AreEqual(PackedKind.Flat, SlotOf(cardA).Kind, "no lid-pocket slot → Flat");
            Assert.IsTrue(scene.Suitcase.TryPlace(cardB));
            Assert.AreEqual(PackedKind.Top, SlotOf(cardB).Kind, "Flat taken → Top");
            Assert.IsTrue(scene.Suitcase.TryPlace(cardC));
            Assert.AreEqual(PackedKind.Upright, SlotOf(cardC).Kind, "Flat and Top taken → nearest free slot");
            Assert.AreEqual(0, scene.Suitcase.FreeSlotCount);
        }

        [UnityTest]
        public IEnumerator Landing_RaisesItemLandedOnce_WithFoleyKind_AndRaisesStackHeight()
        {
            var shoe = Item("shoe", PackedKind.ShoeCorner, 0, ProductCategory.Shoes);
            var landed = new List<(ProductItem item, SoundKind kind, bool correct)>();
            scene.Suitcase.ItemLanded += (i, k, c) => landed.Add((i, k, c));
            float heightChanges = 0;
            scene.Suitcase.StackHeightChanged += h => heightChanges++;
            yield return null;

            Assert.AreEqual(0f, scene.Suitcase.StackHeight);
            Assert.IsTrue(scene.Suitcase.TryPlace(shoe));
            Assert.AreEqual(0, landed.Count, "landing is reported when the settle tween ends");
            Assert.IsTrue(scene.Suitcase.IsSettling(shoe));
            Assert.Greater(scene.Suitcase.StackHeight, 0.05f, "the 10 cm item raises the stack");
            Assert.Greater(heightChanges, 0);

            yield return GameplayTestScene.WaitUntil(() => landed.Count > 0, 2f);
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.AreEqual(1, landed.Count);
            Assert.AreSame(shoe, landed[0].item);
            Assert.AreEqual(SoundKind.Leather, landed[0].kind);
            Assert.IsFalse(landed[0].correct);
            Assert.Less(Vector3.Distance(SlotOf(shoe).PoseFor(shoe).position, shoe.transform.position), 0.005f, "settled on its slot");

            Assert.IsTrue(scene.Suitcase.Remove(shoe));
            Assert.AreEqual(0f, scene.Suitcase.StackHeight);
        }

        [Test]
        public void EaseOutBack_StartsAtZero_OvershootsAndEndsAtOne()
        {
            Assert.AreEqual(0f, SuitcaseController.EaseOutBack(0f), 1e-5f);
            Assert.AreEqual(1f, SuitcaseController.EaseOutBack(1f), 1e-5f);
            Assert.Greater(SuitcaseController.EaseOutBack(0.75f), 1f, "ease-out-back overshoots before settling");
        }
    }
}
