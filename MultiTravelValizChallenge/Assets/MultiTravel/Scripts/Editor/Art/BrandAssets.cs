using System.IO;
using UnityEditor;
using UnityEngine;

namespace MultiTravel.EditorTools.Art
{
    /// <summary>
    /// MultiTravel corporate logos (Art/Brand/*.png, supplied by the client): sprite import settings and the unlit
    /// materials used on the stage plaque and the floor decal. Brand colours: cyan #5BC9EA, blue #25A9E0, navy #18153D.
    /// </summary>
    public static class BrandAssets
    {
        public const string Folder = "Assets/MultiTravel/Art/Brand";
        public const string HorizontalLight = Folder + "/mt-logo-horizontal-light.png";
        public const string HorizontalDark = Folder + "/mt-logo-horizontal-dark.png";
        public const string VerticalLight = Folder + "/mt-logo-vertical-light.png";
        public const string VerticalDark = Folder + "/mt-logo-vertical-dark.png";

        public static readonly Color Cyan = new Color32(0x5B, 0xC9, 0xEA, 255);
        public static readonly Color Blue = new Color32(0x25, 0xA9, 0xE0, 255);
        public static readonly Color Navy = new Color32(0x18, 0x15, 0x3D, 255);

        /// <summary>Imports every logo as an alpha sprite (mip-mapped so it also works on 3D quads).</summary>
        public static void EnsureImported()
        {
            foreach (var path in new[] { HorizontalLight, HorizontalDark, VerticalLight, VerticalDark })
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    AssetDatabase.ImportAsset(path);
                    importer = AssetImporter.GetAtPath(path) as TextureImporter;
                }

                if (importer == null)
                {
                    continue;
                }

                bool changed = importer.textureType != TextureImporterType.Sprite || !importer.alphaIsTransparency || !importer.mipmapEnabled ||
                               importer.maxTextureSize != 1024 || importer.filterMode != FilterMode.Trilinear;
                if (changed)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = true;
                    importer.filterMode = FilterMode.Trilinear;
                    importer.maxTextureSize = 1024;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }
            }
        }

        public static Sprite LoadSprite(string path)
        {
            EnsureImported();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>Unlit transparent material showing a logo texture (stage plaque, floor decal).</summary>
        public static Material LogoMaterial(string texturePath)
        {
            EnsureImported();
            string matPath = "Assets/MultiTravel/Art/materials/brand_" + Path.GetFileNameWithoutExtension(texturePath) + ".mat";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Sprites/Default"));
                AssetDatabase.CreateAsset(material, matPath);
            }

            material.mainTexture = texture;
            material.color = Color.white;
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
