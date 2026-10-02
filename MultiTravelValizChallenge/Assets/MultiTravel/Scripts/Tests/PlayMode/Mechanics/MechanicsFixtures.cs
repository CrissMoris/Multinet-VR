using System;
using MultiTravel.Core.Products;
using MultiTravel.Gameplay.Director;
using MultiTravel.Gameplay.Items;
using MultiTravel.Gameplay.Suitcase;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using Object = UnityEngine.Object;

namespace MultiTravel.Tests.PlayMode.Mechanics
{
    /// <summary>Builders shared by the v2 mechanics tests (OVERHAUL_PLAN §5). Everything is tracked by the scene and destroyed with it.</summary>
    public static class MechanicsFixtures
    {
        /// <summary>A product definition with explicit presentation data.</summary>
        public static ProductDefinition Product(
            GameplayTestScene scene,
            string id,
            bool isCorrect,
            DisplayZone zone,
            PackedKind packed = PackedKind.Flat,
            GripPreset grip = GripPreset.Dynamic,
            ProductCategory category = ProductCategory.Other,
            bool hasHangingVariant = false)
        {
            var definition = scene.Track(ScriptableObject.CreateInstance<ProductDefinition>());
            definition.name = id;
            definition.Id = id;
            definition.DisplayName = id;
            definition.IsCorrect = isCorrect;
            definition.Category = category;
            definition.Availability = GenderAvailability.Both;
            definition.ClearRequiredOverride();
            definition.Presentation = new ProductPresentation
            {
                Zone = zone,
                Packed = packed,
                Grip = grip,
                HasHangingVariant = hasHangingVariant
            };
            return definition;
        }

        /// <summary>A spawn slot of <paramref name="zone"/> under <paramref name="parent"/>.</summary>
        public static SpawnSlot SpawnSlot(Transform parent, Vector3 localPosition, DisplayZone zone)
        {
            var go = new GameObject("Slot_" + zone + "_" + parent.childCount);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var slot = go.AddComponent<SpawnSlot>();
            slot.SetZone(zone);
            slot.SetHeightOffset(0.002f);
            return slot;
        }

        /// <summary>A suitcase slot of <paramref name="kind"/> under the scene's suitcase.</summary>
        public static SuitcaseSlot SuitcaseSlot(GameplayTestScene scene, PackedKind kind, Vector3 localPosition)
        {
            var go = new GameObject("Pack_" + kind + "_" + localPosition.x.ToString("0.00"));
            go.transform.SetParent(scene.Suitcase.transform, false);
            go.transform.localPosition = localPosition;
            var slot = go.AddComponent<SuitcaseSlot>();
            slot.SetKind(kind);
            return slot;
        }

        /// <summary>
        /// Builds a garment with <c>Hanging</c> (0.30 × 0.50 × 0.05, hanging below the pivot) and <c>Folded</c>
        /// (0.30 × 0.05 × 0.25, resting on the pivot) child visuals, a root BoxCollider and <see cref="ItemVisualVariant"/>.
        /// Under an inactive <paramref name="parent"/> the object never wakes up and can serve as a prefab template; with a
        /// null parent it is a live, tracked scene object.
        /// </summary>
        public static GameObject VariantGarment(GameplayTestScene scene, string name, Transform parent)
        {
            var go = new GameObject(name);
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }
            else
            {
                scene.Track(go);
            }

            Visual(go.transform, ItemVisualVariant.HangingChildName, new Vector3(0f, -0.25f, 0f), new Vector3(0.3f, 0.5f, 0.05f));
            var folded = Visual(go.transform, ItemVisualVariant.FoldedChildName, new Vector3(0f, 0.025f, 0f), new Vector3(0.3f, 0.05f, 0.25f));
            folded.gameObject.SetActive(false);
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, -0.25f, 0f);
            box.size = new Vector3(0.3f, 0.5f, 0.05f);
            go.AddComponent<ItemVisualVariant>();
            go.AddComponent<ProductItem>();
            return go;
        }

        private static Transform Visual(Transform parent, string name, Vector3 position, Vector3 size)
        {
            var container = new GameObject(name).transform;
            container.SetParent(parent, false);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(cube.GetComponent<Collider>());
            cube.name = "Model";
            cube.transform.SetParent(container, false);
            cube.transform.localPosition = position;
            cube.transform.localScale = size;
            return container;
        }

        /// <summary>Creates, registers and activates a variant garment for <paramref name="definition"/> at <paramref name="spawn"/>.</summary>
        public static ProductItem CreateVariantItem(GameplayTestScene scene, ProductDefinition definition, Vector3 spawn)
        {
            var go = VariantGarment(scene, "Item_" + definition.Id, null);
            go.transform.SetParent(scene.Root.transform, false);
            go.transform.position = spawn;
            var item = go.GetComponent<ProductItem>();
            item.Setup(definition);
            item.SetSpawnPose(spawn, Quaternion.identity);
            if (!scene.Pool.Register(item) || !scene.Pool.ActivateItem(item))
            {
                throw new InvalidOperationException("Could not register / activate " + definition.Id);
            }

            return item;
        }

        /// <summary>Practice item id used by the director rig.</summary>
        public const string PracticeId = "test-practice-tag";

        /// <summary>
        /// Director-driven gameplay on top of the test scene: pool (scene catalog, cube templates, A as a hanging/folded
        /// garment), four spawn slots, interaction lock, director bound to the scene's services and a practice item.
        /// </summary>
        public sealed class DirectorRig
        {
            public ItemPool Pool;
            public SpawnSlotLayout Layout;
            public InteractionLock Lock;
            public GameplayDirector Director;
            public ProductDefinition Practice;
            public Transform PracticeSpawn;

            public static DirectorRig Create(GameplayTestScene scene)
            {
                var rig = new DirectorRig();
                var templates = scene.Track(new GameObject("Templates"));
                templates.SetActive(false);
                var cube = new GameObject("CubeTemplate");
                cube.transform.SetParent(templates.transform, false);
                cube.AddComponent<BoxCollider>().size = Vector3.one * 0.1f;
                cube.AddComponent<ProductItem>();
                var garment = VariantGarment(scene, "GarmentTemplate", templates.transform);

                scene.DefinitionA.VisualPrefab = garment;
                scene.DefinitionA.Presentation = new ProductPresentation
                {
                    Zone = DisplayZone.Any,
                    Packed = PackedKind.Flat,
                    Grip = GripPreset.Hanger,
                    HasHangingVariant = true
                };
                scene.DefinitionB.VisualPrefab = cube;
                scene.DefinitionWrong.VisualPrefab = cube;
                rig.Practice = Product(scene, PracticeId, true, DisplayZone.Any, PackedKind.Organiser);
                rig.Practice.VisualPrefab = cube;

                var poolGo = scene.Track(new GameObject("RigPool"));
                rig.Pool = poolGo.AddComponent<ItemPool>();
                rig.Pool.SetCatalog(scene.Catalog);
                scene.Suitcase.SetPool(rig.Pool);

                var layoutGo = scene.Track(new GameObject("RigLayout"));
                layoutGo.transform.position = GameplayTestScene.TableTop;
                rig.Layout = layoutGo.AddComponent<SpawnSlotLayout>();
                var slots = new System.Collections.Generic.List<SpawnSlot>();
                for (int i = 0; i < 4; i++)
                {
                    slots.Add(SpawnSlot(layoutGo.transform, new Vector3(-0.6f + 0.4f * i, 0f, 0f), DisplayZone.Any));
                }

                rig.Layout.SetSlots(slots);

                var lockGo = scene.Track(new GameObject("RigLock"));
                rig.Lock = lockGo.AddComponent<InteractionLock>();
                rig.Lock.SetPool(rig.Pool);

                rig.PracticeSpawn = scene.Track(new GameObject("PracticeSpawn")).transform;
                rig.PracticeSpawn.position = GameplayTestScene.SuitcasePosition + new Vector3(0f, 0.7f, -0.3f);

                var directorGo = scene.Track(new GameObject("RigDirector"));
                rig.Director = directorGo.AddComponent<GameplayDirector>();
                rig.Director.Configure(scene.Catalog, rig.Pool, rig.Layout, scene.Suitcase, rig.Lock, null);
                rig.Director.ConfigurePractice(rig.Practice, rig.PracticeSpawn);
                rig.Director.Bind(scene.Session, scene.Config, scene.Score, scene.Evaluator, new ProductSetResolver());
                return rig;
            }
        }

        /// <summary>Drives the session from Welcome to Instructions.</summary>
        public static void EnterInstructions(GameplayTestScene scene)
        {
            scene.Session.BeginRegistration();
            if (!scene.Session.SubmitRegistration(GameplayTestScene.ValidInput()).IsValid)
            {
                throw new InvalidOperationException("Test input invalid.");
            }

            scene.Session.SelectGender(MultiTravel.Core.Session.Gender.Female);
        }

        /// <summary>A direct interactor ("hand") registered with the scene's interaction manager.</summary>
        public static XRDirectInteractor CreateHand(GameplayTestScene scene)
        {
            var hand = scene.Track(new GameObject("Test hand"));
            var trigger = hand.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.05f;
            var interactor = hand.AddComponent<XRDirectInteractor>();
            interactor.interactionManager = scene.Manager;
            return interactor;
        }

        /// <summary>Selects <paramref name="item"/> with <paramref name="hand"/>.</summary>
        public static void Grab(GameplayTestScene scene, XRDirectInteractor hand, ProductItem item)
        {
            scene.Manager.SelectEnter((IXRSelectInteractor)hand, (IXRSelectInteractable)item.Grab);
        }

        /// <summary>Moves the held item to <paramref name="position"/> and releases it in the same frame.</summary>
        public static void ReleaseAt(GameplayTestScene scene, XRDirectInteractor hand, ProductItem item, Vector3 position)
        {
            GameplayTestScene.Teleport(item, position);
            scene.Manager.SelectExit((IXRSelectInteractor)hand, (IXRSelectInteractable)item.Grab);
        }
    }
}
