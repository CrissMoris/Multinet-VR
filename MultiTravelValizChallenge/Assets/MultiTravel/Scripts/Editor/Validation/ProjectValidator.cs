using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MultiTravel.Core.Config;
using MultiTravel.Core.Products;
using MultiTravel.EditorTools.Art;
using MultiTravel.EditorTools.Data;
using MultiTravel.EditorTools.Fonts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MultiTravel.EditorTools.Validation
{
    /// <summary>Serializable result of <see cref="ProjectValidator.Validate"/>.</summary>
    [Serializable]
    public sealed class ValidationReport
    {
        public bool RequirePrefabs;
        public List<string> Errors = new List<string>();
        public List<string> Warnings = new List<string>();
        public List<string> Info = new List<string>();

        public bool HasErrors => Errors.Count > 0;

        public bool Passed => Errors.Count == 0;

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("[MultiTravel] Project validation ").Append(Passed ? "PASSED" : "FAILED")
              .Append($" (errors {Errors.Count}, warnings {Warnings.Count}, requirePrefabs {RequirePrefabs})");
            foreach (var e in Errors)
            {
                sb.Append("\n  ERROR: ").Append(e);
            }

            foreach (var w in Warnings)
            {
                sb.Append("\n  WARN:  ").Append(w);
            }

            foreach (var i in Info)
            {
                sb.Append("\n  info:  ").Append(i);
            }

            return sb.ToString();
        }

        public void Log()
        {
            if (HasErrors)
            {
                Debug.LogError(ToString());
            }
            else if (Warnings.Count > 0)
            {
                Debug.LogWarning(ToString());
            }
            else
            {
                Debug.Log(ToString());
            }
        }
    }

    /// <summary>
    /// Pre-build project checks (ARCHITECTURE §8): catalog validity, product prefabs, AppConfig in Resources, backend config,
    /// build scene list, missing scripts in build scenes, XR loader / OpenXR features, font asset.
    /// </summary>
    public static class ProjectValidator
    {
        public const string BootstrapScenePath = GeneratedAssetUtil.ScenesFolder + "/Bootstrap.unity";
        public const string MainScenePath = GeneratedAssetUtil.ScenesFolder + "/Main.unity";
        public const string TemplateSampleScenePath = "Assets/Scenes/SampleScene.unity";
        private const string MissingPrefabSuffix = "VisualPrefab is missing.";

        /// <summary>
        /// Runs every check. With <paramref name="requirePrefabs"/> false, missing product prefabs are warnings instead of errors
        /// (useful before the content generator has run).
        /// </summary>
        public static ValidationReport Validate(bool requirePrefabs)
        {
            var report = new ValidationReport { RequirePrefabs = requirePrefabs };
            Run(report, "catalog", () => CheckCatalog(report, requirePrefabs));
            Run(report, "app config", () => CheckAppConfig(report));
            Run(report, "font", () => CheckFont(report));
            Run(report, "build scenes", () => CheckBuildScenes(report));
            Run(report, "XR", () => CheckXr(report));
            return report;
        }

        /// <summary>Entry point for <c>-executeMethod MultiTravel.EditorTools.Validation.ProjectValidator.ValidateFromCommandLine</c>
        /// (optional <c>-allowMissingPrefabs</c>). Exits with 1 on errors, 0 otherwise.</summary>
        public static void ValidateFromCommandLine()
        {
            bool allowMissing = Array.IndexOf(Environment.GetCommandLineArgs(), "-allowMissingPrefabs") >= 0;
            var report = Validate(!allowMissing);
            report.Log();
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(report.HasErrors ? 1 : 0);
            }
        }

        private static void Run(ValidationReport report, string label, Action check)
        {
            try
            {
                check();
            }
            catch (Exception ex)
            {
                report.Errors.Add($"Validator '{label}' threw: {ex.Message}");
            }
        }

        private static void CheckCatalog(ValidationReport report, bool requirePrefabs)
        {
            var catalog = ProductDataGenerator.LoadCatalog();
            if (catalog == null)
            {
                report.Errors.Add($"ProductCatalog missing at {ProductDataGenerator.CatalogPath} (run MultiTravel/Generate/Product Data).");
                return;
            }

            var errors = new List<string>();
            catalog.Validate(errors);
            foreach (var e in errors)
            {
                if (!requirePrefabs && e.EndsWith(MissingPrefabSuffix, StringComparison.Ordinal))
                {
                    report.Warnings.Add("Catalog: " + e);
                }
                else
                {
                    report.Errors.Add("Catalog: " + e);
                }
            }

            int withPrefab = 0;
            if (catalog.Products != null)
            {
                foreach (var product in catalog.Products)
                {
                    if (product == null || product.VisualPrefab == null)
                    {
                        continue;
                    }

                    withPrefab++;
                    CheckProductPrefab(report, product, requirePrefabs);
                }

                report.Info.Add($"Catalog: {catalog.Products.Count} product(s), {withPrefab} with a prefab.");
            }
        }

        private static void CheckProductPrefab(ValidationReport report, ProductDefinition product, bool requirePrefabs)
        {
            var prefab = product.VisualPrefab;
            if (PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.NotAPrefab)
            {
                report.Errors.Add($"{product.Id}: VisualPrefab '{prefab.name}' is not a prefab asset.");
                return;
            }

            // Type looked up by name: the Gameplay assembly is not referenced by the editor tooling.
            bool hasProductItem = false;
            foreach (var behaviour in prefab.GetComponents<MonoBehaviour>())
            {
                if (behaviour != null && behaviour.GetType().Name == "ProductItem")
                {
                    hasProductItem = true;
                    break;
                }
            }

            if (!hasProductItem)
            {
                var message = $"{product.Id}: prefab '{prefab.name}' has no ProductItem component on its root.";
                if (requirePrefabs)
                {
                    report.Errors.Add(message);
                }
                else
                {
                    report.Warnings.Add(message);
                }
            }

            int missing = CountMissingScripts(prefab);
            if (missing > 0)
            {
                report.Errors.Add($"{product.Id}: prefab '{prefab.name}' has {missing} missing script(s).");
            }
        }

        private static void CheckAppConfig(ValidationReport report)
        {
            var config = ProductDataGenerator.LoadAppConfig();
            if (config == null)
            {
                report.Errors.Add($"AppConfig missing at {ProductDataGenerator.AppConfigPath} (must live in a Resources folder, name '{AppConfig.ResourceName}').");
                return;
            }

            var guids = AssetDatabase.FindAssets("t:AppConfig");
            int inResources = 0;
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Resources/") && Path.GetFileNameWithoutExtension(path) == AppConfig.ResourceName)
                {
                    inResources++;
                }
            }

            if (inResources > 1)
            {
                report.Errors.Add($"{inResources} AppConfig assets named '{AppConfig.ResourceName}' exist in Resources folders; Resources.Load would be ambiguous.");
            }

            var backend = config.Backend;
            if (backend == null)
            {
                report.Errors.Add("AppConfig.Backend is null.");
                return;
            }

            if (string.IsNullOrWhiteSpace(backend.SupabaseUrl) || string.IsNullOrWhiteSpace(backend.SupabaseAnonKey))
            {
                report.Warnings.Add("AppConfig backend SupabaseUrl / SupabaseAnonKey are empty: results cannot be submitted unless StreamingAssets/persistentDataPath multitravel.config.json provides them.");
            }
            else if (!backend.SupabaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !backend.SupabaseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                report.Errors.Add($"AppConfig SupabaseUrl '{backend.SupabaseUrl}' is not an http(s) URL.");
            }

            if (!string.IsNullOrEmpty(backend.SupabaseAnonKey) && backend.SupabaseAnonKey.IndexOf("service_role", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                report.Errors.Add("AppConfig SupabaseAnonKey looks like a service_role key. Only the anon/publishable key may be stored.");
            }

            if (string.IsNullOrWhiteSpace(backend.EventSlug))
            {
                report.Warnings.Add("AppConfig EventSlug is empty.");
            }

            if (string.IsNullOrWhiteSpace(backend.StationId))
            {
                report.Warnings.Add("AppConfig StationId is empty (the machine name will be used).");
            }

            if (config.Texts == null || string.IsNullOrWhiteSpace(config.Texts.WelcomeTitle) || string.IsNullOrWhiteSpace(config.Texts.InstructionsText))
            {
                report.Warnings.Add("AppConfig welcome title / instructions text is empty.");
            }
        }

        private static void CheckFont(ValidationReport report)
        {
            var font = FontAssetGenerator.Load();
            if (font == null)
            {
                report.Warnings.Add($"TMP font asset missing at {FontAssetGenerator.FontAssetPath}; Turkish characters will not render (run MultiTravel/Generate/Font Asset).");
                return;
            }

            if (font.atlasPopulationMode != TMPro.AtlasPopulationMode.Dynamic)
            {
                report.Warnings.Add($"{FontAssetGenerator.FontAssetPath} is not dynamic.");
            }

            if (!font.HasCharacters("ğĞışŞİçÇöÖüÜ", out uint[] missing, false, false) && missing != null && missing.Length > 0)
            {
                report.Warnings.Add($"{FontAssetGenerator.FontAssetPath} lacks {missing.Length} Turkish glyph(s) in its atlas (re-run MultiTravel/Generate/Font Asset).");
            }
        }

        private static void CheckBuildScenes(ValidationReport report)
        {
            var scenes = EditorBuildSettings.scenes;
            if (scenes == null || scenes.Length == 0)
            {
                report.Errors.Add("Build scene list is empty.");
                return;
            }

            var enabledPaths = new List<string>();
            foreach (var s in scenes)
            {
                if (s == null || !s.enabled)
                {
                    continue;
                }

                if (!File.Exists(GeneratedAssetUtil.ToAbsolutePath(s.path)))
                {
                    report.Errors.Add($"Build scene missing on disk: {s.path}");
                    continue;
                }

                enabledPaths.Add(s.path);
                if (string.Equals(s.path, TemplateSampleScenePath, StringComparison.OrdinalIgnoreCase))
                {
                    report.Warnings.Add($"Template scene {TemplateSampleScenePath} is still enabled in the build list.");
                }
            }

            if (scenes[0] == null || !string.Equals(scenes[0].path, BootstrapScenePath, StringComparison.Ordinal) || !scenes[0].enabled)
            {
                report.Errors.Add($"{BootstrapScenePath} must be the first, enabled scene in Build Settings.");
            }

            if (!enabledPaths.Contains(MainScenePath))
            {
                report.Errors.Add($"{MainScenePath} is not an enabled build scene.");
            }

            CheckMissingScriptsInScenes(report, enabledPaths);
        }

        /// <summary>
        /// Counts missing scripts in every build scene. Scenes that are not loaded are opened additively and closed again;
        /// the previously open scene setup (loaded scenes and active scene) is left as it was.
        /// </summary>
        private static void CheckMissingScriptsInScenes(ValidationReport report, List<string> scenePaths)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                report.Warnings.Add("Missing-script scan skipped (Play mode).");
                return;
            }

            var activeScene = SceneManager.GetActiveScene();
            // Scenes we loaded, and whether they were already listed (unloaded) in the hierarchy before.
            var opened = new List<KeyValuePair<Scene, bool>>();
            try
            {
                foreach (var path in scenePaths)
                {
                    var scene = SceneManager.GetSceneByPath(path);
                    if (!scene.IsValid() || !scene.isLoaded)
                    {
                        bool wasListed = scene.IsValid();
                        scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                        opened.Add(new KeyValuePair<Scene, bool>(scene, wasListed));
                    }

                    int missing = 0;
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        missing += CountMissingScripts(root);
                    }

                    if (missing > 0)
                    {
                        report.Errors.Add($"{path}: {missing} missing script(s).");
                    }
                }
            }
            finally
            {
                // Close what we opened: scenes that were listed-but-unloaded go back to unloaded, the rest are removed.
                for (int i = opened.Count - 1; i >= 0; i--)
                {
                    var scene = opened[i].Key;
                    if (scene.IsValid() && scene.isLoaded)
                    {
                        EditorSceneManager.CloseScene(scene, !opened[i].Value);
                    }
                }

                if (activeScene.IsValid() && activeScene.isLoaded && SceneManager.GetActiveScene() != activeScene)
                {
                    SceneManager.SetActiveScene(activeScene);
                }
            }
        }

        private static void CheckXr(ValidationReport report)
        {
            if (!XrConfigurator.IsOpenXrLoaderAssigned())
            {
                report.Errors.Add("Standalone XR loader list does not include OpenXR (run MultiTravel/Generate/Project Settings (Player + XR)).");
            }

            var general = UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (general != null && !general.InitManagerOnStart)
            {
                report.Errors.Add("XR 'Initialize XR on Startup' is off for Standalone.");
            }

            foreach (var feature in XrConfigurator.GetMissingStandaloneFeatures())
            {
                report.Warnings.Add($"OpenXR (Standalone): '{feature}' is not enabled.");
            }
        }

        /// <summary>Missing MonoBehaviour count on <paramref name="root"/> and all its children.</summary>
        public static int CountMissingScripts(GameObject root)
        {
            if (root == null)
            {
                return 0;
            }

            int count = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                count += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
            }

            return count;
        }
    }
}
