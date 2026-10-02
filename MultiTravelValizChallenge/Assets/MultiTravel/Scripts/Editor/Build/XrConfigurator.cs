using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace MultiTravel.EditorTools
{
    /// <summary>
    /// Configures XR Plug-in Management + OpenXR for the Windows (Standalone) target per ARCHITECTURE §3 / STACK_NOTES §4, §11:
    /// OpenXR loader assigned and initialised on start, Meta Quest Touch Plus / Touch Pro / Oculus Touch controller profiles,
    /// Hand Interaction Profile, Hand Tracking Subsystem and Meta Hand Tracking Aim enabled, render mode Single Pass Instanced.
    /// Idempotent.
    /// </summary>
    public static class XrConfigurator
    {
        public const string OpenXRLoaderTypeName = "UnityEngine.XR.OpenXR.OpenXRLoader";

        // XR Hands feature ids (UnityEngine.XR.Hands.OpenXR.HandTracking.featureId / MetaHandTrackingAim.featureId in com.unity.xr.hands 1.7.3).
        // Kept as literals so this assembly does not need a reference to Unity.XR.Hands.
        public const string HandTrackingFeatureId = "com.unity.openxr.feature.input.handtracking";
        public const string MetaHandTrackingAimFeatureId = "com.unity.openxr.feature.input.metahandtrackingaim";

        /// <summary>Feature ids that must be enabled on Standalone, with a human label.</summary>
        public static readonly IReadOnlyList<KeyValuePair<string, string>> RequiredStandaloneFeatures = new[]
        {
            new KeyValuePair<string, string>(MetaQuestTouchPlusControllerProfile.featureId, "Meta Quest Touch Plus Controller Profile"),
            new KeyValuePair<string, string>(MetaQuestTouchProControllerProfile.featureId, "Meta Quest Touch Pro Controller Profile"),
            new KeyValuePair<string, string>(OculusTouchControllerProfile.featureId, "Oculus Touch Controller Profile"),
            new KeyValuePair<string, string>(HandInteractionProfile.featureId, "Hand Interaction Profile"),
            new KeyValuePair<string, string>(HandTrackingFeatureId, "Hand Tracking Subsystem"),
            new KeyValuePair<string, string>(MetaHandTrackingAimFeatureId, "Meta Hand Tracking Aim"),
        };

        public static GeneratorReport Apply()
        {
            var report = new GeneratorReport("Configure XR (Standalone OpenXR)");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                report.AddError("Cannot change XR settings while in Play mode.");
                return report;
            }

            try
            {
                ConfigureLoader(report);
                if (report.Success)
                {
                    ConfigureOpenXr(report);
                }

                AssetDatabase.SaveAssets();
            }
            catch (Exception ex)
            {
                report.AddError(ex.ToString());
            }

            return report;
        }

        /// <summary>True when the Standalone XR manager lists the OpenXR loader.</summary>
        public static bool IsOpenXrLoaderAssigned()
        {
            var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            var manager = general != null ? general.AssignedSettings : null;
            if (manager == null)
            {
                return false;
            }

            foreach (var loader in manager.activeLoaders)
            {
                if (loader != null && loader.GetType().FullName == OpenXRLoaderTypeName)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Returns labels of required Standalone features that are missing or disabled (empty = all good).</summary>
        public static List<string> GetMissingStandaloneFeatures()
        {
            var missing = new List<string>();
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            if (settings == null)
            {
                missing.Add("OpenXR settings for Standalone");
                return missing;
            }

            foreach (var pair in RequiredStandaloneFeatures)
            {
                var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Standalone, pair.Key);
                if (feature == null || !feature.enabled)
                {
                    missing.Add(pair.Value);
                }
            }

            return missing;
        }

        private static void ConfigureLoader(GeneratorReport report)
        {
            var group = BuildTargetGroup.Standalone;
            var perTarget = FindOrCreatePerBuildTargetSettings(report);
            if (perTarget == null)
            {
                return;
            }

            if (!perTarget.HasManagerSettingsForBuildTarget(group))
            {
                perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
                report.Created.Add("XR manager settings for Standalone");
            }

            var general = perTarget.SettingsForBuildTarget(group);
            if (general == null)
            {
                report.AddError("XRGeneralSettings for Standalone could not be created.");
                return;
            }

            if (!general.InitManagerOnStart)
            {
                general.InitManagerOnStart = true;
                EditorUtility.SetDirty(general);
                report.Updated.Add("XR: Initialize XR on Startup = true (Standalone)");
            }

            var manager = general.AssignedSettings;
            if (manager == null)
            {
                report.AddError("XR manager settings for Standalone are missing.");
                return;
            }

            if (IsOpenXrLoaderAssigned())
            {
                report.Skipped.Add("OpenXR loader already assigned (Standalone)");
            }
            else if (XRPackageMetadataStore.AssignLoader(manager, OpenXRLoaderTypeName, group))
            {
                EditorUtility.SetDirty(manager);
                report.Updated.Add("XR: OpenXR loader assigned (Standalone)");
            }
            else
            {
                report.AddError("XRPackageMetadataStore.AssignLoader failed for OpenXR on Standalone (see console).");
            }

            EditorUtility.SetDirty(perTarget);
        }

        private static XRGeneralSettingsPerBuildTarget FindOrCreatePerBuildTargetSettings(GeneratorReport report)
        {
            if (EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget) && perTarget != null)
            {
                return perTarget;
            }

            var guids = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget");
            if (guids.Length > 0)
            {
                perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(AssetDatabase.GUIDToAssetPath(guids[0]));
                if (perTarget != null)
                {
                    EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
                    report.Updated.Add("XR: registered existing XRGeneralSettingsPerBuildTarget in EditorBuildSettings");
                    return perTarget;
                }
            }

            const string folder = "Assets/XR";
            const string path = folder + "/XRGeneralSettingsPerBuildTarget.asset";
            Art.GeneratedAssetUtil.EnsureFolder(folder);
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, path);
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            report.Created.Add(path);
            return perTarget;
        }

        private static void ConfigureOpenXr(GeneratorReport report)
        {
            var group = BuildTargetGroup.Standalone;
            FeatureHelpers.RefreshFeatures(group);
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (settings == null)
            {
                report.AddError("OpenXR settings for Standalone not found (open Project Settings > XR Plug-in Management > OpenXR once).");
                return;
            }

            // Single Pass Instanced is supported by URP 17 and the OpenXR plugin on Windows (D3D11) and halves draw-call submission.
            if (settings.renderMode != OpenXRSettings.RenderMode.SinglePassInstanced)
            {
                settings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
                report.Updated.Add("OpenXR render mode = Single Pass Instanced (Standalone)");
            }

            foreach (var pair in RequiredStandaloneFeatures)
            {
                OpenXRFeature feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(group, pair.Key);
                if (feature == null)
                {
                    report.AddError($"OpenXR feature '{pair.Value}' ({pair.Key}) is not installed for Standalone.");
                    continue;
                }

                if (!feature.enabled)
                {
                    feature.enabled = true;
                    EditorUtility.SetDirty(feature);
                    if (feature.enabled)
                    {
                        report.Updated.Add($"OpenXR feature enabled: {pair.Value}");
                    }
                    else
                    {
                        report.AddError($"OpenXR feature '{pair.Value}' refused to enable.");
                    }
                }
                else
                {
                    report.Skipped.Add($"OpenXR feature already enabled: {pair.Value}");
                }
            }

            EditorUtility.SetDirty(settings);
        }
    }
}
