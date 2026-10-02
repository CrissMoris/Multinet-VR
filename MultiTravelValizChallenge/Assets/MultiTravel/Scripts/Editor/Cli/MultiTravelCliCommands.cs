#if MT_HAS_PIPELINE
using MultiTravel.EditorTools.Data;
using MultiTravel.EditorTools.Fonts;
using MultiTravel.EditorTools.Validation;
using Unity.Pipeline.Commands;

namespace MultiTravel.EditorTools.Cli
{
    /// <summary>
    /// <c>unity command &lt;name&gt;</c> wrappers (com.unity.pipeline). Handlers run on the main thread (default) and return plain
    /// serialisable objects (<see cref="GeneratorReport"/>, <see cref="ValidationReport"/>, <see cref="BuildOutcome"/>).
    /// Parameter order / Required flags are wire API: append new optional parameters only.
    /// </summary>
    public static class MultiTravelCliCommands
    {
        [CliCommand("mt_generate_data", "MultiTravel: create default ProductDefinitions, ProductCatalog and AppConfig (existing assets kept unless force).", Tags = new[] { "assets" })]
        public static GeneratorReport GenerateData(
            [CliArg("force", "Reset existing product/catalog/config assets to defaults (GUIDs, prefab refs and backend credentials kept).")] bool force = false)
        {
            return ProductDataGenerator.Generate(force);
        }

        [CliCommand("mt_generate_font", "MultiTravel: create/update the dynamic Turkish TMP font asset (Resources/MultiTravelFont).", Tags = new[] { "assets" })]
        public static GeneratorReport GenerateFont(
            [CliArg("force", "Recreate the font asset if it is broken or static (its GUID changes).")] bool force = false)
        {
            return FontAssetGenerator.Generate(force);
        }

        [CliCommand("mt_generate_art", "MultiTravel: generate procedural meshes, palette materials and textures under Assets/MultiTravel/Generated.", Tags = new[] { "assets" })]
        public static GeneratorReport GenerateArt()
        {
            return BaseAssetGenerator.GenerateArt();
        }

        [CliCommand("mt_import_art", "MultiTravel: import production art (Poly Haven CC0 + Blender models), build palette materials and product prefabs.", Tags = new[] { "assets" })]
        public static GeneratorReport ImportArt()
        {
            return Art.ArtImporter.ImportAll();
        }

        [CliCommand("mt_build_main_scene", "MultiTravel: rebuild Main.unity from the production art (keeps the scene GUID) and validate.", Tags = new[] { "scenes" })]
        public static ValidationReport BuildMainScene()
        {
            SceneBuild.MainSceneBuilder.Build();
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ProjectValidator.BootstrapScenePath);
            return ProjectValidator.Validate(true);
        }

        [CliCommand("mt_generate_all", "MultiTravel: full content chain (base assets, production art, scenes, build list) and validation.", Tags = new[] { "assets", "scenes" })]
        public static ValidationReport GenerateAll(
            [CliArg("rebuild_main", "Rebuild Main.unity from the production art (GUID kept).")] bool rebuildMain = false)
        {
            EventSceneGenerator.Generate(rebuildMain);
            return ProjectValidator.Validate(true);
        }

        [CliCommand("mt_configure_project", "MultiTravel: apply player settings, Standalone quality/URP and OpenXR (loader, profiles, hand tracking, single-pass instanced).", Tags = new[] { "settings", "settings/player" })]
        public static GeneratorReport ConfigureProject()
        {
            return BaseAssetGenerator.ConfigureProject();
        }

        [CliCommand("mt_validate", "MultiTravel: validate catalog, prefabs, AppConfig, build scenes, missing scripts and XR configuration.", Tags = new[] { "build" })]
        public static ValidationReport Validate(
            [CliArg("require_prefabs", "Treat missing product prefabs as errors (default true).")] bool requirePrefabs = true)
        {
            return ProjectValidator.Validate(requirePrefabs);
        }

        [CliCommand("mt_build_windows", "MultiTravel: configure, validate and build the Windows x64 player.", Tags = new[] { "build" })]
        public static BuildOutcome BuildWindows(
            [CliArg("output", "Output .exe path (absolute or project-relative). Default: Build/Windows/MultiTravel Valiz Challenge.exe")] string output = null,
            [CliArg("development", "Development build.")] bool development = false,
            [CliArg("station_config", "multitravel.config.json copied into the player (default: <repo>/Deployment/multitravel.config.json).")] string stationConfig = null)
        {
            return BuildScript.BuildWindowsTo(string.IsNullOrWhiteSpace(output) ? BuildScript.DefaultOutputPath : output, development, stationConfig);
        }
    }
}
#endif
