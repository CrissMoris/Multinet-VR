using System.Collections.Generic;
using MultiTravel.EditorTools.Art;
using MultiTravel.EditorTools.Data;
using MultiTravel.EditorTools.Validation;
using MultiTravel.Gameplay.Director;
using MultiTravel.Gameplay.Feedback;
using MultiTravel.Gameplay.Items;
using MultiTravel.Gameplay.Suitcase;
using MultiTravel.Gameplay.UI;
using MultiTravel.Gameplay.Xr;
using MultiTravel.Operator;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;

namespace MultiTravel.EditorTools.SceneBuild
{
    /// <summary>
    /// Builds Main.unity from the production art: a hotel room with a U-shaped dressing-room wardrobe (12 bays x 3
    /// reachable shelves = 36 spawn slots) around the participant, the open suitcase on a luggage bench in front, and all
    /// gameplay systems wired. Saving over the existing scene path keeps its GUID (build settings stay valid).
    /// Layout (Unity): participant at the origin facing +Z; floor at y = 0.
    /// </summary>
    public static class MainSceneBuilder
    {
        public const float BayRadius = 1.05f;
        public static readonly float[] ShelfTops = { 0.68f, 1.08f, 1.48f };
        public static readonly float[] BayAngles = { -165f, -139f, -113f, -87f, -61f, -35f, 35f, 61f, 87f, 113f, 139f, 165f };
        public static readonly Vector3 BenchPosition = new Vector3(0f, 0f, 0.56f);
        public const float BenchTop = 0.42f;

        // Suitcase interior (metres, suitcase-root local): see tools/blender/mt_environment.py suitcase_open().
        private const float WheelHeight = 0.055f;
        private const float ShellWall = 0.006f;
        private const float BaseHeight = 0.14f;
        private static readonly Vector2 InteriorSize = new Vector2(0.67f, 0.43f);
        private const float InteriorFloorY = WheelHeight + ShellWall + 0.006f;

        private const string EnvironmentFolder = ArtImporter.EnvironmentModelsFolder;
        private const string RigPath = "Assets/VRTemplateAssets/Prefabs/Setup/Complete XR Origin Set Up Hands Variant.prefab";

        [MenuItem("MultiTravel/Generate/Rebuild Main Scene", priority = 140)]
        public static void RebuildMenu()
        {
            Build();
        }

        public static void Build()
        {
            var catalog = ProductDataGenerator.LoadCatalog();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ConfigureLighting();

            var environment = new GameObject("Environment").transform;
            BuildRoom(environment);
            var bays = BuildWardrobe(environment, out var spawnSlots);
            BuildFurniture(environment);

            var systems = new GameObject("Gameplay systems");
            var pool = systems.AddComponent<ItemPool>();
            pool.SetCatalog(catalog);
            var interactionLock = systems.AddComponent<InteractionLock>();
            interactionLock.SetPool(pool);
            var recovery = systems.AddComponent<ItemRecoveryService>();
            recovery.SetPool(pool);
            recovery.Configure(0f, new Bounds(new Vector3(0.15f, 1.4f, 0f), new Vector3(6.6f, 2.8f, 5.4f)));
            var layout = systems.AddComponent<SpawnSlotLayout>();
            layout.SetSlots(spawnSlots);

            var suitcase = BuildSuitcase(environment, pool);
            systems.AddComponent<SettleWatcher>().Configure(suitcase, pool);
            var feedback = systems.AddComponent<PlacementFeedback>();
            feedback.SetSuitcase(suitcase);
            systems.AddComponent<ProductNameTags>().SetPool(pool);
            var director = systems.AddComponent<GameplayDirector>();
            director.Configure(catalog, pool, layout, suitcase, interactionLock, feedback);
            EventSceneGenerator.AddConfirmButton(director);
            BuildConfirmStand(director);

            var panel = new GameObject("VR status").AddComponent<VrPanelUI>();
            panel.transform.position = new Vector3(0f, 2.05f, 2.72f);
            panel.Configure(director, catalog);
            new GameObject("Operator screen").AddComponent<OperatorScreen>();
            new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RigPath));
            rig.name = "Quest hands and controllers";
            rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            rig.AddComponent<XrRigController>().SetOrigin(rig.GetComponentInChildren<XROrigin>());

            MarkStatic(environment.gameObject);
            EditorSceneManager.SaveScene(scene, ProjectValidator.MainScenePath);
            Debug.Log($"[MultiTravel] Main scene rebuilt: {bays} wardrobe bays, {spawnSlots.Count} spawn slots.");
        }

        /// <summary>
        /// Puts the manual-confirm poke button on a slim oak stand right of the suitcase. Stand and button share one visual
        /// root, so both disappear when the completion mode does not allow a manual confirmation.
        /// </summary>
        private static void BuildConfirmStand(GameplayDirector director)
        {
            var root = GameObject.Find("Manual confirmation");
            if (root == null)
            {
                return;
            }

            root.transform.position = new Vector3(0.66f, 0.93f, 0.36f);
            var face = root.transform.Find("Button face");
            var assembly = new GameObject("Button assembly");
            assembly.transform.SetParent(root.transform, false);
            face.SetParent(assembly.transform, true);
            var oak = ArtImporter.GetMaterial("oak_veneer");
            var column = GameObject.CreatePrimitive(PrimitiveType.Cube);
            column.name = "Stand column";
            Object.DestroyImmediate(column.GetComponent<BoxCollider>());
            column.transform.SetParent(assembly.transform, false);
            column.transform.position = new Vector3(0.66f, 0.45f, 0.36f);
            column.transform.localScale = new Vector3(0.14f, 0.9f, 0.14f);
            column.GetComponent<MeshRenderer>().sharedMaterial = oak;
            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "Stand top";
            Object.DestroyImmediate(plate.GetComponent<BoxCollider>());
            plate.transform.SetParent(assembly.transform, false);
            plate.transform.position = new Vector3(0.66f, 0.9f, 0.36f);
            plate.transform.localScale = new Vector3(0.27f, 0.025f, 0.18f);
            plate.GetComponent<MeshRenderer>().sharedMaterial = oak;
            var button = root.GetComponent<ManualConfirmButton>();
            var label = root.GetComponentInChildren<TMPro.TMP_Text>(true);
            button.Configure(director, assembly, label, face.GetComponent<Renderer>(), root.GetComponent<BoxCollider>());
        }

        // ------------------------------------------------------------------------------------------------ lighting

        private static void ConfigureLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.86f, 0.88f, 0.92f);
            RenderSettings.ambientEquatorColor = new Color(0.74f, 0.71f, 0.67f);
            RenderSettings.ambientGroundColor = new Color(0.45f, 0.41f, 0.37f);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = false;
            RenderSettings.skybox = null;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;

            var sun = new GameObject("Window daylight").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.6f;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.65f;
            sun.transform.rotation = Quaternion.Euler(42f, 72f, 0f); // from the window on the left wall

            var lights = new GameObject("Ceiling downlights").transform;
            foreach (var p in new[] { new Vector3(-1.6f, 2.8f, -1.6f), new Vector3(0f, 2.8f, 0.4f), new Vector3(1.8f, 2.8f, 2.0f), new Vector3(-1.6f, 2.8f, 2.0f), new Vector3(1.8f, 2.8f, -1.6f) })
            {
                var l = new GameObject("Downlight").AddComponent<Light>();
                l.transform.SetParent(lights, false);
                l.transform.position = p;
                l.type = LightType.Point;
                l.range = 5.5f;
                l.intensity = 3.2f;
                l.color = new Color(1f, 0.9f, 0.78f);
                l.shadows = LightShadows.None;
            }

            // Inside the wardrobe ring: shelf LEDs read better with a soft warm fill at shelf height.
            var fill = new GameObject("Wardrobe fill").AddComponent<Light>();
            fill.transform.position = new Vector3(0f, 1.9f, 0.1f);
            fill.type = LightType.Point;
            fill.range = 3.2f;
            fill.intensity = 2.4f;
            fill.color = new Color(1f, 0.93f, 0.84f);
            fill.shadows = LightShadows.None;

            var probe = new GameObject("Room reflection").AddComponent<ReflectionProbe>();
            probe.transform.position = new Vector3(0.15f, 1.45f, 0f);
            probe.size = new Vector3(6.9f, 2.9f, 5.7f);
            probe.boxProjection = true;
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            probe.resolution = 256;
        }

        // ------------------------------------------------------------------------------------------------ room

        private static void BuildRoom(Transform parent)
        {
            var shell = InstantiateModel("room-shell", parent, Vector3.zero, Quaternion.identity);
            // Physics: floor and walls (items can never leave the room).
            AddBoxCollider(shell, new Vector3(0.15f, -0.05f, -0.05f), new Vector3(7.2f, 0.1f, 6.0f));
            AddBoxCollider(shell, new Vector3(0.15f, 1.45f, 2.86f), new Vector3(7.2f, 2.9f, 0.12f));
            AddBoxCollider(shell, new Vector3(0.15f, 1.45f, -2.96f), new Vector3(7.2f, 2.9f, 0.12f));
            AddBoxCollider(shell, new Vector3(3.66f, 1.45f, -0.05f), new Vector3(0.12f, 2.9f, 6.0f));
            AddBoxCollider(shell, new Vector3(-3.36f, 1.45f, -0.05f), new Vector3(0.12f, 2.9f, 6.0f));
            AddBoxCollider(shell, new Vector3(0.15f, 2.95f, -0.05f), new Vector3(7.2f, 0.1f, 6.0f));
            var rug = InstantiateModel("rug", parent, new Vector3(0f, 0f, 0.1f), Quaternion.identity);
            rug.name = "Rug";
            var curtains = InstantiateModel("curtains", parent, new Vector3(-3.22f, 0f, 0.2f), Quaternion.Euler(0f, 90f, 0f));
            curtains.name = "Curtains";
        }

        // ------------------------------------------------------------------------------------------------ wardrobe

        private static int BuildWardrobe(Transform parent, out List<SpawnSlot> spawnSlots)
        {
            spawnSlots = new List<SpawnSlot>();
            var root = new GameObject("Dressing-room wardrobe").transform;
            root.SetParent(parent, false);
            foreach (var angle in BayAngles)
            {
                float rad = angle * Mathf.Deg2Rad;
                var position = new Vector3(Mathf.Sin(rad) * BayRadius, 0f, Mathf.Cos(rad) * BayRadius);
                var bay = InstantiateModel("wardrobe-bay", root, position, Quaternion.Euler(0f, angle, 0f));
                bay.name = $"Wardrobe bay {angle:+0;-0}";
                // shelves, sides, back and drawer cabinet as colliders (items rest on shelves, cannot fall through)
                foreach (var top in ShelfTops)
                {
                    AddBoxCollider(bay, new Vector3(0f, top - 0.0125f, 0.005f), new Vector3(0.414f, 0.025f, 0.36f));
                }

                AddBoxCollider(bay, new Vector3(0f, 0.33f, 0f), new Vector3(0.43f, 0.66f, 0.38f));
                AddBoxCollider(bay, new Vector3(0f, 1.05f, 0.186f), new Vector3(0.43f, 1.95f, 0.012f));
                foreach (var side in new[] { -1f, 1f })
                {
                    AddBoxCollider(bay, new Vector3(side * 0.216f, 1.05f, 0f), new Vector3(0.018f, 1.95f, 0.38f));
                }

                for (int tier = 0; tier < ShelfTops.Length; tier++)
                {
                    var slotGo = new GameObject($"Product position {tier + 1}");
                    slotGo.transform.SetParent(bay.transform, false);
                    slotGo.transform.localPosition = new Vector3(0f, ShelfTops[tier], 0.005f);
                    var slot = slotGo.AddComponent<SpawnSlot>();
                    slot.SetHeightOffset(0.004f);
                    spawnSlots.Add(slot);
                }
            }

            return BayAngles.Length;
        }

        // ------------------------------------------------------------------------------------------------ suitcase

        private static SuitcaseController BuildSuitcase(Transform parent, ItemPool pool)
        {
            var bench = InstantiateModel("luggage-bench", parent, BenchPosition, Quaternion.identity);
            bench.name = "Luggage bench";
            AddBoxCollider(bench, new Vector3(0f, BenchTop / 2f, 0f), new Vector3(0.86f, BenchTop, 0.56f));

            var suitcaseRoot = new GameObject("Open suitcase");
            suitcaseRoot.transform.SetParent(parent, false);
            suitcaseRoot.transform.position = BenchPosition + new Vector3(0f, BenchTop, 0f);
            var model = InstantiateModel("suitcase-open", suitcaseRoot.transform, suitcaseRoot.transform.position, Quaternion.identity);
            model.name = "Suitcase model";
            // Shell colliders: interior floor, four walls (items dropped in rest inside), and the open lid.
            var cRoot = suitcaseRoot;
            AddBoxCollider(cRoot, new Vector3(0f, (WheelHeight + InteriorFloorY) / 2f, 0f), new Vector3(0.70f, InteriorFloorY - WheelHeight + 0.02f, 0.46f));
            float wallH = BaseHeight;
            float wallY = WheelHeight + BaseHeight / 2f;
            AddBoxCollider(cRoot, new Vector3(0f, wallY, -0.227f), new Vector3(0.70f, wallH, 0.012f));
            AddBoxCollider(cRoot, new Vector3(0f, wallY, 0.227f), new Vector3(0.70f, wallH, 0.012f));
            AddBoxCollider(cRoot, new Vector3(-0.347f, wallY, 0f), new Vector3(0.012f, wallH, 0.46f));
            AddBoxCollider(cRoot, new Vector3(0.347f, wallY, 0f), new Vector3(0.012f, wallH, 0.46f));
            var lid = new GameObject("Lid collider");
            lid.transform.SetParent(cRoot.transform, false);
            lid.transform.localPosition = new Vector3(0f, WheelHeight + BaseHeight, 0.23f);
            lid.transform.localRotation = Quaternion.Euler(-10f, 0f, 0f);
            var lidBox = lid.AddComponent<BoxCollider>();
            lidBox.center = new Vector3(0f, 0.23f, 0.06f);
            lidBox.size = new Vector3(0.70f, 0.46f, 0.11f);

            var volumeGo = new GameObject("Placement volume");
            volumeGo.transform.SetParent(cRoot.transform, false);
            float rim = WheelHeight + BaseHeight;
            float top = rim + 0.15f;
            volumeGo.transform.localPosition = new Vector3(0f, (InteriorFloorY + top) / 2f, 0f);
            var volume = volumeGo.AddComponent<BoxCollider>();
            volume.isTrigger = true;
            volume.size = new Vector3(InteriorSize.x, top - InteriorFloorY, InteriorSize.y);

            // 2 x 2 packing columns, 9 layers each (36 slots); items lie flat, long side across the suitcase.
            var slots = new List<SuitcaseSlot>();
            var columns = new[] { new Vector2(-0.165f, -0.105f), new Vector2(0.165f, -0.105f), new Vector2(-0.165f, 0.105f), new Vector2(0.165f, 0.105f) };
            int column = 0;
            foreach (var c in columns)
            {
                SuitcaseSlot below = null;
                for (int layer = 0; layer < 9; layer++)
                {
                    var slotGo = new GameObject($"Packed slot c{column}-l{layer}");
                    slotGo.transform.SetParent(cRoot.transform, false);
                    slotGo.transform.localPosition = new Vector3(c.x, InteriorFloorY + layer * 0.03f, c.y);
                    slotGo.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                    var slot = slotGo.AddComponent<SuitcaseSlot>();
                    slot.SetBelow(below);
                    slots.Add(slot);
                    below = slot;
                }

                column++;
            }

            var suitcase = cRoot.AddComponent<SuitcaseController>();
            suitcase.Configure(volume, slots);
            suitcase.SetPool(pool);
            return suitcase;
        }

        // ------------------------------------------------------------------------------------------------ furniture

        private static void BuildFurniture(Transform parent)
        {
            var bed = InstantiateModel("bed", parent, new Vector3(2.45f, 0f, 1.25f), Quaternion.Euler(0f, 90f, 0f));
            bed.name = "Bed";
            AddBoxCollider(bed, new Vector3(0f, 0.33f, 0f), new Vector3(1.66f, 0.66f, 2.04f));
            foreach (var z in new[] { 0.0f, 2.5f })
            {
                var stand = InstantiateModel("nightstand", parent, new Vector3(3.3f, 0f, z), Quaternion.Euler(0f, -90f, 0f));
                stand.name = "Nightstand";
                AddBoxCollider(stand, new Vector3(0f, 0.275f, 0f), new Vector3(0.5f, 0.55f, 0.4f));
                InstantiateModel("table-lamp", parent, new Vector3(3.3f, 0.55f, z), Quaternion.identity).name = "Table lamp";
                var lampLight = new GameObject("Lamp light").AddComponent<Light>();
                lampLight.transform.SetParent(parent, false);
                lampLight.transform.position = new Vector3(3.3f, 0.95f, z);
                lampLight.type = LightType.Point;
                lampLight.range = 2.2f;
                lampLight.intensity = 0.9f;
                lampLight.color = new Color(1f, 0.82f, 0.6f);
                lampLight.shadows = LightShadows.None;
            }

            var mirror = InstantiateModel("floor-mirror", parent, new Vector3(-2.2f, 0f, -2.75f), Quaternion.Euler(0f, 180f, 0f));
            mirror.name = "Floor mirror";
            var rack = InstantiateModel("hanger-rail-decor", parent, new Vector3(-2.75f, 0f, 2.05f), Quaternion.Euler(0f, 90f, 0f));
            rack.name = "Clothes rack";

            // Poly Haven CC0 furniture already in the project (see ASSET_MANIFEST.md).
            ImportFurniture("mid_century_lounge_chair", parent, new Vector3(-2.6f, 0f, -1.9f), 0.85f, 35f);
            ImportFurniture("modern_coffee_table_01", parent, new Vector3(-1.75f, 0f, -2.1f), 0.8f, 0f);
            ImportFurniture("calathea_orbifolia_01", parent, new Vector3(-2.95f, 0f, 1.55f), 0.55f, 0f);
            ImportFurniture("drawer_cabinet", parent, new Vector3(2.9f, 0f, -2.45f), 1.0f, 180f);
            ImportFurniture("ceramic_vase_01", parent, new Vector3(2.75f, 0.0f, -2.45f), 0.2f, 0f, stackOnTopOf: "drawer_cabinet");
            ImportFurniture("hanging_picture_frame_01", parent, new Vector3(2.0f, 1.6f, 2.77f), 0.9f, 180f, wall: true);
            ImportFurniture("wall_clock", parent, new Vector3(-1.6f, 2.2f, 2.77f), 0.35f, 180f, wall: true);
        }

        private static readonly Dictionary<string, GameObject> placedFurniture = new Dictionary<string, GameObject>();

        private static void ImportFurniture(string id, Transform parent, Vector3 position, float width, float yaw, bool hang = false, bool wall = false, string stackOnTopOf = null)
        {
            var folder = $"{ArtImporter.PolyHavenFolder}/{id}";
            var guids = AssetDatabase.FindAssets("t:Model", new[] { folder });
            if (guids.Length == 0)
            {
                Debug.LogWarning($"[MultiTravel] Furniture '{id}' not found under {folder}; skipped.");
                return;
            }

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[0]));
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            go.name = id;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * go.transform.localRotation;
            var b = WorldBounds(go);
            float horizontal = Mathf.Max(b.size.x, b.size.z);
            go.transform.localScale *= width / Mathf.Max(0.001f, horizontal);
            var wb = WorldBounds(go);
            float y = hang ? position.y - wb.size.y : position.y;
            if (!string.IsNullOrEmpty(stackOnTopOf) && placedFurniture.TryGetValue(stackOnTopOf, out var under) && under != null)
            {
                y = WorldBounds(under).max.y;
            }

            var target = new Vector3(position.x, y, position.z);
            go.transform.position += target - new Vector3(wb.center.x, wb.min.y, wall ? wb.max.z : wb.center.z);
            if (wall)
            {
                go.transform.position += new Vector3(0f, 0f, 0f);
            }

            RemapPolyHavenMaterials(go, folder, id);
            if (!hang && !wall)
            {
                // World-aligned collider next to the model (Poly Haven roots carry a -90 X / 100x import transform).
                var fb = WorldBounds(go);
                var colGo = new GameObject(id + " collider");
                colGo.transform.SetParent(parent, false);
                colGo.transform.position = fb.center;
                colGo.AddComponent<BoxCollider>().size = fb.size;
            }

            placedFurniture[id] = go;
        }

        private static void RemapPolyHavenMaterials(GameObject go, string folder, string id)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string original = mats[i] != null ? mats[i].name : id;
                    string safe = original.Replace('/', '_').Replace('\\', '_');
                    var existing = AssetDatabase.LoadAssetAtPath<Material>($"{GeneratedAssetUtil.MaterialsFolder}/PH_{id}_{safe}.mat");
                    if (existing != null)
                    {
                        mats[i] = existing;
                    }
                }

                r.sharedMaterials = mats;
            }
        }

        // ------------------------------------------------------------------------------------------------ helpers

        public static GameObject InstantiateModel(string name, Transform parent, Vector3 position, Quaternion rotation)
        {
            var path = $"{EnvironmentFolder}/{name}.fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null)
            {
                throw new System.IO.FileNotFoundException($"Environment model missing: {path}. Run tools/blender/mt_build_assets.py and MultiTravel/Generate/Import Production Art.");
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            go.transform.position = position;
            go.transform.rotation = rotation * go.transform.rotation;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                r.shadowCastingMode = ShadowCastingMode.On;
            }

            return go;
        }

        private static BoxCollider AddBoxCollider(GameObject go, Vector3 center, Vector3 size)
        {
            // Centre/size are in unscaled metres relative to the object's pivot (a counter-scaled holder absorbs any scale).
            var holder = new GameObject("Collider");
            holder.transform.SetParent(go.transform, false);
            var s = go.transform.lossyScale;
            holder.transform.localScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);
            var box = holder.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
            return box;
        }

        private static Bounds WorldBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            var b = renderers.Length > 0 ? renderers[0].bounds : new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in renderers)
            {
                b.Encapsulate(r.bounds);
            }

            return b;
        }

        private static void MarkStatic(GameObject root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(t.gameObject,
                    StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            }
        }
    }
}
