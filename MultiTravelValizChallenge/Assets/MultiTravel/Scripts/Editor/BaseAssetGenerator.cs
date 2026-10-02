using MultiTravel.EditorTools.Art;
using MultiTravel.EditorTools.Data;
using MultiTravel.EditorTools.Fonts;
using MultiTravel.EditorTools.Validation;
using UnityEditor;
using UnityEngine;

namespace MultiTravel.EditorTools
{
    /// <summary>
    /// Static entry points (used by menus, CLI commands and the later scene/content generator) for the base asset pipeline:
    /// product data, TMP font, procedural meshes + palette materials, project/XR configuration and validation.
    /// </summary>
    public static class BaseAssetGenerator
    {
        public const string MenuRoot = "MultiTravel/";

        /// <summary>Procedural meshes + palette materials/textures.</summary>
        public static GeneratorReport GenerateArt()
        {
            // No StartAssetEditing batching: texture generation imports PNGs and immediately configures their importers.
            var report = new GeneratorReport("Generate base art");
            report.Merge(MaterialLibrary.GenerateAll());
            report.Merge(ProceduralMeshLibrary.GenerateStandardMeshes());
            AssetDatabase.SaveAssets();
            return report;
        }

        /// <summary>Player settings + XR (OpenXR) configuration.</summary>
        public static GeneratorReport ConfigureProject()
        {
            var report = new GeneratorReport("Configure project");
            report.Merge(PlayerSettingsConfigurator.Apply());
            report.Merge(XrConfigurator.Apply());
            return report;
        }

        /// <summary>Data, font, art and project configuration in one call.</summary>
        public static GeneratorReport GenerateAllBase(bool force = false)
        {
            var report = new GeneratorReport("Generate all base assets");
            report.Merge(ProductDataGenerator.Generate(force));
            report.Merge(FontAssetGenerator.Generate(force));
            report.Merge(GenerateArt());
            report.Merge(ConfigureProject());
            return report;
        }

        // ------------------------------------------------------------------------------------------------
        // Menu items
        // ------------------------------------------------------------------------------------------------

        [MenuItem(MenuRoot + "Generate/Product Data", priority = 100)]
        private static void MenuGenerateData() => ProductDataGenerator.Generate(false).Log();

        [MenuItem(MenuRoot + "Generate/Product Data (Force Reset)", priority = 101)]
        private static void MenuGenerateDataForce()
        {
            if (Application.isBatchMode || EditorUtility.DisplayDialog("Reset product data",
                    "Overwrite every default ProductDefinition, the ProductCatalog and AppConfig with the default values?\n" +
                    "Prefab/sprite references and backend URL/key/access code are kept.", "Reset", "Cancel"))
            {
                ProductDataGenerator.Generate(true).Log();
            }
        }

        [MenuItem(MenuRoot + "Generate/Font Asset", priority = 102)]
        private static void MenuGenerateFont() => FontAssetGenerator.Generate(false).Log();

        [MenuItem(MenuRoot + "Generate/Base Art (Meshes + Materials)", priority = 103)]
        private static void MenuGenerateArt() => GenerateArt().Log();

        [MenuItem(MenuRoot + "Generate/Project Settings (Player + XR)", priority = 104)]
        private static void MenuConfigureProject() => ConfigureProject().Log();

        [MenuItem(MenuRoot + "Generate/All Base Assets", priority = 120)]
        private static void MenuGenerateAll() => GenerateAllBase(false).Log();

        [MenuItem(MenuRoot + "Validate Project", priority = 200)]
        private static void MenuValidate()
        {
            var report = ProjectValidator.Validate(true);
            report.Log();
            if (Application.isBatchMode && report.HasErrors)
            {
                EditorApplication.Exit(1);
            }
        }

        [MenuItem(MenuRoot + "Build/Windows (Release)", priority = 300)]
        private static void MenuBuildRelease() => MenuBuild(false);

        [MenuItem(MenuRoot + "Build/Windows (Development)", priority = 301)]
        private static void MenuBuildDevelopment() => MenuBuild(true);

        private static void MenuBuild(bool development)
        {
            var outcome = BuildScript.BuildWindowsTo(BuildScript.DefaultOutputPath, development);
            if (Application.isBatchMode)
            {
                if (!outcome.Success)
                {
                    EditorApplication.Exit(1);
                }

                return;
            }

            if (outcome.Success)
            {
                EditorUtility.RevealInFinder(outcome.OutputPath);
            }
            else
            {
                EditorUtility.DisplayDialog("MultiTravel build failed", string.Join("\n", outcome.Errors), "OK");
            }
        }
    }
}
