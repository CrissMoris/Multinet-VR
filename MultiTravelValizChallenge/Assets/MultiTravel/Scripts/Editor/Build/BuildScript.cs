using System;
using System.Collections.Generic;
using System.IO;
using MultiTravel.EditorTools.Art;
using MultiTravel.EditorTools.Validation;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MultiTravel.EditorTools
{
    /// <summary>Serializable outcome of <see cref="BuildScript.BuildWindowsTo"/>.</summary>
    [Serializable]
    public sealed class BuildOutcome
    {
        public bool Success;
        public string OutputPath;
        public bool Development;
        public string Result;
        public int TotalErrors;
        public int TotalWarnings;
        public ulong TotalSizeBytes;
        public double DurationSeconds;
        public List<string> Errors = new List<string>();
        public List<string> Warnings = new List<string>();
        public string StationConfig;
    }

    /// <summary>
    /// Windows x64 (Mono) player build (ARCHITECTURE §8, §11).
    /// Batch: <c>-executeMethod MultiTravel.EditorTools.BuildScript.BuildWindows [-buildOutput &lt;path.exe&gt;] [-development]</c>.
    /// </summary>
    public static class BuildScript
    {
        public const string ExecutableName = "MultiTravel Packing Challenge.exe";

        /// <summary>Default output: <c>&lt;project&gt;/Build/Windows/MultiTravel Packing Challenge.exe</c>.</summary>
        public static string DefaultOutputPath => Path.Combine(GeneratedAssetUtil.ProjectRoot, "Build", "Windows", ExecutableName);

        /// <summary>
        /// Command-line entry point. Reads <c>-buildOutput &lt;path&gt;</c> (aliases <c>-output-path</c>, <c>--output-path</c>) and
        /// <c>-development</c> (bare flag or followed by true/false). Exits the editor with code 1 on failure in batch mode.
        /// </summary>
        public static void BuildWindows()
        {
            var args = Environment.GetCommandLineArgs();
            string output = GetArgValue(args, "-buildOutput", "--buildOutput", "-output-path", "--output-path");
            bool development = GetFlag(args, "-development", "--development");
            string stationConfig = GetArgValue(args, "-stationConfig", "--stationConfig");

            BuildOutcome outcome;
            try
            {
                outcome = BuildWindowsTo(string.IsNullOrWhiteSpace(output) ? DefaultOutputPath : output, development, stationConfig);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MultiTravel] Build threw: {ex}");
                outcome = new BuildOutcome { Success = false };
            }

            if (!outcome.Success && Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Configures the project, validates it (aborting on errors) and builds StandaloneWindows64 to <paramref name="outputPath"/>.
        /// Never exits the editor; callers decide.
        /// </summary>
        /// <summary>Repository-level deployment config copied into the player when no explicit station config is given.</summary>
        public static string DefaultStationConfigPath => Path.GetFullPath(Path.Combine(GeneratedAssetUtil.ProjectRoot, "..", "Deployment", "multitravel.config.json"));

        public static BuildOutcome BuildWindowsTo(string outputPath, bool development, string stationConfigPath = null)
        {
            var outcome = new BuildOutcome { Development = development };
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                outputPath = DefaultOutputPath;
            }

            if (!outputPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                outputPath = Path.Combine(outputPath, ExecutableName);
            }

            outputPath = Path.IsPathRooted(outputPath) ? outputPath : Path.GetFullPath(Path.Combine(GeneratedAssetUtil.ProjectRoot, outputPath));
            outcome.OutputPath = outputPath;

            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                return Fail(outcome, "Cannot build while in Play mode or compiling.");
            }

            // 1. Build scene list must start with the Bootstrap scene.
            var scenes = EditorBuildSettings.scenes;
            if (scenes == null || scenes.Length == 0 || scenes[0] == null ||
                !string.Equals(scenes[0].path, ProjectValidator.BootstrapScenePath, StringComparison.Ordinal) || !scenes[0].enabled)
            {
                return Fail(outcome, $"{ProjectValidator.BootstrapScenePath} must be the first, enabled scene in Build Settings (run the scene generator).");
            }

            // 2. Configure player + XR.
            var player = PlayerSettingsConfigurator.Apply();
            player.Log();
            CollectReport(outcome, player);
            var xr = XrConfigurator.Apply();
            xr.Log();
            CollectReport(outcome, xr);
            if (!player.Success || !xr.Success)
            {
                return Fail(outcome, "Project configuration failed.");
            }

            // 3. Validate.
            var validation = ProjectValidator.Validate(true);
            validation.Log();
            outcome.Warnings.AddRange(validation.Warnings);
            if (validation.HasErrors)
            {
                outcome.Errors.AddRange(validation.Errors);
                return Fail(outcome, "Project validation failed.");
            }

            // 4. Build.
            var enabledScenes = new List<string>();
            foreach (var s in scenes)
            {
                if (s != null && s.enabled)
                {
                    enabledScenes.Add(s.path);
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            var options = new BuildPlayerOptions
            {
                scenes = enabledScenes.ToArray(),
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = development ? BuildOptions.Development : BuildOptions.None
            };

            Debug.Log($"[MultiTravel] Building {(development ? "development" : "release")} player to {outputPath} ({enabledScenes.Count} scene(s)).");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            outcome.Result = summary.result.ToString();
            outcome.TotalErrors = summary.totalErrors;
            outcome.TotalWarnings = summary.totalWarnings;
            outcome.TotalSizeBytes = summary.totalSize;
            outcome.DurationSeconds = summary.totalTime.TotalSeconds;

            if (summary.result != BuildResult.Succeeded)
            {
                foreach (var step in report.steps)
                {
                    foreach (var message in step.messages)
                    {
                        if (message.type == LogType.Error || message.type == LogType.Exception)
                        {
                            outcome.Errors.Add($"{step.name}: {message.content}");
                        }
                    }
                }

                return Fail(outcome, $"Build {summary.result} with {summary.totalErrors} error(s).");
            }

            FinalizeStationConfig(outputPath, stationConfigPath, outcome);
            outcome.Success = true;
            Debug.Log($"[MultiTravel] Build succeeded: {outputPath} ({summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalSeconds:F0} s).");
            return outcome;
        }

        /// <summary>
        /// The editor's StreamingAssets/multitravel.config.json is a developer file (usually pointing at a local Supabase).
        /// The player must carry the event's station config instead: the given file, else Deployment/multitravel.config.json.
        /// When neither exists, a localhost config is removed from the player so a station can never silently send results
        /// to a developer machine; the operator panel then shows the backend as unreachable until a config is supplied.
        /// </summary>
        private static void FinalizeStationConfig(string exePath, string stationConfigPath, BuildOutcome outcome)
        {
            string dataFolder = Path.Combine(Path.GetDirectoryName(exePath), Path.GetFileNameWithoutExtension(exePath) + "_Data", "StreamingAssets");
            string target = Path.Combine(dataFolder, "multitravel.config.json");
            string source = !string.IsNullOrWhiteSpace(stationConfigPath) ? Path.GetFullPath(stationConfigPath) : DefaultStationConfigPath;
            if (File.Exists(source))
            {
                Directory.CreateDirectory(dataFolder);
                File.Copy(source, target, true);
                outcome.StationConfig = source;
                if (LooksLocal(File.ReadAllText(source)))
                {
                    outcome.Warnings.Add($"Station config {source} points at localhost; stations will not reach a shared backend.");
                }

                Debug.Log($"[MultiTravel] Station config copied into the player: {source}");
                return;
            }

            if (File.Exists(target) && LooksLocal(File.ReadAllText(target)))
            {
                File.Delete(target);
                outcome.Warnings.Add("No station config found (Deployment/multitravel.config.json); the developer localhost config was removed from the player. Put the event config into StreamingAssets or %USERPROFILE%/AppData/LocalLow before the event.");
            }
            else if (!File.Exists(target))
            {
                outcome.Warnings.Add("Player has no multitravel.config.json; AppConfig defaults are used (no backend credentials unless set in AppConfig).");
            }
        }

        private static bool LooksLocal(string json)
        {
            return json.IndexOf("127.0.0.1", StringComparison.Ordinal) >= 0 || json.IndexOf("localhost", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void CollectReport(BuildOutcome outcome, GeneratorReport report)
        {
            outcome.Warnings.AddRange(report.Warnings);
            outcome.Errors.AddRange(report.Errors);
        }

        private static BuildOutcome Fail(BuildOutcome outcome, string message)
        {
            outcome.Success = false;
            outcome.Errors.Add(message);
            Debug.LogError($"[MultiTravel] BUILD FAILED: {message}\n  " + string.Join("\n  ", outcome.Errors));
            return outcome;
        }

        internal static string GetArgValue(string[] args, params string[] names)
        {
            for (int i = 0; i < args.Length; i++)
            {
                foreach (var name in names)
                {
                    if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                    {
                        return args[i + 1];
                    }

                    var prefix = name + "=";
                    if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return args[i].Substring(prefix.Length);
                    }
                }
            }

            return null;
        }

        internal static bool GetFlag(string[] args, params string[] names)
        {
            for (int i = 0; i < args.Length; i++)
            {
                foreach (var name in names)
                {
                    if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    {
                        if (i + 1 < args.Length && bool.TryParse(args[i + 1], out bool explicitValue))
                        {
                            return explicitValue;
                        }

                        return true;
                    }

                    var prefix = name + "=";
                    if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && bool.TryParse(args[i].Substring(prefix.Length), out bool v))
                    {
                        return v;
                    }
                }
            }

            return false;
        }
    }
}
