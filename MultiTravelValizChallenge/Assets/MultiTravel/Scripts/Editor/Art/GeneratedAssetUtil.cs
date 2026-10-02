using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MultiTravel.EditorTools.Art
{
    /// <summary>
    /// Idempotent asset helpers shared by every generator. Existing assets are updated in place so their GUIDs
    /// (and therefore every serialized reference to them) stay stable across regenerations.
    /// </summary>
    public static class GeneratedAssetUtil
    {
        public const string RootFolder = "Assets/MultiTravel";
        public const string GeneratedFolder = RootFolder + "/Generated";
        public const string MeshesFolder = GeneratedFolder + "/Meshes";
        public const string MaterialsFolder = GeneratedFolder + "/Materials";
        public const string TexturesFolder = GeneratedFolder + "/Textures";
        public const string PrefabsFolder = GeneratedFolder + "/Prefabs";
        public const string DataFolder = RootFolder + "/Data";
        public const string ProductsFolder = DataFolder + "/Products";
        public const string ResourcesFolder = RootFolder + "/Resources";
        public const string ScenesFolder = RootFolder + "/Scenes";

        /// <summary>Absolute file-system path of the Unity project root (parent of <c>Assets</c>).</summary>
        public static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        /// <summary>Converts an <c>Assets/...</c> path to an absolute file-system path.</summary>
        public static string ToAbsolutePath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(ProjectRoot, assetPath));
        }

        /// <summary>
        /// Creates every missing folder of an <c>Assets/...</c> path (forward slashes) through the AssetDatabase.
        /// Returns the normalised path.
        /// </summary>
        public static string EnsureFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
            {
                throw new ArgumentException("Folder path is empty.", nameof(folderPath));
            }

            var normalised = folderPath.Replace('\\', '/').TrimEnd('/');
            if (normalised != "Assets" && !normalised.StartsWith("Assets/", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Folder must be inside Assets: '{folderPath}'.", nameof(folderPath));
            }

            if (AssetDatabase.IsValidFolder(normalised))
            {
                return normalised;
            }

            var parts = normalised.Split('/');
            var current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    var guid = AssetDatabase.CreateFolder(current, parts[i]);
                    if (string.IsNullOrEmpty(guid))
                    {
                        throw new IOException($"AssetDatabase.CreateFolder failed for '{next}'.");
                    }
                }

                current = next;
            }

            return normalised;
        }

        /// <summary>Ensures the parent folder of an asset path exists.</summary>
        public static void EnsureParentFolder(string assetPath)
        {
            var parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent))
            {
                EnsureFolder(parent);
            }
        }

        /// <summary>
        /// Stores <paramref name="source"/> at <paramref name="assetPath"/> (<c>.asset</c>). When a Mesh asset already exists
        /// there, the data is copied into it (GUID and references preserved) and <paramref name="source"/> is destroyed.
        /// Returns the persistent mesh.
        /// </summary>
        public static Mesh UpsertMesh(Mesh source, string assetPath)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            EnsureParentFolder(assetPath);
            var assetName = Path.GetFileNameWithoutExtension(assetPath);
            source.name = assetName;

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existing == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
                {
                    throw new InvalidOperationException($"'{assetPath}' exists but is not a Mesh asset; delete it or choose another path.");
                }

                AssetDatabase.CreateAsset(source, assetPath);
                return source;
            }

            if (ReferenceEquals(existing, source))
            {
                EditorUtility.SetDirty(existing);
                return existing;
            }

            CopyMeshData(source, existing);
            existing.name = assetName;
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(source);
            return existing;
        }

        /// <summary>Replaces all geometry of <paramref name="target"/> with that of <paramref name="source"/> (UV0..UV3, colours, sub-meshes).</summary>
        public static void CopyMeshData(Mesh source, Mesh target)
        {
            target.Clear();
            target.indexFormat = source.indexFormat;
            target.vertices = source.vertices;
            target.normals = source.normals;
            target.tangents = source.tangents;
            var colors = source.colors32;
            if (colors != null && colors.Length == source.vertexCount && colors.Length > 0)
            {
                target.colors32 = colors;
            }
            var uvs = new System.Collections.Generic.List<Vector2>();
            for (int channel = 0; channel < 4; channel++)
            {
                uvs.Clear();
                source.GetUVs(channel, uvs);
                if (uvs.Count == source.vertexCount && uvs.Count > 0)
                {
                    target.SetUVs(channel, uvs);
                }
            }

            target.subMeshCount = source.subMeshCount;
            for (int i = 0; i < source.subMeshCount; i++)
            {
                target.SetIndices(source.GetIndices(i), source.GetTopology(i), i, false);
            }

            target.bounds = source.bounds;
        }

        /// <summary>
        /// Loads or creates the material at <paramref name="assetPath"/> (<c>.mat</c>), switches it to <paramref name="shader"/> when needed,
        /// then runs <paramref name="configure"/> on the persistent instance. Returns the persistent material.
        /// </summary>
        public static Material UpsertMaterial(string assetPath, Shader shader, Action<Material> configure)
        {
            if (shader == null)
            {
                throw new ArgumentNullException(nameof(shader));
            }

            EnsureParentFolder(assetPath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
                {
                    throw new InvalidOperationException($"'{assetPath}' exists but is not a Material; delete it or choose another path.");
                }

                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(assetPath) };
                configure?.Invoke(material);
                AssetDatabase.CreateAsset(material, assetPath);
                return material;
            }

            if (material.shader != shader)
            {
                material.shader = shader;
            }

            configure?.Invoke(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Texture import options applied by <see cref="SavePngTexture"/>.</summary>
        public struct TextureImportOptions
        {
            public bool SRgb;
            public bool Mipmaps;
            public TextureWrapMode WrapMode;
            public FilterMode FilterMode;
            public int AnisoLevel;
            public bool Readable;
            public TextureImporterCompression Compression;
            public int MaxSize;

            /// <summary>Colour (albedo) texture: sRGB, mipmaps, repeat, trilinear, aniso 4, high-quality compression.</summary>
            public static TextureImportOptions Color => new TextureImportOptions
            {
                SRgb = true,
                Mipmaps = true,
                WrapMode = TextureWrapMode.Repeat,
                FilterMode = FilterMode.Trilinear,
                AnisoLevel = 4,
                Readable = false,
                Compression = TextureImporterCompression.CompressedHQ,
                MaxSize = 1024
            };

            /// <summary>Linear data texture (masks, ramps): no sRGB.</summary>
            public static TextureImportOptions Linear
            {
                get
                {
                    var o = Color;
                    o.SRgb = false;
                    return o;
                }
            }
        }

        /// <summary>
        /// Encodes <paramref name="texture"/> as PNG to <paramref name="assetPath"/> (only rewriting the file when the bytes change),
        /// imports it and applies <paramref name="options"/> to its <see cref="TextureImporter"/>. Returns the imported texture asset.
        /// The source texture is not destroyed.
        /// </summary>
        public static Texture2D SavePngTexture(Texture2D texture, string assetPath, TextureImportOptions options)
        {
            if (texture == null)
            {
                throw new ArgumentNullException(nameof(texture));
            }

            EnsureParentFolder(assetPath);
            var bytes = texture.EncodeToPNG();
            if (bytes == null || bytes.Length == 0)
            {
                throw new InvalidOperationException($"EncodeToPNG failed for '{assetPath}'.");
            }

            var absolute = ToAbsolutePath(assetPath);
            bool changed = !File.Exists(absolute) || !BytesEqual(File.ReadAllBytes(absolute), bytes);
            if (changed)
            {
                File.WriteAllBytes(absolute, bytes);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            }

            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"No TextureImporter for '{assetPath}'.");
            }

            bool dirty = false;
            dirty |= Set(importer.textureType != TextureImporterType.Default, () => importer.textureType = TextureImporterType.Default);
            dirty |= Set(importer.sRGBTexture != options.SRgb, () => importer.sRGBTexture = options.SRgb);
            dirty |= Set(importer.mipmapEnabled != options.Mipmaps, () => importer.mipmapEnabled = options.Mipmaps);
            dirty |= Set(importer.wrapMode != options.WrapMode, () => importer.wrapMode = options.WrapMode);
            dirty |= Set(importer.filterMode != options.FilterMode, () => importer.filterMode = options.FilterMode);
            dirty |= Set(importer.anisoLevel != options.AnisoLevel, () => importer.anisoLevel = options.AnisoLevel);
            dirty |= Set(importer.isReadable != options.Readable, () => importer.isReadable = options.Readable);
            dirty |= Set(importer.textureCompression != options.Compression, () => importer.textureCompression = options.Compression);
            dirty |= Set(options.MaxSize > 0 && importer.maxTextureSize != options.MaxSize, () => importer.maxTextureSize = options.MaxSize);
            dirty |= Set(importer.alphaSource != TextureImporterAlphaSource.FromInput, () => importer.alphaSource = TextureImporterAlphaSource.FromInput);

            if (dirty)
            {
                importer.SaveAndReimport();
            }

            var result = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (result == null)
            {
                throw new InvalidOperationException($"Texture import failed for '{assetPath}'.");
            }

            return result;
        }

        /// <summary>Loads an asset or creates it with <paramref name="factory"/> at <paramref name="assetPath"/>. <paramref name="created"/> reports which happened.</summary>
        public static T LoadOrCreateAsset<T>(string assetPath, Func<T> factory, out bool created) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (existing != null)
            {
                created = false;
                return existing;
            }

            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
            {
                throw new InvalidOperationException($"'{assetPath}' exists but is not a {typeof(T).Name}.");
            }

            EnsureParentFolder(assetPath);
            var instance = factory != null ? factory() : ScriptableObject.CreateInstance<T>();
            instance.name = Path.GetFileNameWithoutExtension(assetPath);
            AssetDatabase.CreateAsset(instance, assetPath);
            created = true;
            return instance;
        }

        private static bool Set(bool condition, Action apply)
        {
            if (!condition)
            {
                return false;
            }

            apply();
            return true;
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
