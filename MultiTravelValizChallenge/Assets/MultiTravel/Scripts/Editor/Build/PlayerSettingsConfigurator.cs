using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MultiTravel.EditorTools
{
    /// <summary>
    /// Applies the Windows standalone player settings required by ARCHITECTURE §11. Idempotent; only touches values that differ.
    /// </summary>
    public static class PlayerSettingsConfigurator
    {
        public const string CompanyName = "ECR Etkinlik Bilgisayar";
        public const string ProductName = "MultiTravel Valiz Challenge";
        public const string DefaultVersion = "1.0.0";

        /// <summary>URP asset used by the Standalone default quality level (HDR, MSAA 4x, Standalone renderer; STACK_NOTES §8).</summary>
        public const string PcVrUrpAssetPath = "Assets/Settings/Project Configuration/Quality URP Config.asset";
        public const string PreferredQualityLevelName = "Ultra";
        private const string StandalonePlatformName = "Standalone";

        public static GeneratorReport Apply()
        {
            var report = new GeneratorReport("Configure player settings");
            try
            {
                SetString(report, "companyName", PlayerSettings.companyName, CompanyName, v => PlayerSettings.companyName = v);
                SetString(report, "productName", PlayerSettings.productName, ProductName, v => PlayerSettings.productName = v);
                if (string.IsNullOrWhiteSpace(PlayerSettings.bundleVersion))
                {
                    PlayerSettings.bundleVersion = DefaultVersion;
                    report.Updated.Add($"bundleVersion = {DefaultVersion}");
                }

                var standalone = NamedBuildTarget.Standalone;
                if (PlayerSettings.GetScriptingBackend(standalone) != ScriptingImplementation.Mono2x)
                {
                    PlayerSettings.SetScriptingBackend(standalone, ScriptingImplementation.Mono2x);
                    report.Updated.Add("Standalone scripting backend = Mono (IL2CPP module not installed)");
                }

                if (PlayerSettings.fullScreenMode != FullScreenMode.FullScreenWindow)
                {
                    PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
                    report.Updated.Add("fullScreenMode = FullScreenWindow");
                }

                SetBool(report, "resizableWindow", PlayerSettings.resizableWindow, true, v => PlayerSettings.resizableWindow = v);
                SetBool(report, "runInBackground", PlayerSettings.runInBackground, true, v => PlayerSettings.runInBackground = v);
                SetBool(report, "visibleInBackground", PlayerSettings.visibleInBackground, true, v => PlayerSettings.visibleInBackground = v);
                SetBool(report, "allowFullscreenSwitch", PlayerSettings.allowFullscreenSwitch, true, v => PlayerSettings.allowFullscreenSwitch = v);
                SetPlayerSettingsBool(report, "defaultIsNativeResolution", true);

                ConfigureQuality(report);
                AssetDatabase.SaveAssets();
            }
            catch (Exception ex)
            {
                report.AddError(ex.ToString());
            }

            return report;
        }

        /// <summary>
        /// Makes the Standalone default quality level (preferably "Ultra") use <see cref="PcVrUrpAssetPath"/> and be included for Standalone.
        /// QualitySettings has no public API for the per-platform default, so <c>m_PerPlatformDefaultQuality</c> is edited via SerializedObject.
        /// </summary>
        private static void ConfigureQuality(GeneratorReport report)
        {
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PcVrUrpAssetPath);
            if (urp == null)
            {
                report.AddError($"URP asset not found at '{PcVrUrpAssetPath}'.");
                return;
            }

            var names = QualitySettings.names;
            int level = Array.IndexOf(names, PreferredQualityLevelName);
            if (level < 0)
            {
                level = names.Length - 1;
                report.Warnings.Add($"Quality level '{PreferredQualityLevelName}' not found; using '{names[level]}'.");
            }

            var qualityAsset = LoadProjectSettingsAsset("ProjectSettings/QualitySettings.asset");
            if (qualityAsset == null)
            {
                report.AddError("Could not load ProjectSettings/QualitySettings.asset.");
                return;
            }

            var so = new SerializedObject(qualityAsset);
            bool changed = false;

            var levels = so.FindProperty("m_QualitySettings");
            if (levels != null && levels.isArray && level < levels.arraySize)
            {
                var rp = levels.GetArrayElementAtIndex(level).FindPropertyRelative("customRenderPipeline");
                if (rp != null && rp.objectReferenceValue != urp)
                {
                    rp.objectReferenceValue = urp;
                    changed = true;
                    report.Updated.Add($"Quality '{names[level]}' render pipeline = {PcVrUrpAssetPath}");
                }
            }
            else
            {
                report.Warnings.Add("QualitySettings.m_QualitySettings not found; render pipeline per level not verified.");
            }

            var defaults = so.FindProperty("m_PerPlatformDefaultQuality");
            bool foundStandalone = false;
            if (defaults != null && defaults.isArray)
            {
                for (int i = 0; i < defaults.arraySize; i++)
                {
                    var pair = defaults.GetArrayElementAtIndex(i);
                    var key = pair.FindPropertyRelative("first");
                    var value = pair.FindPropertyRelative("second");
                    if (key != null && value != null && key.stringValue == StandalonePlatformName)
                    {
                        foundStandalone = true;
                        if (value.intValue != level)
                        {
                            value.intValue = level;
                            changed = true;
                            report.Updated.Add($"Standalone default quality = {names[level]}");
                        }
                    }
                }
            }

            if (!foundStandalone)
            {
                report.Warnings.Add("Standalone entry not found in m_PerPlatformDefaultQuality; set the default quality manually (Project Settings > Quality).");
            }

            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            if (!QualitySettings.IsPlatformIncluded(StandalonePlatformName, level))
            {
                if (QualitySettings.TryIncludePlatformAt(StandalonePlatformName, level, out Exception error))
                {
                    report.Updated.Add($"Quality '{names[level]}' included for Standalone");
                }
                else
                {
                    report.AddError($"Could not include quality level '{names[level]}' for Standalone: {error?.Message}");
                }
            }

            if (GraphicsSettings.defaultRenderPipeline == null)
            {
                report.Warnings.Add("GraphicsSettings default render pipeline is empty; levels without their own URP asset would fall back to the built-in pipeline.");
            }
        }

        /// <summary>Loads the main object of a ProjectSettings/*.asset file (e.g. QualitySettings, PlayerSettings).</summary>
        internal static UnityEngine.Object LoadProjectSettingsAsset(string path)
        {
            var objects = AssetDatabase.LoadAllAssetsAtPath(path);
            return objects != null && objects.Length > 0 ? objects[0] : null;
        }

        /// <summary>Writes a bool on the PlayerSettings object through SerializedObject (for members without a public setter).</summary>
        private static void SetPlayerSettingsBool(GeneratorReport report, string propertyName, bool value)
        {
            var playerSettings = LoadProjectSettingsAsset("ProjectSettings/ProjectSettings.asset");
            if (playerSettings == null)
            {
                report.Warnings.Add($"Could not load ProjectSettings.asset to set {propertyName}.");
                return;
            }

            var so = new SerializedObject(playerSettings);
            var prop = so.FindProperty(propertyName);
            if (prop == null || prop.propertyType != SerializedPropertyType.Boolean)
            {
                report.Warnings.Add($"PlayerSettings.{propertyName} not found; set it manually.");
                return;
            }

            if (prop.boolValue != value)
            {
                prop.boolValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
                report.Updated.Add($"{propertyName} = {value}");
            }
        }

        private static void SetString(GeneratorReport report, string label, string current, string wanted, Action<string> apply)
        {
            if (!string.Equals(current, wanted, StringComparison.Ordinal))
            {
                apply(wanted);
                report.Updated.Add($"{label} = {wanted}");
            }
        }

        private static void SetBool(GeneratorReport report, string label, bool current, bool wanted, Action<bool> apply)
        {
            if (current != wanted)
            {
                apply(wanted);
                report.Updated.Add($"{label} = {wanted}");
            }
        }
    }
}
