using System;
using System.Collections.Generic;
using MultiTravel.Core.Products;
using MultiTravel.EditorTools.Art;
using MultiTravel.EditorTools.Data;
using MultiTravel.EditorTools.Validation;
using MultiTravel.Gameplay.Audio;
using MultiTravel.Gameplay.Director;
using MultiTravel.Gameplay.Feedback;
using MultiTravel.Gameplay.Items;
using MultiTravel.Gameplay.Presentation;
using MultiTravel.Gameplay.Suitcase;
using MultiTravel.Gameplay.Tutorial;
using MultiTravel.Gameplay.Xr;
using MultiTravel.Operator;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using Object = UnityEngine.Object;

namespace MultiTravel.EditorTools.SceneBuild
{
    /// <summary>
    /// Builds Main.unity (v2, docs/OVERHAUL_PLAN.md): a branded stage with a curved backdrop, a U-shaped open wardrobe whose
    /// zones match the item types, two console tables, the open suitcase on a luggage rack in front of the participant, the
    /// diegetic stopwatch / scoreboard / result card, mood lighting and all gameplay systems.
    /// <para>
    /// Environment models are exported by tools/blender in world space (participant at the origin facing +Z). Marker empties
    /// inside the FBX drive the wiring: <c>SLOT.&lt;zone&gt;.nn</c> spawn slots, <c>PACK.&lt;kind&gt;.nn</c> suitcase packing
    /// anchors, <c>PIVOT.*</c> animation pivots, <c>UI.*</c> text anchors, <c>VOL.placement</c>, <c>LIGHT.*</c> and
    /// <c>EMISSIVE.*</c>. Saving over the existing scene path keeps its GUID.
    /// </para>
    /// </summary>
    public static class MainSceneBuilder
    {
        /// <summary>Static environment models, all positioned in world space by the art pipeline.</summary>
        public static readonly string[] EnvironmentModels =
        {
            "stage-floor", "stage-backdrop", "floor-mat", "wardrobe-carcass", "wardrobe-hanging-module",
            "wardrobe-folded-module", "wardrobe-door-left", "wardrobe-door-right", "console-table-business",
            "console-table-leisure", "luggage-rack", "room-shell"
        };

        /// <summary>Where the participant enters the hotel room (door side), facing the cabin. Authored as MARK.start.</summary>
        public static readonly Vector3 DefaultStartPosition = new Vector3(0f, 0f, -5.6f);

        /// <summary>
        /// The suitcase (with its rack, floor mat and lights) stands towards the stopwatch / backdrop instead of in the middle
        /// of the cabin, so walking between the shelves is free.
        /// </summary>
        public static readonly Vector3 SuitcaseShift = new Vector3(0f, 0f, 1.45f);   // back ~0.1 m in front of the stopwatch wall (2.25 m)

        public const string SuitcaseModel = "suitcase-open";
        public const string StopwatchModel = "stopwatch";
        public const string ScoreboardModel = "scoreboard";

        /// <summary>Layers stacked above each flat / top packing anchor (3 cm each).</summary>
        public const int FlatLayers = 7;
        public const float LayerStep = 0.03f;

        /// <summary>A Blender cube empty with display size 1 spans ±1 unit, so its full size is 2 × scale.</summary>
        public const float VolumeEmptyUnitSize = 2f;

        private const string EnvironmentFolder = ArtImporter.EnvironmentModelsFolder;
        private const string RigPath = "Assets/VRTemplateAssets/Prefabs/Setup/Complete XR Origin Set Up Hands Variant.prefab";
        private const string VolumeProfilePath = GeneratedAssetUtil.RootFolder + "/Scenes/MainVolumeProfile.asset";
        private const string LightingSettingsPath = GeneratedAssetUtil.RootFolder + "/Scenes/MainLighting.lighting";

        [MenuItem("MultiTravel/Generate/Rebuild Main Scene", priority = 140)]
        public static void RebuildMenu()
        {
            Build();
        }

        [MenuItem("MultiTravel/Generate/Bake Main Lighting", priority = 141)]
        public static void BakeMenu()
        {
            BakeLighting();
        }

        public static void Build()
        {
            var catalog = ProductDataGenerator.LoadCatalog();
            if (catalog == null)
            {
                throw new InvalidOperationException("Product catalog missing. Run MultiTravel/Generate/Product Data first.");
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // NewScene unloads unreferenced assets; load the definitions afterwards so they are not destroyed stubs.
            catalog = ProductDataGenerator.LoadCatalog();
            var practice = ProductDataGenerator.LoadPracticeDefinition();
            ConfigureEnvironmentLighting();

            // ---------------------------------------------------------------- static set
            var environment = new GameObject("Environment").transform;
            var placed = new Dictionary<string, GameObject>();
            foreach (var name in EnvironmentModels)
            {
                placed[name] = InstantiateModel(name, environment);
            }

            // Animated displays live outside the static set (static batching would freeze the stopwatch needle).
            var displays = new GameObject("Diegetic displays").transform;
            var stopwatch = InstantiateModel(StopwatchModel, displays);
            var scoreboard = InstantiateModel(ScoreboardModel, displays);
            foreach (var name in new[] { "luggage-rack", "floor-mat" })
            {
                if (placed.TryGetValue(name, out var go) && go != null)
                {
                    go.transform.position += SuitcaseShift;
                }
            }

            AddDecor(environment);
            AddRoomFurniture(environment);
            AddStaticColliders(environment);

            // ---------------------------------------------------------------- systems
            var systems = new GameObject("Gameplay systems");
            var pool = systems.AddComponent<ItemPool>();
            pool.SetCatalog(catalog);
            var interactionLock = systems.AddComponent<InteractionLock>();
            interactionLock.SetPool(pool);
            var recovery = systems.AddComponent<ItemRecoveryService>();
            recovery.SetPool(pool);
            recovery.Configure(0f, new Bounds(new Vector3(0f, 1.5f, -2.6f), new Vector3(9.4f, 3.4f, 12.0f)));

            var spawnSlots = CreateSpawnSlots(environment);
            var layout = systems.AddComponent<SpawnSlotLayout>();
            layout.SetSlots(spawnSlots);
            ValidateZoneCapacity(catalog, layout);
            AddZoneLabels(spawnSlots, environment);
            AddShelfDecor(environment);
            AddBrandDecor(environment);

            var suitcase = BuildSuitcase(pool, out var lid, out var practiceSpawn);
            recovery.SetSuitcase(suitcase);
            systems.AddComponent<SettleWatcher>().Configure(suitcase, pool);
            var feedback = systems.AddComponent<PlacementFeedback>();
            feedback.SetSuitcase(suitcase);
            systems.AddComponent<ProductNameTags>().SetPool(pool);

            var director = systems.AddComponent<GameplayDirector>();
            director.Configure(catalog, pool, layout, suitcase, interactionLock, feedback);
            if (practice != null)
            {
                director.ConfigurePractice(practice, practiceSpawn);
            }
            else
            {
                Debug.LogWarning("[MultiTravel] Practice item definition missing; the in-VR tutorial will be skipped.");
            }

            EventSceneGenerator.AddConfirmButton(director);
            PlaceConfirmButton(director, suitcase.transform);

            // ---------------------------------------------------------------- presentation
            var presentation = new GameObject("Presentation");
            var audio = presentation.AddComponent<AudioDirector>();
            audio.Configure(director, suitcase, lid);
            SetSerialized(audio, "playCountdownBeeps", false); // PlacementFeedback already voices the countdown.

            var stopwatchDisplay = stopwatch.AddComponent<StopwatchDisplay>();
            stopwatchDisplay.Configure(FindRequired(stopwatch.transform, "PIVOT.needle"), FindRequired(stopwatch.transform, "UI.stopwatch_face"), director, audio);
            var scoreboardDisplay = scoreboard.AddComponent<ScoreboardDisplay>();
            scoreboardDisplay.Configure(FindRequired(scoreboard.transform, "UI.scoreboard"), new Vector2(1.0f, 0.45f));

            var resultGo = new GameObject("Result card");
            var resultAnchor = FindMarker(environment, "UI.result") ?? FindMarker(displays, "UI.result");
            if (resultAnchor != null)
            {
                FaceTextAnchor(resultAnchor);
                resultGo.transform.SetPositionAndRotation(resultAnchor.position, resultAnchor.rotation);
            }
            else
            {
                // Above the closed lid, 1.1 m from the eyes, slightly below eye level, facing the participant.
                // Above the back of the open suitcase, in front of the backdrop.
                resultGo.transform.SetPositionAndRotation(new Vector3(0f, 1.32f, SuitcaseShift.z + 0.35f), Quaternion.identity);
            }

            resultGo.AddComponent<ResultCard>().Configure(lid, new Vector2(0.9f, 0.55f));

            var mood = presentation.AddComponent<MoodLighting>();
            mood.Configure(BuildLightGroups(environment, suitcase.transform));
            presentation.AddComponent<AttractMode>().Configure(mood, scoreboardDisplay);
            presentation.AddComponent<TutorialController>().Configure(director, suitcase, audio);
            presentation.AddComponent<ScreenFade>().Configure(null);

            // ---------------------------------------------------------------- operator, rig
            new GameObject("Operator screen").AddComponent<OperatorScreen>();
            var spectator = new GameObject("Spectator camera");
            spectator.transform.position = new Vector3(1.0f, 2.0f, 1.9f);
            spectator.transform.LookAt(new Vector3(0f, 0.95f, -0.4f));
            spectator.AddComponent<SpectatorCamera>();

            new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RigPath));
            rig.name = "Quest hands and controllers";
            var startMarker = FindMarker(environment, "MARK.start");
            var startMark = new GameObject("Start mark").transform;
            startMark.position = startMarker != null ? new Vector3(startMarker.position.x, 0f, startMarker.position.z) : DefaultStartPosition;
            startMark.rotation = Quaternion.identity;
            rig.transform.SetPositionAndRotation(startMark.position, startMark.rotation);
            var rigController = rig.AddComponent<XrRigController>();
            rigController.SetOrigin(rig.GetComponentInChildren<XROrigin>());
            rigController.SetRecenterTarget(startMark);
            var modality = rig.GetComponentInChildren<XRInputModalityManager>(true);
            presentation.AddComponent<TrackingLossGuard>().Configure(pool, modality);
            rig.AddComponent<HandPresenceStyler>().Configure(modality, null);

            AddLightProbes(environment);
            AddReflectionProbe();
            AddPostProcessing();
            MarkStatic(environment.gameObject);

            EditorSceneManager.SaveScene(scene, ProjectValidator.MainScenePath);
            Debug.Log($"[MultiTravel] Main scene rebuilt: {spawnSlots.Count} spawn slots in {CountZones(spawnSlots)} zones, {suitcase.GetComponentsInChildren<SuitcaseSlot>().Length} suitcase slots.");
        }

        // ------------------------------------------------------------------------------------------------ slots

        private static List<SpawnSlot> CreateSpawnSlots(Transform environment)
        {
            var slots = new List<SpawnSlot>();
            foreach (var marker in FindMarkers(environment, "SLOT."))
            {
                var parts = marker.name.Split('.');
                if (parts.Length < 3 || !TryParseZone(parts[1], out var zone))
                {
                    Debug.LogWarning($"[MultiTravel] Spawn slot marker '{marker.name}' has an unknown zone; skipped.");
                    continue;
                }

                var slot = marker.gameObject.AddComponent<SpawnSlot>();
                slot.SetZone(zone);
                if (parts.Length > 3 && parts[3].Length > 0)
                {
                    // Size suffix: S / M / W (widest) and an optional T when the space above is open (tall items fit).
                    var cls = char.ToUpperInvariant(parts[3][0]);
                    slot.SetSize(cls == 'S' ? SlotSize.Small : cls == 'M' ? SlotSize.Medium : SlotSize.Wide, parts[3].ToUpperInvariant().Contains('T'));
                }
                slot.SetHeightOffset(zone == DisplayZone.Hanging ? 0f : 0.004f);
                slots.Add(slot);
            }

            if (slots.Count == 0)
            {
                throw new InvalidOperationException("No SLOT.* markers found in the environment models. Re-export the art (tools/blender/mt_build_assets.py).");
            }

            return slots;
        }

        /// <summary>
        /// Corporate logo on the stage plaque above the stopwatch (<c>UI.logo</c> marker) and as a floor decal in front of
        /// the backdrop. Both use the light (white wordmark) logo, which reads on the navy plaque and the parquet.
        /// </summary>
        private static void AddBrandDecor(Transform environment)
        {
            var plaque = FindMarker(environment, "UI.logo");
            if (plaque != null)
            {
                // The backdrop is a concave arc around the participant: the logo is a curved strip on the same radius.
                float radius = new Vector2(plaque.position.x, plaque.position.z).magnitude - 0.012f;
                CreateCurvedLogo("Brand plaque", environment, BrandAssets.LogoMaterial(BrandAssets.HorizontalLight), radius, plaque.position.y, 0.92f, 0.246f);
            }

            float floorTop = 0.006f;
            var floor = environment.Find("stage-floor");
            if (floor != null)
            {
                var bounds = new Bounds(floor.position, Vector3.zero);
                foreach (var r in floor.GetComponentsInChildren<Renderer>())
                {
                    bounds.Encapsulate(r.bounds);
                }

                floorTop = bounds.max.y + 0.004f;
            }

            CreateLogoQuad("Brand floor decal", environment, BrandAssets.LogoMaterial(BrandAssets.VerticalLight),
                new Vector3(0f, floorTop, -0.6f), Quaternion.Euler(90f, 0f, 0f), new Vector2(0.8f, 0.447f));
            AddIllustratedLogos(environment);
        }

        /// <summary>
        /// The client's illustrated "M": large curved panels on the cabin's outer skin either side of the entrance (seen while
        /// walking in from the room) and posters on the room's side walls and on the wall behind the start mark.
        /// </summary>
        private static void AddIllustratedLogos(Transform environment)
        {
            if (!System.IO.File.Exists(BrandAssets.MIllustration))
            {
                Debug.LogWarning("[MultiTravel] Illustrated logo missing: " + BrandAssets.MIllustration);
                return;
            }

            var material = BrandAssets.LogoMaterial(BrandAssets.MIllustration);
            var root = new GameObject("Illustrated logos").transform;
            root.SetParent(environment, false);
            const float aspect = BrandAssets.MIllustrationAspect;

            // Cabin outer skin (radius ~2.0 m) beside the rear entrance (azimuth 180 deg = towards the start mark).
            const float skinRadius = 2.03f;
            foreach (float azimuth in new[] { 132f, -132f })
            {
                var panel = CreateCurvedLogo("Cabin skin logo", root, material, skinRadius, 1.22f, 1.15f, 1.15f / aspect, outward: true);
                panel.transform.rotation = Quaternion.Euler(0f, azimuth, 0f);
            }

            // Room posters (room inner walls: x = +-4.23, back wall z = -7.93); the left wall carries the window and the rail
            // further back, so its poster sits towards the front.
            CreateLogoQuad("Room poster right", root, material, new Vector3(4.215f, 1.6f, -1.7f), Quaternion.Euler(0f, 90f, 0f), new Vector2(1.6f, 1.6f / aspect));
            CreateLogoQuad("Room poster left", root, material, new Vector3(-4.215f, 1.6f, 0.7f), Quaternion.Euler(0f, -90f, 0f), new Vector2(1.6f, 1.6f / aspect));
            CreateLogoQuad("Room poster back", root, material, new Vector3(0f, 1.65f, -7.915f), Quaternion.Euler(0f, 180f, 0f), new Vector2(2.0f, 2.0f / aspect));
        }

        /// <summary>Strip of a cylinder around the vertical axis through the origin, facing the participant (UV 0..1).</summary>
        /// <summary>Width / height of the horizontal logo artwork.</summary>
        private const float LogoAspect = 0.92f / 0.246f;

        private static GameObject CreateCurvedLogo(string name, Transform parent, Material material, float radius, float centreY, float width, float height,
            bool outward = false)
        {
            const int segments = 24;
            var vertices = new Vector3[(segments + 1) * 2];
            var uv = new Vector2[vertices.Length];
            var normals = new Vector3[vertices.Length];
            float span = width / radius;
            for (int i = 0; i <= segments; i++)
            {
                float u = i / (float)segments;
                float angle = (u - 0.5f) * span;
                var dir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                for (int j = 0; j < 2; j++)
                {
                    int k = i * 2 + j;
                    vertices[k] = dir * radius + Vector3.up * (centreY + (j == 0 ? -height : height) * 0.5f);
                    // Outward strips (cabin's outer skin) are seen from outside: mirror u so the artwork reads correctly.
                    uv[k] = new Vector2(outward ? 1f - u : u, j);
                    normals[k] = outward ? dir : -dir;
                }
            }

            var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                int k = i * 2;
                int t = i * 6;
                // Clockwise seen from the participant (origin side), so the face points at the participant; reversed outward.
                if (outward)
                {
                    triangles[t] = k; triangles[t + 1] = k + 2; triangles[t + 2] = k + 1;
                    triangles[t + 3] = k + 1; triangles[t + 4] = k + 2; triangles[t + 5] = k + 3;
                }
                else
                {
                    triangles[t] = k; triangles[t + 1] = k + 1; triangles[t + 2] = k + 2;
                    triangles[t + 3] = k + 1; triangles[t + 4] = k + 3; triangles[t + 5] = k + 2;
                }
            }

            var mesh = new Mesh { name = name };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.normals = normals;
            mesh.triangles = triangles;
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        private static void CreateLogoQuad(string name, Transform parent, Material material, Vector3 position, Quaternion rotation, Vector2 size)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(parent, false);
            quad.transform.SetPositionAndRotation(position, rotation);
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);
            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            GameObjectUtility.SetStaticEditorFlags(quad, StaticEditorFlags.BatchingStatic);
        }

        private static string ZoneTitle(DisplayZone zone)
        {
            switch (zone)
            {
                case DisplayZone.Hanging: return "ASKILIK";
                case DisplayZone.Folded: return "KATLI GİYSİ";
                case DisplayZone.Shoes: return "AYAKKABI";
                case DisplayZone.Accessories: return "AKSESUAR";
                case DisplayZone.Jewellery: return "TAKI";
                case DisplayZone.Business: return "İŞ MASASI";
                case DisplayZone.Leisure: return "TATİL MASASI";
                default: return null;
            }
        }

        /// <summary>
        /// MultiTravel logo on every header band of the shelf wall (<c>UI.header_&lt;zone&gt;</c> markers from the art). The
        /// shelves deliberately carry no category names (business / leisure …): the participant decides what a trip needs.
        /// </summary>
        private static void AddZoneLabels(List<SpawnSlot> slots, Transform environment)
        {
            var root = new GameObject("Header logos").transform;
            root.SetParent(environment, false);
            var material = BrandAssets.LogoMaterial(BrandAssets.HorizontalLight);
            foreach (DisplayZone zone in Enum.GetValues(typeof(DisplayZone)))
            {
                if (ZoneTitle(zone) == null)
                {
                    continue;
                }

                var marker = FindMarker(environment, "UI.header_" + zone.ToString().ToLowerInvariant());
                if (marker == null)
                {
                    continue;
                }

                // Curved strip on the concave header band, just in front of it, centred on the marker's azimuth.
                float radius = new Vector2(marker.position.x, marker.position.z).magnitude - 0.012f;
                float width = Mathf.Clamp(Mathf.Abs(marker.lossyScale.x) * 0.8f, 0.2f, 0.56f);
                float height = width / LogoAspect;
                var logo = CreateCurvedLogo("Header logo " + zone, root, material, radius, marker.position.y, width, height);
                logo.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(marker.position.x, marker.position.z) * Mathf.Rad2Deg, 0f);
            }
        }

        /// <summary>
        /// Shelf-end decoration from the art markers: the MultiTravel logo on the front of every storage box
        /// (<c>UI.boxlogo.*</c>) and a ceramic vase (Poly Haven) at every <c>DECOR.plant.*</c>.
        /// </summary>
        private static void AddShelfDecor(Transform environment)
        {
            var root = new GameObject("Shelf decor").transform;
            root.SetParent(environment, false);
            var logo = BrandAssets.LogoMaterial(BrandAssets.VerticalLight);
            int plants = 0;
            foreach (var t in environment.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("UI.boxlogo.", StringComparison.Ordinal))
                {
                    // Face the participant (cabin centre): the quad's front side looks along -Z.
                    var away = t.position;
                    away.y = 0f;
                    CreateLogoQuad("Box logo", root, logo, t.position, Quaternion.LookRotation(away.normalized, Vector3.up), new Vector2(0.16f, 0.16f / 1.787f));
                }
                else if (t.name.StartsWith("DECOR.plant.", StringComparison.Ordinal))
                {
                    // The Poly Haven calathea file holds several small plants (too scattered at shelf scale): a ceramic vase.
                    ImportFurniture("ceramic_vase_01", root, t.position, 0.13f, 37f * ++plants, worldMeasure: true);
                }
            }
        }

        private static TMPro.TMP_FontAsset FontAssetGeneratorLoad()
        {
            return MultiTravel.EditorTools.Fonts.FontAssetGenerator.Load();
        }

        public static bool TryParseZone(string token, out DisplayZone zone)
        {
            foreach (DisplayZone value in Enum.GetValues(typeof(DisplayZone)))
            {
                if (string.Equals(value.ToString(), token, StringComparison.OrdinalIgnoreCase))
                {
                    zone = value;
                    return value != DisplayZone.Any;
                }
            }

            zone = DisplayZone.Any;
            return false;
        }

        private static void ValidateZoneCapacity(ProductCatalog catalog, SpawnSlotLayout layout)
        {
            var errors = new List<string>();
            // The shelf wall is exact-fit: every item has a slot, spare slots exist only where genders differ (OVERHAUL_PLAN asked for 20 %).
            if (!ZoneCapacityValidator.Validate(ZoneCapacityValidator.EntriesFor(catalog.Products), layout.CountSlotsPerZone(), errors, 1f))
            {
                throw new InvalidOperationException("Zone capacity check failed:\n" + string.Join("\n", errors));
            }
        }

        private static int CountZones(List<SpawnSlot> slots)
        {
            var zones = new HashSet<DisplayZone>();
            foreach (var s in slots)
            {
                zones.Add(s.Zone);
            }

            return zones.Count;
        }

        // ------------------------------------------------------------------------------------------------ suitcase

        private static SuitcaseController BuildSuitcase(ItemPool pool, out SuitcaseLid lid, out Transform practiceSpawn)
        {
            var root = new GameObject("Open suitcase");
            root.transform.position = SuitcaseShift;
            var model = InstantiateModel(SuitcaseModel, root.transform);
            model.name = "Suitcase model";

            var lidPivot = FindRequired(model.transform, "PIVOT.lid");
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (IsMarker(mf.transform) || mf.sharedMesh == null)
                {
                    continue;
                }

                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            }

            // Moving colliders (lid) belong to a kinematic body so the physics engine treats them as dynamic geometry.
            var lidBody = lidPivot.gameObject.AddComponent<Rigidbody>();
            lidBody.isKinematic = true;
            lidBody.useGravity = false;

            var volumeMarker = FindRequired(model.transform, "VOL.placement");
            var volume = volumeMarker.gameObject.AddComponent<BoxCollider>();
            volume.isTrigger = true;
            volume.size = Vector3.one * VolumeEmptyUnitSize;
            volume.center = Vector3.zero;

            var slots = new List<SuitcaseSlot>();
            foreach (var marker in FindMarkers(model.transform, "PACK."))
            {
                var parts = marker.name.Split('.');
                if (parts.Length < 3 || !TryParsePacked(parts[1], out var kind))
                {
                    Debug.LogWarning($"[MultiTravel] Packing marker '{marker.name}' has an unknown kind; skipped.");
                    continue;
                }

                bool stacked = kind == PackedKind.Flat || kind == PackedKind.Top;
                int layers = stacked ? FlatLayers : 1;
                SuitcaseSlot below = null;
                for (int layer = 0; layer < layers; layer++)
                {
                    var slotGo = layer == 0 ? marker.gameObject : new GameObject($"{marker.name}.L{layer}");
                    if (layer > 0)
                    {
                        slotGo.transform.SetParent(marker.parent, false);
                        slotGo.transform.localPosition = marker.localPosition + marker.parent.InverseTransformVector(Vector3.up * (layer * LayerStep));
                        slotGo.transform.localRotation = marker.localRotation;
                    }

                    var slot = slotGo.AddComponent<SuitcaseSlot>();
                    slot.SetKind(kind);
                    slot.SetBelow(below);
                    slots.Add(slot);
                    below = slot;
                }
            }

            if (slots.Count == 0)
            {
                throw new InvalidOperationException("No PACK.* markers found in the suitcase model.");
            }

            var suitcase = root.AddComponent<SuitcaseController>();
            suitcase.Configure(volume, slots);
            suitcase.SetPool(pool);

            lid = root.AddComponent<SuitcaseLid>();
            lid.Configure(lidPivot, suitcase);
            var straps = StrapLift.FindDeep(model.transform, "Straps");
            if (straps != null)
            {
                root.AddComponent<StrapLift>().Configure(suitcase, straps, 0.05f, 0.06f);
            }

            // Practice tag hovers above the front edge of the suitcase, at chest height and within easy reach.
            var spawn = new GameObject("Practice item spawn").transform;
            spawn.SetParent(root.transform, false);
            var vb = volume.bounds;
            spawn.position = new Vector3(vb.center.x, vb.max.y + 0.22f, vb.min.z - 0.05f);
            practiceSpawn = spawn;
            return suitcase;
        }

        public static bool TryParsePacked(string token, out PackedKind kind)
        {
            foreach (PackedKind value in Enum.GetValues(typeof(PackedKind)))
            {
                if (string.Equals(value.ToString(), token, StringComparison.OrdinalIgnoreCase))
                {
                    kind = value;
                    return true;
                }
            }

            kind = PackedKind.Flat;
            return false;
        }

        private static void PlaceConfirmButton(GameplayDirector director, Transform suitcaseRoot)
        {
            var root = GameObject.Find("Manual confirmation");
            if (root == null)
            {
                return;
            }

            // Hidden unless the completion mode allows a manual confirmation; sits on the rack's right edge.
            // World bounds: the suitcase root is moved (SuitcaseShift), root-local bounds would leave the button behind.
            var bounds = WorldRendererBounds(suitcaseRoot.gameObject);
            root.transform.position = new Vector3(bounds.max.x + 0.16f, bounds.min.y + 0.58f, bounds.min.z + 0.08f);
        }

        // ------------------------------------------------------------------------------------------------ lighting

        private static void ConfigureEnvironmentLighting()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.42f, 0.47f, 0.58f);
            RenderSettings.ambientEquatorColor = new Color(0.36f, 0.36f, 0.40f);
            RenderSettings.ambientGroundColor = new Color(0.20f, 0.19f, 0.19f);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = false;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;

            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
            if (settings == null)
            {
                settings = new LightingSettings { name = "MainLighting" };
                GeneratedAssetUtil.EnsureFolder(GeneratedAssetUtil.RootFolder + "/Scenes");
                AssetDatabase.CreateAsset(settings, LightingSettingsPath);
            }

            settings.bakedGI = true;
            settings.realtimeGI = false;
            settings.mixedBakeMode = MixedLightingMode.Shadowmask;
            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            settings.lightmapResolution = 20f;
            settings.lightmapMaxSize = 2048;
            settings.lightmapPadding = 4;
            settings.ao = true;
            settings.aoMaxDistance = 0.8f;
            settings.directionalityMode = LightmapsMode.NonDirectional;
            settings.maxBounces = 2;
            EditorUtility.SetDirty(settings);
            Lightmapping.lightingSettings = settings;
        }

        private static List<LightGroup> BuildLightGroups(Transform environment, Transform suitcaseRoot)
        {
            var general = new LightGroup { Name = "Stage", Role = LightGroupRole.General };
            var suitcaseKey = new LightGroup { Name = "Suitcase key", Role = LightGroupRole.SuitcaseKey };
            var wardrobe = new LightGroup { Name = "Wardrobe spots", Role = LightGroupRole.WardrobeSpots };
            var suitcaseSpot = new LightGroup { Name = "Suitcase spot", Role = LightGroupRole.SuitcaseSpot };
            var led = new LightGroup { Name = "LED strips", Role = LightGroupRole.Led };

            // Key: one realtime soft-shadow directional light for crisp contact shadows on items and hands.
            var key = new GameObject("Stage key").AddComponent<Light>();
            key.type = LightType.Directional;
            key.lightmapBakeType = LightmapBakeType.Mixed;
            key.intensity = 0.4f;
            key.color = new Color(1f, 0.96f, 0.92f);
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.75f;
            key.transform.rotation = Quaternion.Euler(52f, 28f, 0f);
            general.Lights.Add(key);

            var hints = FindMarkers(environment, "LIGHT.");
            foreach (var hint in hints)
            {
                string n = hint.name.ToLowerInvariant();
                var light = hint.gameObject.AddComponent<Light>();
                light.type = n.Contains("spot") ? LightType.Spot : LightType.Point;
                light.color = new Color(1f, 0.92f, 0.82f);
                light.range = n.Contains("room_") ? 8f : 4f;
                light.spotAngle = 55f;
                light.innerSpotAngle = 30f;
                // Cabin lights match the room level (they used to make the cabin look much brighter than the room).
                light.intensity = n.Contains("room_") ? 1.1f : 0.9f;
                light.shadows = LightShadows.None;
                light.lightmapBakeType = LightmapBakeType.Baked;
                if (n.Contains("suitcase"))
                {
                    light.lightmapBakeType = LightmapBakeType.Mixed;
                    light.shadows = LightShadows.Soft;
                    light.intensity = 3f;
                    suitcaseSpot.Lights.Add(light);
                }
                else if (n.Contains("wardrobe") || n.Contains("hanging") || n.Contains("folded") || n.Contains("door"))
                {
                    light.lightmapBakeType = LightmapBakeType.Mixed;
                    wardrobe.Lights.Add(light);
                }
                else
                {
                    general.Lights.Add(light);
                }
            }

            if (suitcaseSpot.Lights.Count == 0)
            {
                var spot = new GameObject("Suitcase spot").AddComponent<Light>();
                spot.type = LightType.Spot;
                spot.lightmapBakeType = LightmapBakeType.Mixed;
                var target = WorldRendererBounds(suitcaseRoot.gameObject).center;
                spot.transform.position = target + new Vector3(0f, 1.6f, -0.35f);
                spot.transform.LookAt(target);
                spot.spotAngle = 55f;
                spot.innerSpotAngle = 28f;
                spot.range = 4f;
                spot.intensity = 3f;
                spot.color = new Color(1f, 0.93f, 0.84f);
                spot.shadows = LightShadows.Soft;
                suitcaseSpot.Lights.Add(spot);
            }

            // The suitcase key is the warm fill used by the tutorial preset.
            var fill = new GameObject("Suitcase key").AddComponent<Light>();
            fill.type = LightType.Point;
            fill.lightmapBakeType = LightmapBakeType.Realtime;
            fill.transform.position = new Vector3(0f, 1.55f, 0.1f) + SuitcaseShift;
            fill.range = 2.2f;
            fill.intensity = 1.1f;
            fill.color = new Color(1f, 0.9f, 0.78f);
            fill.shadows = LightShadows.None;
            suitcaseKey.Lights.Add(fill);

            foreach (var r in environment.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.StartsWith("EMISSIVE.", StringComparison.Ordinal))
                {
                    led.Emissives.Add(r);
                }
            }

            return new List<LightGroup> { general, suitcaseKey, wardrobe, suitcaseSpot, led };
        }

        private static void AddLightProbes(Transform environment)
        {
            var go = new GameObject("Light probes");
            var group = go.AddComponent<LightProbeGroup>();
            var positions = new List<Vector3>();
            // Covers the widened cabin (shelf wall radius 1.8 m, backdrop 2.25 m).
            for (float x = -1.8f; x <= 1.81f; x += 0.45f)
            {
                for (float y = 0.5f; y <= 2.01f; y += 0.5f)
                {
                    for (float z = -1.35f; z <= 2.06f; z += 0.45f)
                    {
                        positions.Add(new Vector3(x, y, z));
                    }
                }
            }

            group.probePositions = positions.ToArray();
        }

        private static void AddReflectionProbe()
        {
            var probe = new GameObject("Stage reflection").AddComponent<ReflectionProbe>();
            probe.transform.position = new Vector3(0f, 1.3f, 0.35f);
            probe.size = new Vector3(5.0f, 2.8f, 4.8f);
            probe.boxProjection = true;
            probe.mode = ReflectionProbeMode.Baked;
            probe.resolution = 256;
            probe.hdr = true;
        }

        private static void AddPostProcessing()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }

            if (!profile.TryGet(out Tonemapping tonemapping))
            {
                tonemapping = profile.Add<Tonemapping>(true);
                AssetDatabase.AddObjectToAsset(tonemapping, profile);
            }

            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = TonemappingMode.Neutral;

            if (!profile.TryGet(out Bloom bloom))
            {
                bloom = profile.Add<Bloom>(true);
                AssetDatabase.AddObjectToAsset(bloom, profile);
            }

            bloom.threshold.overrideState = true;
            bloom.threshold.value = 1.1f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.35f;
            bloom.highQualityFiltering.overrideState = true;
            bloom.highQualityFiltering.value = false;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var volume = new GameObject("Post processing").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;
            volume.sharedProfile = profile;
        }

        /// <summary>Bakes lightmaps, light probes and the reflection probe of the open Main scene (synchronous).</summary>
        public static bool BakeLighting()
        {
            var scene = EditorSceneManager.OpenScene(ProjectValidator.MainScenePath, OpenSceneMode.Single);
            bool ok = Lightmapping.Bake();
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[MultiTravel] Lighting bake {(ok ? "completed" : "FAILED")}.");
            return ok;
        }

        // ------------------------------------------------------------------------------------------------ decor

        private static void AddDecor(Transform environment)
        {
            // CC0 Poly Haven dressing outside the reach zone (ASSET_MANIFEST.md): plants frame the stage, a vase and
            // stationery dress the business table edge without taking item slots.
            ImportFurniture("calathea_orbifolia_01", environment, new Vector3(-3.75f, 0f, 2.25f), 0.7f, 20f);
            ImportFurniture("calathea_orbifolia_01", environment, new Vector3(3.75f, 0f, 2.25f), 0.7f, -20f);
            ImportFurniture("calathea_orbifolia_01", environment, new Vector3(-3.8f, 0f, -7.5f), 0.7f, 40f);
            ImportFurniture("modern_ceiling_lamp_01", environment, new Vector3(0f, 2.95f, 0.45f), 0.55f, 0f, hang: true);
            ImportFurniture("modern_ceiling_lamp_01", environment, new Vector3(0f, 2.95f, -5.2f), 0.55f, 0f, hang: true);
        }

        /// <summary>The hotel room around the cabin: bed, nightstands with lamps, rug, bench, mirror, curtains and a clothes rack.</summary>
        private static void AddRoomFurniture(Transform environment)
        {
            PlaceModel("rug", environment, new Vector3(0f, 0f, -5.4f), 0f);
            PlaceModel("bed", environment, new Vector3(3.15f, 0f, -5.0f), 90f);
            foreach (float z in new[] { -6.3f, -3.7f })
            {
                PlaceModel("nightstand", environment, new Vector3(4.0f, 0f, z), -90f);
                PlaceModel("table-lamp", environment, new Vector3(4.0f, 0.55f, z), 0f).name = "Table lamp";
            }

            PlaceModel("luggage-bench", environment, new Vector3(1.6f, 0f, -5.0f), 90f);
            PlaceModel("curtains", environment, new Vector3(-4.2f, 0f, -4.25f), 90f).name = "Curtains";
            PlaceModel("floor-mirror", environment, new Vector3(-3.3f, 0f, -7.85f), 180f);
            PlaceModel("hanger-rail-decor", environment, new Vector3(-3.95f, 0f, -1.4f), 90f);
        }

        private static GameObject PlaceModel(string name, Transform parent, Vector3 position, float yaw)
        {
            var go = InstantiateModel(name, parent);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            return go;
        }

        private static void ImportFurniture(string id, Transform parent, Vector3 position, float width, float yaw, bool hang = false,
            bool worldMeasure = false)
        {
            var folder = $"{ArtImporter.PolyHavenFolder}/{id}";
            var guids = AssetDatabase.FindAssets("t:Model", new[] { folder });
            if (guids.Length == 0)
            {
                Debug.LogWarning($"[MultiTravel] Decor '{id}' not found under {folder}; skipped.");
                return;
            }

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[0]));
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            go.name = id;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * go.transform.localRotation;
            // worldMeasure: some Poly Haven roots carry an import scale (e.g. the ceramic vase) that the root-local bounds do
            // not see, which blew the model up; the room furniture keeps the original root-local measurement.
            var b = worldMeasure ? WorldRendererBounds(go) : ArtImporter.RendererBounds(go);
            float horizontal = Mathf.Max(b.size.x, b.size.z);
            go.transform.localScale *= width / Mathf.Max(0.001f, horizontal);
            var wb = worldMeasure ? WorldRendererBounds(go) : ArtImporter.RendererBounds(go);
            float y = hang ? position.y - wb.size.y : position.y;
            go.transform.position += new Vector3(position.x, y, position.z) - new Vector3(wb.center.x, wb.min.y, wb.center.z);
            RemapPolyHavenMaterials(go, id);
        }

        private static Bounds WorldRendererBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return new Bounds(root.transform.position, Vector3.one * 0.1f);
            }

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        private static void RemapPolyHavenMaterials(GameObject go, string id)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string original = mats[i] != null ? mats[i].name : id;
                    string safe = original.Replace('/', '_').Replace('\\', '_');
                    var existing = AssetDatabase.LoadAssetAtPath<Material>($"{ArtImporter.ArtMaterialsFolder}/PH_{id}_{safe}.mat")
                                   ?? AssetDatabase.LoadAssetAtPath<Material>($"{GeneratedAssetUtil.MaterialsFolder}/PH_{id}_{safe}.mat");
                    if (existing != null)
                    {
                        mats[i] = existing;
                    }
                }

                r.sharedMaterials = mats;
            }
        }

        // ------------------------------------------------------------------------------------------------ helpers

        public static GameObject InstantiateModel(string name, Transform parent)
        {
            var path = $"{EnvironmentFolder}/{name}.fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null)
            {
                throw new System.IO.FileNotFoundException($"Environment model missing: {path}. Run tools/blender/mt_build_assets.py and MultiTravel/Generate/Import Production Art.");
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            go.name = name;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool emissive = r.name.StartsWith("EMISSIVE.", StringComparison.Ordinal);
                r.shadowCastingMode = emissive ? ShadowCastingMode.Off : ShadowCastingMode.On;
                if (r.name.StartsWith("UI.", StringComparison.Ordinal))
                {
                    r.enabled = false; // text anchors only
                }
            }

            return go;
        }

        /// <summary>Static mesh colliders for the set (items rest on shelves, tables and the floor and cannot pass walls).</summary>
        private static void AddStaticColliders(Transform environment)
        {
            foreach (var mf in environment.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || IsMarker(mf.transform) || mf.name.StartsWith("EMISSIVE.", StringComparison.Ordinal))
                {
                    continue;
                }

                if (mf.GetComponent<Collider>() == null)
                {
                    mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                }
            }
        }

        private static bool IsMarker(Transform t)
        {
            string n = t.name;
            return n.StartsWith("SLOT.", StringComparison.Ordinal) || n.StartsWith("PACK.", StringComparison.Ordinal)
                || n.StartsWith("VOL.", StringComparison.Ordinal) || n.StartsWith("UI.", StringComparison.Ordinal)
                || n.StartsWith("HOOK.", StringComparison.Ordinal) || n.StartsWith("LIGHT.", StringComparison.Ordinal)
                || n.StartsWith("PIVOT.", StringComparison.Ordinal);
        }

        public static List<Transform> FindMarkers(Transform root, string prefix)
        {
            var result = new List<Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    result.Add(t);
                }
            }

            result.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return result;
        }

        public static Transform FindMarker(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                {
                    return t;
                }
            }

            return null;
        }

        private static Transform FindRequired(Transform root, string name)
        {
            var t = FindMarker(root, name);
            if (t == null)
            {
                throw new InvalidOperationException($"Marker '{name}' not found under '{root.name}'. Re-export the art.");
            }

            if (name.StartsWith("UI.", StringComparison.Ordinal))
            {
                FaceTextAnchor(t);
            }

            return t;
        }

        /// <summary>
        /// World-space text reads correctly when the anchor's +Z points away from the viewer. FBX empties import with
        /// +Z toward the participant (the face normal), so such anchors are turned around.
        /// </summary>
        private static void FaceTextAnchor(Transform anchor)
        {
            var toEye = new Vector3(0f, 1.7f, 0f) - anchor.position;
            if (Vector3.Dot(anchor.forward, toEye) > 0f)
            {
                anchor.Rotate(0f, 180f, 0f, Space.Self);
            }
        }

        private static void SetSerialized(Object target, string field, bool value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning($"[MultiTravel] Field '{field}' not found on {target.GetType().Name}.");
                return;
            }

            prop.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void MarkStatic(GameObject root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(t.gameObject,
                    StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic
                    | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            }
        }
    }
}
