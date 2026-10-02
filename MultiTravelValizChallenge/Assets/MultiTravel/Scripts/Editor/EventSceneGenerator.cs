using System;
using System.IO;
using MultiTravel.EditorTools.Art;
using MultiTravel.EditorTools.Data;
using MultiTravel.EditorTools.SceneBuild;
using MultiTravel.EditorTools.Validation;
using MultiTravel.Gameplay;
using MultiTravel.Gameplay.Director;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static MultiTravel.EditorTools.Art.ProductVisuals;

namespace MultiTravel.EditorTools
{
    /// <summary>
    /// One-click content pipeline: base assets (font, palette, data, project settings) → production art (materials,
    /// models, product prefabs) → Bootstrap/Main scenes → build list → validation. Existing scenes are kept unless
    /// <c>rebuildMain</c> is set (Main is then rebuilt in place, keeping its GUID).
    /// </summary>
    public static class EventSceneGenerator
    {
        [MenuItem("MultiTravel/Generate/Complete Event Content", priority = 130)]
        public static void GenerateMenu()
        {
            Generate();
        }

        public static void Generate(bool rebuildMain = false)
        {
            var report = BaseAssetGenerator.GenerateAllBase();
            report.Log();
            if (!report.Success)
            {
                throw new InvalidOperationException("Base content generation failed.");
            }

            var art = ArtImporter.ImportAll();
            art.Log();
            if (!art.Success)
            {
                throw new InvalidOperationException("Production art import failed.");
            }

            GeneratedAssetUtil.EnsureFolder(GeneratedAssetUtil.ScenesFolder);
            var catalog = ProductDataGenerator.LoadCatalog();
            if (!File.Exists(ProjectValidator.BootstrapScenePath))
            {
                var bootstrap = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                new GameObject("AppBootstrap").AddComponent<AppBootstrap>().Configure(catalog);
                EditorSceneManager.SaveScene(bootstrap, ProjectValidator.BootstrapScenePath);
            }

            if (rebuildMain || !File.Exists(ProjectValidator.MainScenePath))
            {
                MainSceneBuilder.Build();
            }

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ProjectValidator.BootstrapScenePath, true),
                new EditorBuildSettingsScene(ProjectValidator.MainScenePath, true)
            };
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(ProjectValidator.BootstrapScenePath);
            ProjectValidator.Validate(true).Log();
        }

        /// <summary>Poke button "Valizi Tamamla" (used when the completion mode allows a manual confirm).</summary>
        public static void AddConfirmButton(GameplayDirector director)
        {
            if (GameObject.Find("Manual confirmation") != null)
            {
                return;
            }

            var root = Node("Manual confirmation", null, new Vector3(.50f, .98f, .40f));
            var surface = root.AddComponent<BoxCollider>();
            surface.size = new Vector3(.23f, .05f, .14f);
            var visual = Part("Button face", root.transform, Vector3.zero, surface.size, "Teal");
            var label = Label(visual.transform, ManualConfirmButton.Caption, new Vector3(0, .032f, 0), .15f, Color.white);
            label.transform.localScale = new Vector3(1 / .23f, 1 / .05f, 1 / .14f);
            label.transform.localRotation = Quaternion.Euler(90, 0, 0);
            label.rectTransform.sizeDelta = new Vector2(.21f, .08f);
            root.AddComponent<ManualConfirmButton>().Configure(director, visual, label, visual.GetComponent<Renderer>(), surface);
        }
    }
}
