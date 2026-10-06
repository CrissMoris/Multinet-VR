using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MultiTravel.Core.Products;
using MultiTravel.EditorTools.Data;
using MultiTravel.Gameplay.Items;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MultiTravel.EditorTools.Art
{
    /// <summary>
    /// Imports the production art: Poly Haven (CC0) textures/models and the original Blender models built by
    /// <c>tools/blender/mt_build_assets.py</c>. Builds URP/Lit materials from <c>Art/materials.json</c>, remaps model
    /// materials onto them and (re)builds the product prefabs in place (stable GUIDs). Idempotent.
    /// <para>
    /// Product prefabs (OVERHAUL_PLAN §4/§5): model + box collider + anchor + Rigidbody + XRGrabInteractable + ProductItem,
    /// plus an inverted-hull hover outline (<c>Outline</c> children, material <c>MT_Outline</c>), a <c>Hanging</c> /
    /// <c>Folded</c> visual pair with <see cref="ItemVisualVariant"/> when <c>&lt;id&gt;-hanging.fbx</c> exists, an invisible
    /// enlarged <c>GrabCollider</c> for items smaller than 5 cm in any axis and the grip <c>Attach</c> child. The practice
    /// item (<c>Data/Practice/practice-tag</c>) gets a prefab the same way.
    /// </para>
    /// </summary>
    public static class ArtImporter
    {
        public const string ArtFolder = GeneratedAssetUtil.RootFolder + "/Art";
        public const string PaletteJsonPath = ArtFolder + "/materials.json";
        public const string ModelsManifestPath = ArtFolder + "/Models/models.json";
        public const string ArtMaterialsFolder = ArtFolder + "/Materials";
        public const string ProductModelsFolder = ArtFolder + "/Models/Products";
        public const string EnvironmentModelsFolder = ArtFolder + "/Models/Environment";
        public const string PolyHavenFolder = GeneratedAssetUtil.RootFolder + "/ThirdParty/PolyHaven";
        public const string PolyHavenTexturesFolder = PolyHavenFolder + "/Textures";
        private const string LitShader = "Universal Render Pipeline/Lit";
        private const string UnlitShader = "Universal Render Pipeline/Unlit";

        /// <summary>Hover outline material (unlit teal #19B394, Cull Front).</summary>
        public const string OutlineMaterialPath = ArtMaterialsFolder + "/MT_Outline.mat";

        /// <summary>Folder of the generated outline hull meshes.</summary>
        public const string OutlineMeshesFolder = GeneratedAssetUtil.MeshesFolder + "/Outline";

        /// <summary>Suffix of the hanging display variant model (<c>&lt;id&gt;-hanging.fbx</c>).</summary>
        public const string HangingSuffix = "-hanging";

        /// <summary>Outline colour (#19B394).</summary>
        public static readonly Color OutlineColor = new Color32(0x19, 0xB3, 0x94, 0xFF);

        /// <summary>Product id → Poly Haven model folder (CC0) for products not modelled in Blender.</summary>
        public static readonly IReadOnlyDictionary<string, string> PolyHavenProducts = new Dictionary<string, string>
        {
            { "glasses", "round_spectacles" },
            { "sunglasses", "round_spectacles" },
            { "rubber-duck", "rubber_duck_toy" },
            { "football", "football" },
            { "ukulele", "Ukulele_01" },
            { "garden-gnome", "garden_gnome" },
            { "binoculars", "binoculars" },
        };

        /// <summary>Optional per-product pose fix (degrees, applied to the model child) so items rest naturally on a shelf.</summary>
        private static readonly Dictionary<string, Vector3> ModelRotation = new Dictionary<string, Vector3>
        {
            { "ukulele", new Vector3(-90f, 0f, 0f) },
        };

        /// <summary>Optional largest-dimension override (metres) for Poly Haven models whose real size is impractical in VR.</summary>
        private static readonly Dictionary<string, float> ModelMaxSize = new Dictionary<string, float>
        {
            { "garden-gnome", 0.32f },
            { "rubber-duck", 0.16f },
            { "football", 0.22f },
            { "binoculars", 0.18f },
        };

        private static JObject palette;

        [MenuItem("MultiTravel/Generate/Import Production Art", priority = 120)]
        public static void ImportAllMenu()
        {
            ImportAll().Log();
        }

        public static GeneratorReport ImportAll()
        {
            var report = new GeneratorReport("Import production art");
            try
            {
                palette = null;
                ConfigureTextures(report);
                BuildPaletteMaterials(report);
                ConfigureArtModels(report);
                ConfigurePolyHavenModels(report);
                BuildProductPrefabs(report);
                AssetDatabase.SaveAssets();
            }
            catch (Exception ex)
            {
                report.AddError(ex.ToString());
            }

            return report;
        }

        // ------------------------------------------------------------------------------------------------- palette

        private static JObject Palette
        {
            get
            {
                if (palette == null)
                {
                    var json = File.ReadAllText(GeneratedAssetUtil.ToAbsolutePath(PaletteJsonPath));
                    palette = (JObject)JObject.Parse(json)["materials"];
                }

                return palette;
            }
        }

        public static string MaterialPath(string key) => $"{ArtMaterialsFolder}/{key}.mat";

        /// <summary>Palette material for <paramref name="key"/> (materials.json), created on first use.</summary>
        public static Material GetMaterial(string key)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(key));
            return existing != null ? existing : UpsertPaletteMaterial(key);
        }

        private static void BuildPaletteMaterials(GeneratorReport report)
        {
            GeneratedAssetUtil.EnsureFolder(ArtMaterialsFolder);
            foreach (var prop in Palette.Properties())
            {
                UpsertPaletteMaterial(prop.Name);
                report.Updated.Add(MaterialPath(prop.Name));
            }
        }

        private static Material UpsertPaletteMaterial(string key)
        {
            var spec = Palette[key] as JObject ?? throw new ArgumentException($"Unknown palette material '{key}'.");
            GeneratedAssetUtil.EnsureFolder(ArtMaterialsFolder);
            var shader = Shader.Find(LitShader) ?? throw new InvalidOperationException("URP Lit shader not found.");
            return GeneratedAssetUtil.UpsertMaterial(MaterialPath(key), shader, m =>
            {
                string textureSet = (string)spec["texture"] ?? string.Empty;
                float tile = Mathf.Max(0.001f, (float?)spec["tile"] ?? 1f);
                Texture2D baseMap = null;
                Texture2D normalMap = null;
                if (!string.IsNullOrEmpty(textureSet))
                {
                    baseMap = FindTexture($"{PolyHavenTexturesFolder}/{textureSet}", "_diff_");
                    string tintHex = ((string)spec["tint"] ?? "FFFFFF").ToUpperInvariant();
                    if ((bool?)spec["albedo"] == false)
                    {
                        baseMap = null; // surface relief only (normal map); colour comes from the tint
                    }
                    else if (baseMap != null && tintHex != "FFFFFF")
                    {
                        // Tinted materials use a desaturated copy so the palette colour, not the scan's own colour, shows.
                        baseMap = NeutralAlbedo(baseMap, textureSet);
                    }
                    normalMap = FindTexture($"{PolyHavenTexturesFolder}/{textureSet}", "_nor_gl_");
                    if (baseMap == null && normalMap == null)
                    {
                        Debug.LogWarning($"[MultiTravel] Palette '{key}': texture set '{textureSet}' not found under {PolyHavenTexturesFolder}.");
                    }
                }

                ApplyLit(m, baseMap, normalMap, Hex((string)spec["tint"]), (float?)spec["metallic"] ?? 0f, (float?)spec["smoothness"] ?? 0.3f,
                    new Vector2(1f / tile, 1f / tile));
                float alpha = (float?)spec["alpha"] ?? 1f;
                if (alpha < 0.999f)
                {
                    MakeTransparent(m, alpha);
                }

                string emission = (string)spec["emission"];
                if (!string.IsNullOrEmpty(emission))
                {
                    float intensity = (float?)spec["emissionIntensity"] ?? 1f;
                    var e = Hex(emission) * intensity;
                    m.SetColor("_EmissionColor", new Color(e.r, e.g, e.b, 1f));
                    m.EnableKeyword("_EMISSION");
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
            });
        }

        private static void ApplyLit(Material m, Texture2D baseMap, Texture2D normalMap, Color tint, float metallic, float smoothness, Vector2 tiling)
        {
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_ZWrite", 1f);
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", (float)BlendMode.Zero);
            m.SetOverrideTag("RenderType", "Opaque");
            m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = -1;
            m.SetFloat("_WorkflowMode", 1f);
            m.SetColor("_BaseColor", tint);
            m.SetTexture("_BaseMap", baseMap);
            m.SetTextureScale("_BaseMap", tiling);
            m.SetTexture("_BumpMap", normalMap);
            m.SetFloat("_BumpScale", 1f);
            if (normalMap != null)
            {
                m.EnableKeyword("_NORMALMAP");
            }
            else
            {
                m.DisableKeyword("_NORMALMAP");
            }

            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            m.SetColor("_EmissionColor", Color.black);
            m.DisableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            m.enableInstancing = true;
        }

        /// <summary>URP/Lit alpha-blended surface (glass, lenses).</summary>
        public static void MakeTransparent(Material m, float alpha)
        {
            var c = m.GetColor("_BaseColor");
            c.a = alpha;
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        private static Color Hex(string hex)
        {
            if (string.IsNullOrEmpty(hex) || !ColorUtility.TryParseHtmlString("#" + hex.TrimStart('#'), out var c))
            {
                return Color.white;
            }

            return c;
        }

        public const string NeutralTexturesFolder = ArtFolder + "/Textures";

        /// <summary>
        /// Grey-scale copy of a colour texture, normalised to a mean luminance of 0.82, so <c>_BaseColor</c> sets the hue.
        /// Written once per texture set to Art/Textures/&lt;set&gt;_neutral.png (sRGB, mipmapped).
        /// </summary>
        private static Texture2D NeutralAlbedo(Texture2D source, string textureSet)
        {
            GeneratedAssetUtil.EnsureFolder(NeutralTexturesFolder);
            var path = $"{NeutralTexturesFolder}/{textureSet}_neutral.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
            {
                return existing;
            }

            var srcPath = AssetDatabase.GetAssetPath(source);
            var bytes = File.ReadAllBytes(GeneratedAssetUtil.ToAbsolutePath(srcPath));
            var readable = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            readable.LoadImage(bytes);
            var pixels = readable.GetPixels32();
            double sum = 0;
            var lum = new float[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                lum[i] = (0.2126f * pixels[i].r + 0.7152f * pixels[i].g + 0.0722f * pixels[i].b) / 255f;
                sum += lum[i];
            }

            float mean = Mathf.Max(0.01f, (float)(sum / pixels.Length));
            float gain = 0.82f / mean;
            for (int i = 0; i < pixels.Length; i++)
            {
                byte v = (byte)Mathf.Clamp(Mathf.RoundToInt(lum[i] * gain * 255f), 0, 255);
                pixels[i] = new Color32(v, v, v, 255);
            }

            readable.SetPixels32(pixels);
            readable.Apply();
            File.WriteAllBytes(GeneratedAssetUtil.ToAbsolutePath(path), readable.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(readable);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.sRGBTexture = true;
                importer.mipmapEnabled = true;
                importer.maxTextureSize = 1024;
                importer.anisoLevel = 4;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Texture2D FindTexture(string folder, string marker)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                return null;
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(path).IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                }
            }

            return null;
        }

        // ------------------------------------------------------------------------------------------------- textures

        private static void ConfigureTextures(GeneratorReport report)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { PolyHavenFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                {
                    continue;
                }

                string file = Path.GetFileName(path).ToLowerInvariant();
                bool normal = file.Contains("_nor_gl") || file.Contains("_normal");
                bool linear = normal || file.Contains("_arm") || file.Contains("_rough") || file.Contains("_metal") || file.Contains("_ao_");
                bool changed = false;
                var wantedType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (importer.textureType != wantedType) { importer.textureType = wantedType; changed = true; }
                if (importer.sRGBTexture == linear) { importer.sRGBTexture = !linear; changed = true; }
                if (importer.maxTextureSize != 1024) { importer.maxTextureSize = 1024; changed = true; }
                if (importer.mipmapEnabled == false) { importer.mipmapEnabled = true; changed = true; }
                if (importer.anisoLevel != 4) { importer.anisoLevel = 4; changed = true; }
                if (importer.textureCompression != TextureImporterCompression.CompressedHQ) { importer.textureCompression = TextureImporterCompression.CompressedHQ; changed = true; }
                if (changed)
                {
                    importer.SaveAndReimport();
                    report.Updated.Add(path);
                }
            }
        }

        // ------------------------------------------------------------------------------------------------- models

        private static Dictionary<string, List<string>> LoadModelManifest()
        {
            var result = new Dictionary<string, List<string>>();
            var abs = GeneratedAssetUtil.ToAbsolutePath(ModelsManifestPath);
            if (!File.Exists(abs))
            {
                return result;
            }

            foreach (var prop in JObject.Parse(File.ReadAllText(abs)).Properties())
            {
                result[prop.Name] = prop.Value["materials"]?.Select(t => (string)t).ToList() ?? new List<string>();
            }

            return result;
        }

        private static void ConfigureArtModels(GeneratorReport report)
        {
            var manifest = LoadModelManifest();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ArtFolder + "/Models" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
                {
                    continue;
                }

                var name = Path.GetFileNameWithoutExtension(path);
                // Product meshes stay CPU-readable: the importer copies them into the hover outline hulls.
                ApplyModelSettings(importer, path.StartsWith(ProductModelsFolder + "/", StringComparison.Ordinal));
                if (manifest.TryGetValue(name, out var keys))
                {
                    foreach (var key in keys)
                    {
                        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), key), GetMaterial(key));
                    }
                }
                else
                {
                    report.Warnings.Add($"{path}: not listed in {ModelsManifestPath}; materials not remapped.");
                }

                importer.SaveAndReimport();
                report.Updated.Add(path);
            }
        }

        private static void ApplyModelSettings(ModelImporter importer, bool readable = false)
        {
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.importBlendShapes = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.isReadable = readable;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.addCollider = false;
            importer.generateSecondaryUV = importer.assetPath.Contains("/Environment/"); // lightmapped set
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        }

        /// <summary>Poly Haven FBX models: URP materials built from the textures shipped next to each model.</summary>
        private static void ConfigurePolyHavenModels(GeneratorReport report)
        {
            foreach (var folderName in PolyHavenProducts.Values.Distinct())
            {
                var folder = $"{PolyHavenFolder}/{folderName}";
                var modelPath = $"{folder}/{folderName}.fbx";
                if (!(AssetImporter.GetAtPath(modelPath) is ModelImporter importer))
                {
                    report.Warnings.Add($"{modelPath}: model missing (run tools/assets/fetch-polyhaven.mjs).");
                    continue;
                }

                ApplyModelSettings(importer, true);
                importer.SaveAndReimport();
                // Embedded materials disappear from the sub-assets once remapped, so also read the existing remap table.
                var materialNames = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Material>().Select(m => m.name)
                    .Concat(importer.GetExternalObjectMap().Keys.Where(k => k.type == typeof(Material)).Select(k => k.name))
                    .Distinct().ToList();
                foreach (var name in materialNames)
                {
                    var mat = UpsertPolyHavenMaterial(folder, folderName, name);
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), mat);
                }

                importer.SaveAndReimport();
                report.Updated.Add(modelPath);
            }

            // Sunglasses reuse the spectacles mesh with tinted lenses (separate materials, see BuildProductPrefabs).
        }

        private static Material UpsertPolyHavenMaterial(string folder, string modelId, string materialName)
        {
            var textureFolder = folder + "/textures";
            var path = $"{ArtMaterialsFolder}/PH_{modelId}_{Sanitize(materialName)}.mat";
            var shader = Shader.Find(LitShader);
            return GeneratedAssetUtil.UpsertMaterial(path, shader, m =>
            {
                var textures = AssetDatabase.IsValidFolder(textureFolder)
                    ? AssetDatabase.FindAssets("t:Texture2D", new[] { textureFolder }).Select(AssetDatabase.GUIDToAssetPath).ToList()
                    : new List<string>();
                string Pick(string marker)
                {
                    var prefix = materialName.ToLowerInvariant();
                    var candidates = textures.Where(p => Path.GetFileName(p).ToLowerInvariant().Contains(marker)).ToList();
                    return candidates.FirstOrDefault(p => Path.GetFileName(p).ToLowerInvariant().StartsWith(prefix)) ?? candidates.FirstOrDefault();
                }

                var diff = Pick("_diff_");
                var nor = Pick("_nor_gl_");
                bool glass = materialName.ToLowerInvariant().Contains("glass") || materialName.ToLowerInvariant().Contains("lens");
                ApplyLit(m, diff != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(diff) : null,
                    nor != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(nor) : null,
                    glass ? new Color(0.92f, 0.95f, 1f) : Color.white, 0f, glass ? 0.95f : 0.35f, Vector2.one);
                if (glass)
                {
                    m.SetTexture("_BaseMap", null);
                    m.SetTexture("_BumpMap", null);
                    m.DisableKeyword("_NORMALMAP");
                    MakeTransparent(m, 0.12f);
                }
            });
        }

        private static string Sanitize(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                s = s.Replace(c, '_');
            }

            return s.Replace(' ', '_');
        }

        // ------------------------------------------------------------------------------------------------- prefabs

        public static string ModelPathFor(string productId)
        {
            if (PolyHavenProducts.TryGetValue(productId, out var ph))
            {
                return $"{PolyHavenFolder}/{ph}/{ph}.fbx";
            }

            return $"{ProductModelsFolder}/{productId}.fbx";
        }

        /// <summary>Path of the hanging display variant model of <paramref name="productId"/> (may not exist).</summary>
        public static string HangingModelPathFor(string productId)
        {
            return $"{ProductModelsFolder}/{productId}{HangingSuffix}.fbx";
        }

        /// <summary>The hover outline material (created / updated in place).</summary>
        public static Material EnsureOutlineMaterial()
        {
            var shader = Shader.Find(UnlitShader) ?? throw new InvalidOperationException("URP Unlit shader not found.");
            GeneratedAssetUtil.EnsureFolder(ArtMaterialsFolder);
            return GeneratedAssetUtil.UpsertMaterial(OutlineMaterialPath, shader, m =>
            {
                m.SetColor("_BaseColor", OutlineColor);
                if (m.HasProperty("_Cull"))
                {
                    m.SetFloat("_Cull", (float)CullMode.Front);
                }

                m.SetFloat("_Surface", 0f);
                m.SetOverrideTag("RenderType", "Opaque");
                m.renderQueue = (int)RenderQueue.Geometry + 10;
                m.enableInstancing = true;
            });
        }

        private static void BuildProductPrefabs(GeneratorReport report)
        {
            var catalog = ProductDataGenerator.LoadCatalog();
            if (catalog == null)
            {
                report.AddError("ProductCatalog missing: run MultiTravel/Generate/Product Data first.");
                return;
            }

            GeneratedAssetUtil.EnsureFolder(GeneratedAssetUtil.PrefabsFolder);
            GeneratedAssetUtil.EnsureFolder(OutlineMeshesFolder);
            var outline = EnsureOutlineMaterial();
            foreach (var product in catalog.Products)
            {
                if (product != null)
                {
                    BuildProductPrefab(product, outline, report, false);
                }
            }

            var practice = ProductDataGenerator.LoadPracticeDefinition();
            if (practice != null)
            {
                BuildProductPrefab(practice, outline, report, true);
            }
            else
            {
                report.Warnings.Add($"Practice item definition missing at {ProductDataGenerator.PracticePath} (run MultiTravel/Generate/Product Data).");
            }
        }

        private static void BuildProductPrefab(ProductDefinition product, Material outline, GeneratorReport report, bool optional)
        {
            var modelPath = ModelPathFor(product.Id);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                if (optional)
                {
                    report.Warnings.Add($"{product.Id}: model missing at {modelPath}; the tutorial runs without the practice item.");
                }
                else
                {
                    report.AddError($"{product.Id}: production model missing at {modelPath}.");
                }

                return;
            }

            var hangingPath = HangingModelPathFor(product.Id);
            var hangingModel = AssetDatabase.LoadAssetAtPath<GameObject>(hangingPath);
            bool wantsVariant = product.Presentation != null && product.Presentation.HasHangingVariant;
            if (wantsVariant && hangingModel == null)
            {
                report.Warnings.Add($"{product.Id}: HasHangingVariant is set but {hangingPath} is missing; the prefab has no hanging visual.");
            }

            var prefabPath = $"{GeneratedAssetUtil.PrefabsFolder}/{product.Id}.prefab";
            var root = new GameObject(product.Id);
            try
            {
                Transform foldedRoot;
                Transform hangingRoot = null;
                if (hangingModel != null)
                {
                    foldedRoot = new GameObject(ItemVisualVariant.FoldedChildName).transform;
                    foldedRoot.SetParent(root.transform, false);
                    hangingRoot = new GameObject(ItemVisualVariant.HangingChildName).transform;
                    hangingRoot.SetParent(root.transform, false);
                }
                else
                {
                    foldedRoot = root.transform;
                }

                // Packed / default visual: rests on the root origin (bottom centre) so spawn and suitcase slots line up.
                var visual = InstantiateModel(product.Id, model, foldedRoot, "Model");
                var bounds = ItemVisualVariant.ComputeVisualBounds(root.transform, foldedRoot);
                if (ModelMaxSize.TryGetValue(product.Id, out var maxSize))
                {
                    float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                    if (largest > 0.0001f)
                    {
                        visual.transform.localScale *= maxSize / largest;
                        bounds = ItemVisualVariant.ComputeVisualBounds(root.transform, foldedRoot);
                    }
                }

                visual.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                var foldedBounds = ItemVisualVariant.ComputeVisualBounds(root.transform, foldedRoot);

                // Hanging display visual: the prefab pivot is the hook top (OVERHAUL_PLAN §5 Hanger grip).
                var hangingBounds = foldedBounds;
                if (hangingRoot != null)
                {
                    var hanging = InstantiateModel(product.Id, hangingModel, hangingRoot, "Model");
                    var hb = ItemVisualVariant.ComputeVisualBounds(root.transform, hangingRoot);
                    if (Mathf.Abs(hb.max.y) > 0.02f)
                    {
                        // The FBX pivot is not at the hook: put the top centre of the visual on the pivot.
                        hanging.transform.localPosition -= new Vector3(hb.center.x, hb.max.y, hb.center.z);
                    }

                    hangingBounds = ItemVisualVariant.ComputeVisualBounds(root.transform, hangingRoot);
                }

                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    r.shadowCastingMode = ShadowCastingMode.On;
                    r.receiveShadows = true;
                    if (product.Id == "sunglasses")
                    {
                        r.sharedMaterials = r.sharedMaterials.Select(TintSunglassesLens).ToArray();
                    }
                }

                var outlineWarnings = new List<string>();
                ProductVisuals.AddOutlineHulls(root, OutlineMeshesFolder, product.Id, outline, outlineWarnings);
                report.Warnings.AddRange(outlineWarnings);

                var box = root.AddComponent<BoxCollider>();
                var displayBounds = hangingRoot != null ? hangingBounds : foldedBounds;
                box.center = displayBounds.center;
                box.size = Vector3.Max(displayBounds.size, Vector3.one * 0.02f);
                var anchor = new GameObject(ProductItem.AnchorChildName).transform;
                anchor.SetParent(root.transform, false);
                anchor.localPosition = displayBounds.center;

                if (hangingRoot != null)
                {
                    var variant = root.AddComponent<ItemVisualVariant>();
                    variant.Configure(hangingRoot, foldedRoot, box, ColliderBounds(hangingBounds), ColliderBounds(foldedBounds));
                    variant.SetAnchor(anchor, hangingBounds.center, foldedBounds.center);
                }
                else
                {
                    ProductVisuals.EnsureGrabCollider(root, foldedBounds);
                }

                var grip = product.Presentation != null ? product.Presentation.Grip : GripPreset.Dynamic;
                var attach = ProductItem.BuildGripAttach(root.transform, grip, hangingRoot != null);
                var item = root.AddComponent<ProductItem>();
                item.SetGripAttach(attach);
                item.Setup(product);
                item.SetAnchorPoint(anchor);
                var fullSize = (hangingRoot != null ? hangingBounds : foldedBounds).size;
                float displayScale = optional ? 1f : DisplayScaleFor(fullSize);
                float footprint = fullSize.x * displayScale;
                float displayHeight = fullSize.y * displayScale;
                item.SetDisplayMetrics(displayScale, footprint, displayHeight);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                if (product.VisualPrefab != prefab)
                {
                    product.VisualPrefab = prefab;
                    EditorUtility.SetDirty(product);
                }

                var size = displayBounds.size;
                report.Updated.Add($"{prefabPath} ← {modelPath}{(hangingRoot != null ? " + " + hangingPath : string.Empty)} " +
                                   $"({size.x:0.00}×{size.y:0.00}×{size.z:0.00} m, display ×{displayScale:0.00}, w {footprint:0.00} h {displayHeight:0.00} = {SpawnSlot.ClassFor(footprint)})");
            }
            catch (Exception ex)
            {
                report.AddError($"{product.Id}: {ex.Message}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Resting scale of an item in its display slot: big items are shown smaller (and grow to real size when grabbed), so
        /// the shelf wall can show every product without overlap while reach stays comfortable.
        /// </summary>
        public static float DisplayScaleFor(Vector3 size)
        {
            float scale = 1f; // few products on a big cabin wall: everything is shown at real size unless it exceeds the shelf
            // The shelf is 0.26 m deep: long items (ukulele, trousers) shrink until their depth fits.
            if (size.z * scale > ShelfDepth)
            {
                scale = ShelfDepth / size.z;
            }

            if (size.x * scale > MaxDisplayWidth)
            {
                scale = MaxDisplayWidth / size.x;
            }

            return Mathf.Clamp(scale, 0.5f, 1f);
        }

        /// <summary>Usable shelf depth (m) of the shelf wall.</summary>
        public const float ShelfDepth = 0.34f;

        /// <summary>Widest item (m) on the shelf wall: the widest slot class.</summary>
        public const float MaxDisplayWidth = 0.46f;

        private static GameObject InstantiateModel(string productId, GameObject model, Transform parent, string name)
        {
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            visual.name = name;
            visual.transform.localPosition = Vector3.zero;
            // Keep the importer's root rotation/scale (Poly Haven FBX roots carry the Z-up conversion); add the pose fix on top.
            if (ModelRotation.TryGetValue(productId, out var euler))
            {
                visual.transform.localRotation = Quaternion.Euler(euler) * visual.transform.localRotation;
            }

            if (productId == "football")
            {
                KeepRoundestRenderer(visual);
            }

            return visual;
        }

        private static Bounds ColliderBounds(Bounds visualBounds)
        {
            return new Bounds(visualBounds.center, Vector3.Max(visualBounds.size, Vector3.one * 0.02f));
        }

        /// <summary>The Poly Haven "football" asset holds an intact and a deflated ball; keep the intact (roundest) one.</summary>
        private static void KeepRoundestRenderer(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length < 2)
            {
                return;
            }

            float Roundness(Renderer r) => Mathf.Min(r.bounds.size.x, Mathf.Min(r.bounds.size.y, r.bounds.size.z)) /
                                           Mathf.Max(0.0001f, Mathf.Max(r.bounds.size.x, Mathf.Max(r.bounds.size.y, r.bounds.size.z)));
            var keep = renderers.OrderByDescending(Roundness).First();
            foreach (var r in renderers)
            {
                if (r != keep)
                {
                    r.gameObject.SetActive(false);
                }
            }
        }

        private static Material TintSunglassesLens(Material source)
        {
            if (source == null || !(source.name.ToLowerInvariant().Contains("glass") || source.name.ToLowerInvariant().Contains("lens")))
            {
                return source;
            }

            return GetMaterial("sunglass_lens");
        }

        public static Bounds RendererBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return new Bounds(Vector3.zero, Vector3.one * 0.1f);
            }

            var inverse = root.transform.worldToLocalMatrix;
            bool first = true;
            var result = new Bounds();
            foreach (var r in renderers)
            {
                var b = r.bounds;
                var c = b.center;
                var e = b.extents;
                for (int i = 0; i < 8; i++)
                {
                    var corner = inverse.MultiplyPoint3x4(c + Vector3.Scale(e, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (first)
                    {
                        result = new Bounds(corner, Vector3.zero);
                        first = false;
                    }
                    else
                    {
                        result.Encapsulate(corner);
                    }
                }
            }

            return result;
        }
    }
}
