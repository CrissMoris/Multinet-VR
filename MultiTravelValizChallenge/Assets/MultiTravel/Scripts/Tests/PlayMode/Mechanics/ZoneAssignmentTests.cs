using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MultiTravel.Core.Products;
using MultiTravel.Core.Services;
using MultiTravel.Gameplay.Items;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MultiTravel.Tests.PlayMode.Mechanics
{
    /// <summary>Zone-strict spawn assignment (OVERHAUL_PLAN §3/§5: <c>SpawnSlotLayout.Assign</c>).</summary>
    public sealed class ZoneAssignmentTests
    {
        private GameplayTestScene scene;
        private SpawnSlotLayout layout;
        private Transform slotRoot;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AppServices.Clear();
            scene = new GameplayTestScene();
            var go = scene.Track(new GameObject("Layout"));
            go.transform.position = new Vector3(0f, 1f, 2f);
            slotRoot = go.transform;
            layout = go.AddComponent<SpawnSlotLayout>();
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

        private List<SpawnSlot> Slots(params DisplayZone[] zones)
        {
            var slots = new List<SpawnSlot>();
            for (int i = 0; i < zones.Length; i++)
            {
                slots.Add(MechanicsFixtures.SpawnSlot(slotRoot, new Vector3(-1.2f + 0.3f * i, 0f, 0f), zones[i]));
            }

            layout.SetSlots(slots);
            return slots;
        }

        private ProductItem Item(string id, DisplayZone zone, int index)
        {
            var definition = MechanicsFixtures.Product(scene, id, true, zone);
            return scene.CreateItem(definition, scene.TableSpawn(index));
        }

        [UnityTest]
        public IEnumerator Assign_PutsEveryItemInItsOwnZone()
        {
            Slots(DisplayZone.Hanging, DisplayZone.Hanging, DisplayZone.Folded, DisplayZone.Business, DisplayZone.Any, DisplayZone.Any);
            var items = new List<ProductItem>
            {
                Item("hang-a", DisplayZone.Hanging, 0),
                Item("hang-b", DisplayZone.Hanging, 1),
                Item("fold-a", DisplayZone.Folded, 2),
                Item("biz-a", DisplayZone.Business, 3),
                Item("loose", DisplayZone.Any, 4),
            };
            yield return null;

            Assert.AreEqual(5, layout.Assign(items, true, 1234));
            Assert.AreEqual(0, layout.LastFallbackCount);
            Assert.AreEqual(0, layout.LastOverflowCount);
            foreach (var item in items)
            {
                var slot = layout.SlotOf(item);
                Assert.IsNotNull(slot, item.ProductId);
                Assert.AreSame(slot, item.HomeSlot, item.ProductId + " remembers its home slot");
                Assert.AreEqual(item.Definition.Presentation.Zone, slot.Zone, item.ProductId);
                Assert.Less(Vector3.Distance(item.transform.position, item.SpawnPose.position), 1e-3f, "free items move to the slot instantly");
                Assert.IsTrue(item.IsDocked, item.ProductId + " rests docked at home");
            }

            // Hanging slots are hooks: the hang point of the item sits on the slot.
            var hanging = items[0];
            var hook = layout.SlotOf(hanging).transform.position;
            Assert.Less(Vector3.Distance(hanging.transform.TransformPoint(hanging.HangPointLocal), hook), 1e-3f);
        }

        [UnityTest]
        public IEnumerator Assign_FullZone_FallsBackToAnyWithOneWarning_NeverAnotherNamedZone()
        {
            var slots = Slots(DisplayZone.Hanging, DisplayZone.Folded, DisplayZone.Folded, DisplayZone.Folded, DisplayZone.Any);
            var items = new List<ProductItem>
            {
                Item("hang-a", DisplayZone.Hanging, 0),
                Item("hang-b", DisplayZone.Hanging, 1),
                Item("hang-c", DisplayZone.Hanging, 2),
            };
            yield return null;

            LogAssert.Expect(LogType.Warning, new Regex("no free slot in their display zone"));
            LogAssert.Expect(LogType.Error, new Regex("had no spawn slot"));
            int assigned = layout.Assign(items, false, 0);

            Assert.AreEqual(2, assigned, "one hanging slot + one Any slot; the free folded slots are never used");
            Assert.AreEqual(1, layout.LastFallbackCount);
            Assert.AreEqual(1, layout.LastOverflowCount);
            foreach (var item in items)
            {
                var slot = layout.SlotOf(item);
                if (slot != null)
                {
                    Assert.That(slot.Zone, Is.EqualTo(DisplayZone.Hanging).Or.EqualTo(DisplayZone.Any), item.ProductId);
                }
            }

            for (int i = 1; i <= 3; i++)
            {
                CollectionAssert.DoesNotContain(layout.LastAssignedSlots, slots[i], "folded slots stay empty");
            }
        }

        [UnityTest]
        public IEnumerator Assign_SeededShuffle_IsDeterministic_AndStaysInsideTheZone()
        {
            Slots(DisplayZone.Business, DisplayZone.Business, DisplayZone.Business, DisplayZone.Business,
                DisplayZone.Leisure, DisplayZone.Leisure, DisplayZone.Leisure);
            var items = new List<ProductItem>
            {
                Item("biz-a", DisplayZone.Business, 0),
                Item("biz-b", DisplayZone.Business, 1),
                Item("biz-c", DisplayZone.Business, 2),
                Item("fun-a", DisplayZone.Leisure, 3),
                Item("fun-b", DisplayZone.Leisure, 4),
            };
            yield return null;

            layout.Assign(items, true, 77);
            var first = new Dictionary<ProductItem, SpawnSlot>();
            foreach (var item in items)
            {
                first[item] = layout.SlotOf(item);
            }

            layout.Assign(items, true, 77);
            foreach (var item in items)
            {
                Assert.AreSame(first[item], layout.SlotOf(item), "same seed, same layout");
            }

            var seenBizSlots = new HashSet<SpawnSlot>();
            for (int seed = 1; seed <= 25; seed++)
            {
                layout.Assign(items, true, seed);
                foreach (var item in items)
                {
                    var slot = layout.SlotOf(item);
                    Assert.AreEqual(item.Definition.Presentation.Zone, slot.Zone, "seed " + seed + ": " + item.ProductId);
                    if (item.ProductId == "biz-a")
                    {
                        seenBizSlots.Add(slot);
                    }
                }
            }

            Assert.Greater(seenBizSlots.Count, 1, "the shuffle moves items between the slots of their zone");
        }
    }
}
