using MultiTravel.EditorTools.Art;
using MultiTravel.EditorTools.Data;
using MultiTravel.EditorTools.Fonts;
using MultiTravel.EditorTools.Validation;
using UnityEditor;
using UnityEngine;

namespace MultiTravel.EditorTools.SceneBuild
{
    /// <summary>Batch-mode entry points: <c>-executeMethod MultiTravel.X.Run</c>.</summary>
    public static class ContentPipelineBatch
    {
        public static void Run()
        {
            ProductDataGenerator.Generate(true).Log();
            FontAssetGenerator.Generate(true).Log();
            AssetDatabase.Refresh();
            ArtImporter.ImportAll().Log();
            MainSceneBuilder.Build();
            ProjectValidator.Validate(true).Log();
            AssetDatabase.SaveAssets();
        }

        public static void BuildOnly()
        {
            MainSceneBuilder.Build();
        }

        public static void Bake()
        {
            MainSceneBuilder.BakeLighting();
        }
    }
}
