using System;
using MultiTravel.EditorTools.Art;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace MultiTravel.EditorTools.Fonts
{
    /// <summary>
    /// Creates <c>Assets/MultiTravel/Resources/MultiTravelFont.asset</c>: a DYNAMIC, multi-atlas TMP font asset built from
    /// <c>Assets/VRTemplateAssets/Fonts/Inter/Inter-Regular.ttf</c> with Turkish glyphs pre-baked (STACK_NOTES §7).
    /// The atlas texture(s) and material are sub-assets, so the asset is self-contained; <c>clearDynamicDataOnBuild</c> is
    /// forced off so the baked glyphs ship in the player. Load at runtime with <c>Resources.Load&lt;TMP_FontAsset&gt;("MultiTravelFont")</c>.
    /// </summary>
    public static class FontAssetGenerator
    {
        public const string SourceFontPath = "Assets/VRTemplateAssets/Fonts/Inter/Inter-Regular.ttf";
        public const string FontAssetPath = GeneratedAssetUtil.ResourcesFolder + "/MultiTravelFont.asset";
        public const string ResourceName = "MultiTravelFont";

        public const int SamplingPointSize = 90;
        public const int AtlasPadding = 9;
        public const int AtlasSize = 1024;

        /// <summary>Characters pre-added to the atlas (Turkish alphabet, digits, punctuation, plus q/w/x for names and e-mails).</summary>
        public const string PrebakedCharacters =
            "abcçdefgğhıijklmnoöprsştuüvyzABCÇDEFGĞHIİJKLMNOÖPRSŞTUÜVYZ0123456789 .,:;!?+-/()%·•" +
            "qwxQWX@_'\"&*=#<>[]";

        /// <summary>Loads the generated font asset, or null.</summary>
        public static TMP_FontAsset Load()
        {
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        }

        /// <summary>
        /// Creates the font asset if missing, otherwise repairs it in place (dynamic mode, source font, multi-atlas, sub-assets) and
        /// tops up the pre-baked characters. With <paramref name="force"/> a broken / static asset at the path is deleted and recreated
        /// (its GUID changes; references must be re-wired by the scene generator).
        /// </summary>
        public static GeneratorReport Generate(bool force = false)
        {
            var report = new GeneratorReport("Generate TMP font asset");
            try
            {
                var font = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
                if (font == null)
                {
                    report.AddError($"Source font not found at '{SourceFontPath}'.");
                    return report;
                }

                GeneratedAssetUtil.EnsureFolder(GeneratedAssetUtil.ResourcesFolder);

                var existing = Load();
                if (existing == null && AssetDatabase.LoadMainAssetAtPath(FontAssetPath) != null)
                {
                    if (!force)
                    {
                        report.AddError($"'{FontAssetPath}' exists but is not a TMP_FontAsset. Re-run with force to replace it.");
                        return report;
                    }

                    AssetDatabase.DeleteAsset(FontAssetPath);
                }

                if (existing != null)
                {
                    bool broken = existing.material == null || existing.atlasTextures == null || existing.atlasTextures.Length == 0 || existing.atlasTextures[0] == null;
                    bool wrongMode = existing.atlasPopulationMode != AtlasPopulationMode.Dynamic;
                    if (broken || wrongMode)
                    {
                        if (!force)
                        {
                            report.AddError($"'{FontAssetPath}' is {(broken ? "missing its material/atlas" : "not dynamic")}. Re-run with force to recreate it.");
                            return report;
                        }

                        AssetDatabase.DeleteAsset(FontAssetPath);
                        existing = null;
                    }
                }

                TMP_FontAsset fontAsset;
                if (existing == null)
                {
                    fontAsset = Create(font);
                    report.Created.Add(FontAssetPath);
                }
                else
                {
                    fontAsset = existing;
                    Repair(fontAsset, font, report);
                    report.Updated.Add(FontAssetPath);
                }

                if (!fontAsset.TryAddCharacters(PrebakedCharacters, out string missing))
                {
                    if (!string.IsNullOrEmpty(missing))
                    {
                        report.Warnings.Add($"Glyphs not available in {SourceFontPath}: '{missing}'.");
                    }
                }

                EnsureSubAssets(fontAsset);
                EditorUtility.SetDirty(fontAsset);
                AssetDatabase.SaveAssets();

                fontAsset.HasCharacters("ğĞışŞİçÇöÖüÜ", out uint[] stillMissing, false, false);
                if (stillMissing != null && stillMissing.Length > 0)
                {
                    report.AddError($"Turkish glyphs missing after generation: {stillMissing.Length} code point(s).");
                }
            }
            catch (Exception ex)
            {
                report.AddError(ex.ToString());
            }

            return report;
        }

        private static TMP_FontAsset Create(Font font)
        {
            // Exact overload (com.unity.ugui 2.0.0, TMP_FontAsset.cs:556).
            var fontAsset = TMP_FontAsset.CreateFontAsset(font, SamplingPointSize, AtlasPadding, GlyphRenderMode.SDFAA,
                AtlasSize, AtlasSize, AtlasPopulationMode.Dynamic, true);
            if (fontAsset == null)
            {
                throw new InvalidOperationException($"TMP_FontAsset.CreateFontAsset failed for '{SourceFontPath}' (is 'Include Font Data' enabled on the TTF?).");
            }

            fontAsset.name = ResourceName;
            fontAsset.material.name = ResourceName + " Material";
            fontAsset.atlasTextures[0].name = ResourceName + " Atlas";

            AssetDatabase.CreateAsset(fontAsset, FontAssetPath);
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            AssetDatabase.AddObjectToAsset(fontAsset.atlasTextures[0], fontAsset);
            SetClearDynamicDataOnBuild(fontAsset, false);
            return fontAsset;
        }

        private static void Repair(TMP_FontAsset fontAsset, Font font, GeneratorReport report)
        {
            if (fontAsset.sourceFontFile == null)
            {
                // TMP_FontAsset.sourceFontFile has an internal setter; write the serialized fields instead.
                var so = new SerializedObject(fontAsset);
                var fontProp = so.FindProperty("m_SourceFontFile");
                var guidProp = so.FindProperty("m_SourceFontFileGUID");
                if (fontProp != null)
                {
                    fontProp.objectReferenceValue = font;
                }

                if (guidProp != null)
                {
                    guidProp.stringValue = AssetDatabase.AssetPathToGUID(SourceFontPath);
                }

                so.ApplyModifiedPropertiesWithoutUndo();
                fontAsset.ReadFontAssetDefinition();
                report.Warnings.Add("Source font reference was missing and has been restored.");
            }

            if (!fontAsset.isMultiAtlasTexturesEnabled)
            {
                fontAsset.isMultiAtlasTexturesEnabled = true;
            }

            SetClearDynamicDataOnBuild(fontAsset, false);
        }

        /// <summary>Makes sure the material and every atlas texture are stored inside the font asset file.</summary>
        private static void EnsureSubAssets(TMP_FontAsset fontAsset)
        {
            if (fontAsset.material != null && !AssetDatabase.IsSubAsset(fontAsset.material) && !AssetDatabase.Contains(fontAsset.material))
            {
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }

            var textures = fontAsset.atlasTextures;
            for (int i = 0; textures != null && i < textures.Length; i++)
            {
                var tex = textures[i];
                if (tex == null)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(tex.name))
                {
                    tex.name = $"{ResourceName} Atlas {i}";
                }

                if (!AssetDatabase.Contains(tex))
                {
                    AssetDatabase.AddObjectToAsset(tex, fontAsset);
                }
            }
        }

        /// <summary><c>TMP_FontAsset.clearDynamicDataOnBuild</c> is internal; set the serialized field instead.</summary>
        private static void SetClearDynamicDataOnBuild(TMP_FontAsset fontAsset, bool value)
        {
            var so = new SerializedObject(fontAsset);
            var prop = so.FindProperty("m_ClearDynamicDataOnBuild");
            if (prop != null && prop.boolValue != value)
            {
                prop.boolValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
