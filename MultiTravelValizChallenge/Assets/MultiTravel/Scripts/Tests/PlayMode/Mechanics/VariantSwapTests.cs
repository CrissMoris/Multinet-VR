using System.Collections;
using MultiTravel.Core.Products;
using MultiTravel.Core.Services;
using MultiTravel.Gameplay.Audio;
using MultiTravel.Gameplay.Items;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MultiTravel.Tests.PlayMode.Mechanics
{
    /// <summary>Hanging ↔ folded visual swap on place / remove / return (OVERHAUL_PLAN §5: <c>ItemVisualVariant</c>).</summary>
    public sealed class VariantSwapTests
    {
        private GameplayTestScene scene;
        private ProductItem garment;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AppServices.Clear();
            scene = new GameplayTestScene();
            var definition = MechanicsFixtures.Product(scene, "test-shirt", true, DisplayZone.Hanging, PackedKind.Flat, GripPreset.Hanger,
                ProductCategory.Clothing, true);
            garment = MechanicsFixtures.CreateVariantItem(scene, definition, new Vector3(-0.5f, 1.4f, 1.5f));
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
        public IEnumerator Place_SwapsToFoldedHalfWay_RemoveKeepsFolded_ReturnHomeRestoresHanging()
        {
            var variant = garment.Variant;
            Assert.IsNotNull(variant);
            Assert.AreEqual(ItemVariant.Hanging, variant.Current);
            Assert.IsTrue(variant.HangingVisual.gameObject.activeSelf);
            Assert.IsFalse(variant.FoldedVisual.gameObject.activeSelf);
            Assert.AreEqual(0.5f, garment.LocalBounds.size.y, 0.01f, "hanging bounds while displayed");
            Assert.AreEqual(Vector3.zero, garment.GripAttach.localPosition, "hanger grip at the hook (prefab pivot)");
            Assert.IsFalse(garment.Grab.useDynamicAttach);
            Assert.IsFalse(garment.Grab.trackRotation, "a garment held by the hanger keeps hanging straight down");

            int landed = 0;
            SoundKind landedKind = SoundKind.Hard;
            scene.Suitcase.ItemLanded += (i, k, c) =>
            {
                landed++;
                landedKind = k;
            };

            GameplayTestScene.Teleport(garment, scene.InsideVolume);
            Assert.IsTrue(scene.Suitcase.TryPlace(garment));
            Assert.AreEqual(ItemVariant.Hanging, variant.Current, "the swap happens half-way through the settle tween, not at once");

            int hangingFrames = 0;
            int foldedFramesBeforeLanding = 0;
            float deadline = Time.realtimeSinceStartup + 2f;
            while (landed == 0 && Time.realtimeSinceStartup < deadline)
            {
                if (variant.Current == ItemVariant.Hanging)
                {
                    hangingFrames++;
                }
                else
                {
                    foldedFramesBeforeLanding++;
                }

                yield return null;
            }

            Assert.AreEqual(1, landed);
            Assert.Greater(hangingFrames, 0);
            Assert.Greater(foldedFramesBeforeLanding, 0, "folded before the tween ended");
            Assert.AreEqual(ItemVariant.Folded, variant.Current);
            Assert.AreEqual(SoundKind.Cloth, landedKind);
            Assert.AreEqual(0.05f, garment.GetComponent<BoxCollider>().size.y, 0.01f, "root collider follows the folded visual");

            yield return new WaitForSecondsRealtime(0.3f); // soft-goods squash finished
            Assert.AreEqual(garment.BaseLocalScale, garment.transform.localScale, "squash restored the scale");
            Assert.IsTrue(scene.Suitcase.TryGetSlot(garment, out var slot));
            Assert.Less(Vector3.Distance(slot.PoseFor(garment).position, garment.transform.position), 0.005f);

            Assert.IsTrue(scene.Suitcase.Remove(garment));
            Assert.AreEqual(ItemVariant.Folded, variant.Current, "taken out: stays folded until it is back home");

            Assert.IsTrue(garment.ReturnToSpawn(true));
            Assert.AreEqual(ItemVariant.Hanging, variant.Current, "home again: hanging");
            Assert.AreEqual(0.5f, garment.GetComponent<BoxCollider>().size.y, 0.01f);
            Assert.IsTrue(garment.IsDocked);
        }

        [UnityTest]
        public IEnumerator ReturnToPool_RestoresHanging()
        {
            GameplayTestScene.Teleport(garment, scene.InsideVolume);
            Assert.IsTrue(scene.Suitcase.TryPlace(garment));
            yield return new WaitForSecondsRealtime(0.6f);
            Assert.AreEqual(ItemVariant.Folded, garment.Variant.Current);

            scene.Pool.DeactivateAll();
            Assert.AreEqual(ProductItemState.Pooled, garment.State);
            Assert.AreEqual(ItemVariant.Hanging, garment.Variant.Current);
            Assert.IsFalse(scene.Suitcase.Contains(garment));
        }
    }
}
