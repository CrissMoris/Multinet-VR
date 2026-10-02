using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MultiTravel.EditorTools.Art
{
    /// <summary>
    /// Brand palette as URP/Lit materials under <c>Assets/MultiTravel/Generated/Materials/MT_&lt;Name&gt;.mat</c>.
    /// Procedural textures (wood grain, towel stripes, straw weave, fabric weave) are written as PNGs under
    /// <c>Assets/MultiTravel/Generated/Textures</c>. Everything is updated in place (stable GUIDs).
    /// Use the <see cref="Names"/> constants with <see cref="Get"/>.
    /// </summary>
    public static class MaterialLibrary
    {
        public const string ShaderName = "Universal Render Pipeline/Lit";

        /// <summary>Palette entry names accepted by <see cref="Get"/>.</summary>
        public static class Names
        {
            public const string BrandDeepBlue = "BrandDeepBlue";
            public const string Navy = "Navy";
            public const string Teal = "Teal";
            public const string Orange = "Orange";
            public const string White = "White";
            public const string OffWhite = "OffWhite";
            public const string LightGrey = "LightGrey";
            public const string DarkGrey = "DarkGrey";
            public const string WoodLight = "WoodLight";
            public const string WoodDark = "WoodDark";
            public const string LeatherBlack = "LeatherBlack";
            public const string FabricNavy = "FabricNavy";
            public const string FabricLightBlue = "FabricLightBlue";
            public const string TowelStripes = "TowelStripes";
            public const string StrawBeige = "StrawBeige";
            public const string Metal = "Metal";
            public const string ScreenDark = "ScreenDark";
            public const string SuitcaseLiningBlue = "SuitcaseLiningBlue";
            public const string EmissiveTeal = "EmissiveTeal";
            public const string EmissiveOrange = "EmissiveOrange";
        }

        /// <summary>Raw brand colours (sRGB) for code that needs the values, e.g. UI or vertex colours.</summary>
        public static class Colors
        {
            public static readonly Color BrandDeepBlue = Hex(0x0B3C8C);
            public static readonly Color Navy = Hex(0x14213D);
            public static readonly Color Teal = Hex(0x19B394);
            public static readonly Color Orange = Hex(0xF39200);
            public static readonly Color White = Hex(0xFFFFFF);
            public static readonly Color OffWhite = Hex(0xF4F1EA);
            public static readonly Color LightGrey = Hex(0xC9CED6);
            public static readonly Color DarkGrey = Hex(0x3A3F47);
            public static readonly Color WoodLight = Hex(0xC8A27A);
            public static readonly Color WoodDark = Hex(0x6B4A33);
            public static readonly Color LeatherBlack = Hex(0x1B1A1A);
            public static readonly Color FabricNavy = Hex(0x1F2E5A);
            public static readonly Color FabricLightBlue = Hex(0xA9C8EC);
            public static readonly Color StrawBeige = Hex(0xE2CC92);
            public static readonly Color Metal = Hex(0xB8BEC6);
            public static readonly Color ScreenDark = Hex(0x0E1117);
            public static readonly Color SuitcaseLiningBlue = Hex(0x2D5DA8);

            // Kept inside Colors: calling the outer class from this static initializer would trigger the outer
            // initializer first, which reads these fields while they are still default (black, alpha 0).
            private static Color Hex(int rgb)
            {
                return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
            }
        }

        private sealed class Spec
        {
            public string Name;
            public Color BaseColor;
            public float Metallic;
            public float Smoothness;
            public Color Emission = Color.black;
            public float EmissionIntensity;
            public string TextureName;
            public Vector2 Tiling = Vector2.one;
        }

        private static readonly Spec[] Specs =
        {
            new Spec { Name = Names.BrandDeepBlue, BaseColor = Colors.BrandDeepBlue, Smoothness = 0.45f },
            new Spec { Name = Names.Navy, BaseColor = Colors.Navy, Smoothness = 0.4f },
            new Spec { Name = Names.Teal, BaseColor = Colors.Teal, Smoothness = 0.45f },
            new Spec { Name = Names.Orange, BaseColor = Colors.Orange, Smoothness = 0.45f },
            new Spec { Name = Names.White, BaseColor = Colors.White, Smoothness = 0.35f },
            new Spec { Name = Names.OffWhite, BaseColor = Colors.OffWhite, Smoothness = 0.25f },
            new Spec { Name = Names.LightGrey, BaseColor = Colors.LightGrey, Smoothness = 0.3f },
            new Spec { Name = Names.DarkGrey, BaseColor = Colors.DarkGrey, Smoothness = 0.35f },
            new Spec { Name = Names.WoodLight, BaseColor = Colors.WoodLight, Smoothness = 0.35f, TextureName = WoodTexture, Tiling = new Vector2(1f, 1f) },
            new Spec { Name = Names.WoodDark, BaseColor = Colors.WoodDark, Smoothness = 0.4f, TextureName = WoodTexture, Tiling = new Vector2(1f, 1f) },
            new Spec { Name = Names.LeatherBlack, BaseColor = Colors.LeatherBlack, Smoothness = 0.55f },
            new Spec { Name = Names.FabricNavy, BaseColor = Colors.FabricNavy, Smoothness = 0.12f, TextureName = FabricTexture, Tiling = new Vector2(4f, 4f) },
            new Spec { Name = Names.FabricLightBlue, BaseColor = Colors.FabricLightBlue, Smoothness = 0.12f, TextureName = FabricTexture, Tiling = new Vector2(4f, 4f) },
            new Spec { Name = Names.TowelStripes, BaseColor = Color.white, Smoothness = 0.05f, TextureName = TowelTexture },
            new Spec { Name = Names.StrawBeige, BaseColor = Colors.StrawBeige, Smoothness = 0.15f, TextureName = StrawTexture, Tiling = new Vector2(6f, 3f) },
            new Spec { Name = Names.Metal, BaseColor = Colors.Metal, Metallic = 0.9f, Smoothness = 0.7f },
            new Spec { Name = Names.ScreenDark, BaseColor = Colors.ScreenDark, Smoothness = 0.85f },
            new Spec { Name = Names.SuitcaseLiningBlue, BaseColor = Colors.SuitcaseLiningBlue, Smoothness = 0.15f, TextureName = FabricTexture, Tiling = new Vector2(6f, 6f) },
            new Spec { Name = Names.EmissiveTeal, BaseColor = Colors.Teal, Smoothness = 0.5f, Emission = Colors.Teal, EmissionIntensity = 1.6f },
            new Spec { Name = Names.EmissiveOrange, BaseColor = Colors.Orange, Smoothness = 0.5f, Emission = Colors.Orange, EmissionIntensity = 1.6f },
        };

        private const string WoodTexture = "MT_WoodGrain";
        private const string TowelTexture = "MT_TowelStripes";
        private const string StrawTexture = "MT_StrawWeave";
        private const string FabricTexture = "MT_FabricWeave";

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int WorkflowModeId = Shader.PropertyToID("_WorkflowMode");

        /// <summary>Every palette name, in declaration order.</summary>
        public static IReadOnlyList<string> AllNames
        {
            get
            {
                var list = new List<string>(Specs.Length);
                foreach (var s in Specs)
                {
                    list.Add(s.Name);
                }

                return list;
            }
        }

        public static string PathFor(string name)
        {
            return $"{GeneratedAssetUtil.MaterialsFolder}/MT_{name}.mat";
        }

        /// <summary>
        /// Returns the persistent material for a palette <paramref name="name"/> (see <see cref="Names"/>), generating it (and its texture)
        /// on first use. Throws for unknown names.
        /// </summary>
        public static Material Get(string name)
        {
            var spec = Find(name);
            if (spec == null)
            {
                throw new ArgumentException($"Unknown palette material '{name}'. Known: {string.Join(", ", AllNames)}", nameof(name));
            }

            var existing = AssetDatabase.LoadAssetAtPath<Material>(PathFor(spec.Name));
            return existing != null ? existing : Upsert(spec);
        }

        /// <summary>Generates / refreshes every palette material and procedural texture. Idempotent.</summary>
        public static GeneratorReport GenerateAll()
        {
            var report = new GeneratorReport("Generate palette materials");
            try
            {
                GeneratedAssetUtil.EnsureFolder(GeneratedAssetUtil.MaterialsFolder);
                GeneratedAssetUtil.EnsureFolder(GeneratedAssetUtil.TexturesFolder);
            }
            catch (Exception ex)
            {
                report.AddError(ex.Message);
                return report;
            }

            foreach (var textureName in new[] { WoodTexture, TowelTexture, StrawTexture, FabricTexture })
            {
                try
                {
                    bool existed = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePathFor(textureName)) != null;
                    GenerateTexture(textureName);
                    (existed ? report.Updated : report.Created).Add(TexturePathFor(textureName));
                }
                catch (Exception ex)
                {
                    report.AddError($"{textureName}: {ex.Message}");
                }
            }

            foreach (var spec in Specs)
            {
                try
                {
                    bool existed = AssetDatabase.LoadAssetAtPath<Material>(PathFor(spec.Name)) != null;
                    Upsert(spec);
                    (existed ? report.Updated : report.Created).Add(PathFor(spec.Name));
                }
                catch (Exception ex)
                {
                    report.AddError($"{spec.Name}: {ex.Message}");
                }
            }

            AssetDatabase.SaveAssets();
            return report;
        }

        /// <summary>Path of a generated texture by name (e.g. "MT_WoodGrain").</summary>
        public static string TexturePathFor(string textureName)
        {
            return $"{GeneratedAssetUtil.TexturesFolder}/{textureName}.png";
        }

        /// <summary>Returns the generated texture (creating it when missing). Names: MT_WoodGrain, MT_TowelStripes, MT_StrawWeave, MT_FabricWeave.</summary>
        public static Texture2D GetTexture(string textureName)
        {
            var path = TexturePathFor(textureName);
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            return existing != null ? existing : GenerateTexture(textureName);
        }

        private static Spec Find(string name)
        {
            foreach (var s in Specs)
            {
                if (string.Equals(s.Name, name, StringComparison.Ordinal))
                {
                    return s;
                }
            }

            return null;
        }

        private static Material Upsert(Spec spec)
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException($"Shader '{ShaderName}' not found. Is URP installed and active?");
            }

            Texture2D texture = null;
            if (!string.IsNullOrEmpty(spec.TextureName))
            {
                texture = GetTexture(spec.TextureName);
            }

            return GeneratedAssetUtil.UpsertMaterial(PathFor(spec.Name), shader, m => Configure(m, spec, texture));
        }

        private static void Configure(Material m, Spec spec, Texture2D texture)
        {
            // Opaque, metallic workflow (URP/Lit defaults; asserted explicitly so a hand-edited material is restored).
            if (m.HasProperty(SurfaceId))
            {
                m.SetFloat(SurfaceId, 0f);
            }

            if (m.HasProperty(WorkflowModeId))
            {
                m.SetFloat(WorkflowModeId, 1f);
            }

            m.SetColor(BaseColorId, spec.BaseColor);
            if (m.HasProperty(ColorId))
            {
                m.SetColor(ColorId, spec.BaseColor);
            }

            m.SetTexture(BaseMapId, texture);
            m.SetTextureScale(BaseMapId, spec.Tiling);
            if (m.HasProperty(MainTexId))
            {
                m.SetTexture(MainTexId, texture);
                m.SetTextureScale(MainTexId, spec.Tiling);
            }

            m.SetFloat(MetallicId, spec.Metallic);
            m.SetFloat(SmoothnessId, spec.Smoothness);

            if (spec.EmissionIntensity > 0f)
            {
                // SetColor takes gamma-space values (converted to linear by Unity in a linear project); intensity > 1 gives HDR glow.
                var e = spec.Emission * spec.EmissionIntensity;
                m.SetColor(EmissionColorId, new Color(e.r, e.g, e.b, 1f));
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                m.SetColor(EmissionColorId, Color.black);
                m.DisableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }

            m.enableInstancing = true;
        }

        // ------------------------------------------------------------------------------------------------
        // Procedural textures
        // ------------------------------------------------------------------------------------------------

        private static Texture2D GenerateTexture(string textureName)
        {
            Texture2D tex;
            switch (textureName)
            {
                case WoodTexture:
                    tex = BuildWoodGrain(512);
                    break;
                case TowelTexture:
                    tex = BuildTowelStripes(512);
                    break;
                case StrawTexture:
                    tex = BuildStrawWeave(256);
                    break;
                case FabricTexture:
                    tex = BuildFabricWeave(256);
                    break;
                default:
                    throw new ArgumentException($"Unknown generated texture '{textureName}'.", nameof(textureName));
            }

            try
            {
                return GeneratedAssetUtil.SavePngTexture(tex, TexturePathFor(textureName), GeneratedAssetUtil.TextureImportOptions.Color);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        /// <summary>Greyscale-ish wood grain (multiplied by the material base colour). Tileable along both axes.</summary>
        private static Texture2D BuildWoodGrain(int size)
        {
            var tex = NewTexture(size);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    float v = (float)y / size;
                    // Grain runs along U; wobble from tileable sines keeps it seamless.
                    float wobble = 0.03f * Mathf.Sin(v * Mathf.PI * 2f * 3f) + 0.015f * Mathf.Sin(u * Mathf.PI * 2f * 2f + v * Mathf.PI * 2f);
                    float rings = Mathf.Sin((v + wobble) * Mathf.PI * 2f * 14f);
                    float fine = Mathf.Sin((v + wobble * 0.5f) * Mathf.PI * 2f * 61f + Mathf.Sin(u * Mathf.PI * 2f * 5f));
                    float noise = TileNoise(u, v, 8) * 0.5f + TileNoise(u, v, 32) * 0.5f;
                    float value = 0.80f + 0.10f * rings + 0.04f * fine + 0.10f * (noise - 0.5f);
                    byte b = (byte)(Mathf.Clamp01(value) * 255f);
                    pixels[y * size + x] = new Color32(b, b, b, 255);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>Beach towel: brand teal / orange / white / deep-blue stripes along V with a subtle terry texture.</summary>
        private static Texture2D BuildTowelStripes(int size)
        {
            var tex = NewTexture(size);
            var pixels = new Color32[size * size];
            Color[] bands = { Colors.Teal, Color.white, Colors.Orange, Color.white, Colors.BrandDeepBlue, Color.white };
            float[] widths = { 0.22f, 0.06f, 0.16f, 0.06f, 0.12f, 0.38f };
            for (int y = 0; y < size; y++)
            {
                float v = (float)y / size;
                // Mirror so the pattern is symmetric and tiles.
                float t = v < 0.5f ? v * 2f : (1f - v) * 2f;
                Color band = bands[bands.Length - 1];
                float acc = 0f;
                for (int i = 0; i < widths.Length; i++)
                {
                    acc += widths[i];
                    if (t <= acc)
                    {
                        band = bands[i];
                        break;
                    }
                }

                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    float terry = 0.92f + 0.08f * TileNoise(u, v, 64);
                    var c = band * terry;
                    pixels[y * size + x] = new Color(c.r, c.g, c.b, 1f);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>Basket weave for straw (multiplied by the beige base colour).</summary>
        private static Texture2D BuildStrawWeave(int size)
        {
            var tex = NewTexture(size);
            var pixels = new Color32[size * size];
            const int cells = 8;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size * cells;
                    float v = (float)y / size * cells;
                    int cu = Mathf.FloorToInt(u);
                    int cv = Mathf.FloorToInt(v);
                    bool horizontal = ((cu + cv) & 1) == 0;
                    float local = horizontal ? v - cv : u - cu;
                    float strand = Mathf.Sin(local * Mathf.PI); // rounded strand profile
                    float fibre = 0.9f + 0.1f * Mathf.Sin((horizontal ? u : v) * Mathf.PI * 2f * 6f);
                    float value = Mathf.Lerp(0.55f, 1f, strand) * fibre;
                    byte b = (byte)(Mathf.Clamp01(value) * 255f);
                    pixels[y * size + x] = new Color32(b, b, b, 255);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>Fine plain-weave fabric (multiplied by the base colour).</summary>
        private static Texture2D BuildFabricWeave(int size)
        {
            var tex = NewTexture(size);
            var pixels = new Color32[size * size];
            const int threads = 32;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size * threads;
                    float v = (float)y / size * threads;
                    float warp = Mathf.Abs(Mathf.Sin(u * Mathf.PI));
                    float weft = Mathf.Abs(Mathf.Sin(v * Mathf.PI));
                    bool over = ((Mathf.FloorToInt(u) + Mathf.FloorToInt(v)) & 1) == 0;
                    float value = 0.84f + 0.1f * (over ? warp : weft) + 0.06f * TileNoise((float)x / size, (float)y / size, 16);
                    byte b = (byte)(Mathf.Clamp01(value) * 255f);
                    pixels[y * size + x] = new Color32(b, b, b, 255);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static Texture2D NewTexture(int size)
        {
            return new Texture2D(size, size, TextureFormat.RGBA32, false, false) { wrapMode = TextureWrapMode.Repeat };
        }

        /// <summary>Deterministic tileable value noise in 0..1 on a <paramref name="period"/>-cell lattice.</summary>
        private static float TileNoise(float u, float v, int period)
        {
            float x = u * period;
            float y = v * period;
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            float fx = x - x0;
            float fy = y - y0;
            float sx = fx * fx * (3f - 2f * fx);
            float sy = fy * fy * (3f - 2f * fy);
            float a = Hash(Mod(x0, period), Mod(y0, period));
            float b = Hash(Mod(x0 + 1, period), Mod(y0, period));
            float c = Hash(Mod(x0, period), Mod(y0 + 1, period));
            float d = Hash(Mod(x0 + 1, period), Mod(y0 + 1, period));
            return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
        }

        private static int Mod(int a, int m)
        {
            int r = a % m;
            return r < 0 ? r + m : r;
        }

        private static float Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

    }
}
