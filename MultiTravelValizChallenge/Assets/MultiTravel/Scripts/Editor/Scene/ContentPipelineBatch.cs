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

        /// <summary>
        /// Art + scene refresh that keeps the product data (non-forced <see cref="ProductDataGenerator"/>: only missing
        /// products are added, existing definitions are left as they are): data, import, build, bake, validate.
        /// </summary>
        public static void RefreshArtAndScene()
        {
            AssetDatabase.Refresh();
            ProductDataGenerator.Generate(false).Log();
            ArtImporter.ImportAll().Log();
            MainSceneBuilder.Build();
            MainSceneBuilder.BakeLighting();
            ProjectValidator.Validate(true).Log();
            AssetDatabase.SaveAssets();
        }

        /// <summary>Re-imports the production art (materials, models, product prefabs) only.</summary>
        public static void ImportArt()
        {
            AssetDatabase.Refresh();
            ArtImporter.ImportAll().Log();
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
