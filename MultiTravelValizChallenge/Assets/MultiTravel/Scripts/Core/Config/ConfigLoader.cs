using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MultiTravel.Core.Completion;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MultiTravel.Core.Config
{
    /// <summary>
    /// Pure, testable merge of <see cref="AppConfig"/> defaults with optional JSON overrides (ARCHITECTURE.md §6).
    /// <para>
    /// Override schema (partial, camelCase, every key optional; later sources win):
    /// <code>
    /// {
    ///   "backend":  { "supabaseUrl": "https://xyz.supabase.co", "supabaseAnonKey": "anon-key", "eventSlug": "event-2026",
    ///                 "eventAccessCode": "code", "stationId": "station-1", "requestTimeoutSeconds": 10, "maxAutoRetries": 5 },
    ///   "gameplay": { "completionMode": "RequiredItemsPlaced | ManualConfirm | RequiredItemsOrManual", "timeLimitSeconds": 0,
    ///                 "revertScoreOnRemoval": true, "countdownSeconds": 3, "enableLocomotion": false, "shuffleSpawnPositions": true },
    ///   "texts":    { "welcomeTitle": "...", "welcomeSubtitle": "...", "instructionsText": "...", "scenarioText": "..." },
    ///   "privacy":  { "consentText": "...", "consentVersion": "2026-01" },
    ///   "branding": { "productTitle": "...", "primaryColor": "#0C4489", "accentColor": "#F28A22FF" },
    ///   "debug":    { "enableDeviceSimulatorWhenNoHmd": true }
    /// }
    /// </code>
    /// Keys are matched case-insensitively. Unknown keys are ignored. A key with the wrong type is ignored with a warning.
    /// A file that is missing is silently skipped; a file that is not valid JSON produces a warning and is skipped.
    /// Colors use HTML notation (<c>#RRGGBB</c> or <c>#RRGGBBAA</c>). <c>branding.logoSprite</c> cannot be overridden (asset reference).
    /// </para>
    /// </summary>
    public static class ConfigLoader
    {
        public const string StreamingSourceName = "StreamingAssets";
        public const string PersistentSourceName = "PersistentData";

        /// <summary>
        /// Reads <c>StreamingAssets/multitravel.config.json</c> then <c>persistentDataPath/multitravel.config.json</c>
        /// and merges them over the asset defaults. Warnings are logged; the app keeps running on any error.
        /// Windows standalone / editor only (StreamingAssets is read with <see cref="File"/>).
        /// </summary>
        public static RuntimeConfig LoadFromDisk(AppConfig appConfig)
        {
            var ioWarnings = new List<string>();
            string streamingJson = ReadIfExists(Path.Combine(Application.streamingAssetsPath, RuntimeConfig.OverrideFileName), StreamingSourceName, ioWarnings);
            string persistentJson = ReadIfExists(Path.Combine(Application.persistentDataPath, RuntimeConfig.OverrideFileName), PersistentSourceName, ioWarnings);

            var config = Load(appConfig, streamingJson, persistentJson, Application.version, ioWarnings);
            for (int i = 0; i < config.Warnings.Count; i++)
            {
                Debug.LogWarning("[MultiTravel] Config: " + config.Warnings[i]);
            }

            return config;
        }

        /// <summary>
        /// Pure merge. <paramref name="streamingJsonOrNull"/> is applied first, then <paramref name="persistentJsonOrNull"/>.
        /// A null <paramref name="appConfig"/> yields the built-in defaults.
        /// </summary>
        public static RuntimeConfig Load(AppConfig appConfig, string streamingJsonOrNull, string persistentJsonOrNull, string clientVersion = null)
        {
            return Load(appConfig, streamingJsonOrNull, persistentJsonOrNull, clientVersion, null);
        }

        private static RuntimeConfig Load(AppConfig appConfig, string streamingJson, string persistentJson, string clientVersion, List<string> initialWarnings)
        {
            var warnings = initialWarnings != null ? new List<string>(initialWarnings) : new List<string>();
            var sources = new List<string>(2);

            var backend = CopyBackend(appConfig != null ? appConfig.Backend : null);
            var gameplay = CopyGameplay(appConfig != null ? appConfig.Gameplay : null);
            var texts = CopyTexts(appConfig != null ? appConfig.Texts : null);
            var privacy = CopyPrivacy(appConfig != null ? appConfig.Privacy : null);
            var branding = CopyBranding(appConfig != null ? appConfig.Branding : null);
            var debug = CopyDebug(appConfig != null ? appConfig.Debug : null);

            ApplySource(StreamingSourceName, streamingJson, backend, gameplay, texts, privacy, branding, debug, sources, warnings);
            ApplySource(PersistentSourceName, persistentJson, backend, gameplay, texts, privacy, branding, debug, sources, warnings);

            Sanitize(backend, gameplay, privacy, warnings);

            return new RuntimeConfig(
                new BackendConfig(backend.SupabaseUrl, backend.SupabaseAnonKey, backend.EventSlug, backend.EventAccessCode,
                    backend.StationId, backend.RequestTimeoutSeconds, backend.MaxAutoRetries),
                new GameplayConfig(gameplay.CompletionMode, gameplay.TimeLimitSeconds, gameplay.RevertScoreOnRemoval,
                    gameplay.CountdownSeconds, gameplay.EnableLocomotion, gameplay.ShuffleSpawnPositions),
                new TextsConfig(texts.WelcomeTitle, texts.WelcomeSubtitle, texts.InstructionsText, texts.ScenarioText),
                new PrivacyConfig(privacy.ConsentText, privacy.ConsentVersion),
                new BrandingConfig(branding.ProductTitle, branding.LogoSprite, branding.PrimaryColor, branding.AccentColor),
                new DebugConfig(debug.EnableDeviceSimulatorWhenNoHmd),
                clientVersion,
                sources,
                warnings);
        }

        private static string ReadIfExists(string path, string sourceName, List<string> warnings)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                warnings.Add($"{sourceName}: could not read '{path}': {ex.Message}");
                return null;
            }
        }

        private static void ApplySource(
            string sourceName,
            string json,
            AppConfig.BackendSection backend,
            AppConfig.GameplaySection gameplay,
            AppConfig.TextsSection texts,
            AppConfig.PrivacySection privacy,
            AppConfig.BrandingSection branding,
            AppConfig.DebugSection debug,
            List<string> sources,
            List<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            JObject root;
            try
            {
                var token = JToken.Parse(json);
                root = token as JObject;
                if (root == null)
                {
                    warnings.Add($"{sourceName}: override must be a JSON object; ignored.");
                    return;
                }
            }
            catch (JsonException ex)
            {
                warnings.Add($"{sourceName}: invalid JSON, override ignored ({ex.Message}).");
                return;
            }

            sources.Add(sourceName);

            var backendNode = GetSection(root, "backend", sourceName, warnings);
            if (backendNode != null)
            {
                ApplyString(backendNode, "supabaseUrl", sourceName, warnings, v => backend.SupabaseUrl = v);
                ApplyString(backendNode, "supabaseAnonKey", sourceName, warnings, v => backend.SupabaseAnonKey = v);
                ApplyString(backendNode, "eventSlug", sourceName, warnings, v => backend.EventSlug = v);
                ApplyString(backendNode, "eventAccessCode", sourceName, warnings, v => backend.EventAccessCode = v);
                ApplyString(backendNode, "stationId", sourceName, warnings, v => backend.StationId = v);
                ApplyInt(backendNode, "requestTimeoutSeconds", sourceName, warnings, v => backend.RequestTimeoutSeconds = v);
                ApplyInt(backendNode, "maxAutoRetries", sourceName, warnings, v => backend.MaxAutoRetries = v);
            }

            var gameplayNode = GetSection(root, "gameplay", sourceName, warnings);
            if (gameplayNode != null)
            {
                ApplyEnum<CompletionMode>(gameplayNode, "completionMode", sourceName, warnings, v => gameplay.CompletionMode = v);
                ApplyInt(gameplayNode, "timeLimitSeconds", sourceName, warnings, v => gameplay.TimeLimitSeconds = v);
                ApplyBool(gameplayNode, "revertScoreOnRemoval", sourceName, warnings, v => gameplay.RevertScoreOnRemoval = v);
                ApplyInt(gameplayNode, "countdownSeconds", sourceName, warnings, v => gameplay.CountdownSeconds = v);
                ApplyBool(gameplayNode, "enableLocomotion", sourceName, warnings, v => gameplay.EnableLocomotion = v);
                ApplyBool(gameplayNode, "shuffleSpawnPositions", sourceName, warnings, v => gameplay.ShuffleSpawnPositions = v);
            }

            var textsNode = GetSection(root, "texts", sourceName, warnings);
            if (textsNode != null)
            {
                ApplyString(textsNode, "welcomeTitle", sourceName, warnings, v => texts.WelcomeTitle = v);
                ApplyString(textsNode, "welcomeSubtitle", sourceName, warnings, v => texts.WelcomeSubtitle = v);
                ApplyString(textsNode, "instructionsText", sourceName, warnings, v => texts.InstructionsText = v);
                ApplyString(textsNode, "scenarioText", sourceName, warnings, v => texts.ScenarioText = v);
            }

            var privacyNode = GetSection(root, "privacy", sourceName, warnings);
            if (privacyNode != null)
            {
                ApplyString(privacyNode, "consentText", sourceName, warnings, v => privacy.ConsentText = v);
                ApplyString(privacyNode, "consentVersion", sourceName, warnings, v => privacy.ConsentVersion = v);
            }

            var brandingNode = GetSection(root, "branding", sourceName, warnings);
            if (brandingNode != null)
            {
                ApplyString(brandingNode, "productTitle", sourceName, warnings, v => branding.ProductTitle = v);
                ApplyColor(brandingNode, "primaryColor", sourceName, warnings, v => branding.PrimaryColor = v);
                ApplyColor(brandingNode, "accentColor", sourceName, warnings, v => branding.AccentColor = v);
            }

            var debugNode = GetSection(root, "debug", sourceName, warnings);
            if (debugNode != null)
            {
                ApplyBool(debugNode, "enableDeviceSimulatorWhenNoHmd", sourceName, warnings, v => debug.EnableDeviceSimulatorWhenNoHmd = v);
            }
        }

        private static void Sanitize(AppConfig.BackendSection backend, AppConfig.GameplaySection gameplay, AppConfig.PrivacySection privacy, List<string> warnings)
        {
            if (backend.RequestTimeoutSeconds <= 0)
            {
                warnings.Add($"backend.requestTimeoutSeconds must be positive (was {backend.RequestTimeoutSeconds}); using 10.");
                backend.RequestTimeoutSeconds = 10;
            }

            if (backend.MaxAutoRetries < 0)
            {
                warnings.Add($"backend.maxAutoRetries must not be negative (was {backend.MaxAutoRetries}); using 0.");
                backend.MaxAutoRetries = 0;
            }

            if (string.IsNullOrWhiteSpace(backend.StationId))
            {
                backend.StationId = Environment.MachineName;
            }

            if (gameplay.TimeLimitSeconds < 0)
            {
                warnings.Add($"gameplay.timeLimitSeconds must not be negative (was {gameplay.TimeLimitSeconds}); using 0.");
                gameplay.TimeLimitSeconds = 0;
            }

            if (gameplay.CountdownSeconds < 0)
            {
                warnings.Add($"gameplay.countdownSeconds must not be negative (was {gameplay.CountdownSeconds}); using 0.");
                gameplay.CountdownSeconds = 0;
            }

            if (!string.IsNullOrWhiteSpace(privacy.ConsentText) && string.IsNullOrWhiteSpace(privacy.ConsentVersion))
            {
                warnings.Add("privacy.consentVersion is empty although consentText is set; using '1.0'.");
                privacy.ConsentVersion = "1.0";
            }
        }

        // ----- section copies (AppConfig sections double as mutable staging objects) -----

        private static AppConfig.BackendSection CopyBackend(AppConfig.BackendSection source)
        {
            var copy = new AppConfig.BackendSection();
            if (source == null)
            {
                return copy;
            }

            copy.SupabaseUrl = source.SupabaseUrl;
            copy.SupabaseAnonKey = source.SupabaseAnonKey;
            copy.EventSlug = source.EventSlug;
            copy.EventAccessCode = source.EventAccessCode;
            copy.StationId = source.StationId;
            copy.RequestTimeoutSeconds = source.RequestTimeoutSeconds;
            copy.MaxAutoRetries = source.MaxAutoRetries;
            return copy;
        }

        private static AppConfig.GameplaySection CopyGameplay(AppConfig.GameplaySection source)
        {
            var copy = new AppConfig.GameplaySection();
            if (source == null)
            {
                return copy;
            }

            copy.CompletionMode = source.CompletionMode;
            copy.TimeLimitSeconds = source.TimeLimitSeconds;
            copy.RevertScoreOnRemoval = source.RevertScoreOnRemoval;
            copy.CountdownSeconds = source.CountdownSeconds;
            copy.EnableLocomotion = source.EnableLocomotion;
            copy.ShuffleSpawnPositions = source.ShuffleSpawnPositions;
            return copy;
        }

        private static AppConfig.TextsSection CopyTexts(AppConfig.TextsSection source)
        {
            var copy = new AppConfig.TextsSection();
            if (source == null)
            {
                return copy;
            }

            copy.WelcomeTitle = source.WelcomeTitle;
            copy.WelcomeSubtitle = source.WelcomeSubtitle;
            copy.InstructionsText = source.InstructionsText;
            copy.ScenarioText = source.ScenarioText;
            return copy;
        }

        private static AppConfig.PrivacySection CopyPrivacy(AppConfig.PrivacySection source)
        {
            var copy = new AppConfig.PrivacySection();
            if (source == null)
            {
                return copy;
            }

            copy.ConsentText = source.ConsentText;
            copy.ConsentVersion = source.ConsentVersion;
            return copy;
        }

        private static AppConfig.BrandingSection CopyBranding(AppConfig.BrandingSection source)
        {
            var copy = new AppConfig.BrandingSection();
            if (source == null)
            {
                return copy;
            }

            copy.ProductTitle = source.ProductTitle;
            copy.LogoSprite = source.LogoSprite;
            copy.PrimaryColor = source.PrimaryColor;
            copy.AccentColor = source.AccentColor;
            return copy;
        }

        private static AppConfig.DebugSection CopyDebug(AppConfig.DebugSection source)
        {
            var copy = new AppConfig.DebugSection();
            if (source == null)
            {
                return copy;
            }

            copy.EnableDeviceSimulatorWhenNoHmd = source.EnableDeviceSimulatorWhenNoHmd;
            return copy;
        }

        // ----- JSON helpers -----

        private static JObject GetSection(JObject root, string key, string sourceName, List<string> warnings)
        {
            if (!TryGetToken(root, key, out var token))
            {
                return null;
            }

            if (token is JObject section)
            {
                return section;
            }

            warnings.Add($"{sourceName}: '{key}' must be an object; ignored.");
            return null;
        }

        private static bool TryGetToken(JObject obj, string key, out JToken token)
        {
            if (obj != null && obj.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out token) && token != null && token.Type != JTokenType.Null)
            {
                return true;
            }

            token = null;
            return false;
        }

        private static void ApplyString(JObject section, string key, string sourceName, List<string> warnings, Action<string> apply)
        {
            if (!TryGetToken(section, key, out var token))
            {
                return;
            }

            if (token.Type == JTokenType.String)
            {
                apply(((string)token) ?? string.Empty);
                return;
            }

            if (token is JValue primitive && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float || token.Type == JTokenType.Boolean))
            {
                apply(Convert.ToString(primitive.Value, CultureInfo.InvariantCulture));
                return;
            }

            warnings.Add($"{sourceName}: '{section.Path}.{key}' must be a string; ignored.");
        }

        private static void ApplyInt(JObject section, string key, string sourceName, List<string> warnings, Action<int> apply)
        {
            if (!TryGetToken(section, key, out var token))
            {
                return;
            }

            switch (token.Type)
            {
                case JTokenType.Integer:
                    apply((int)token);
                    return;
                case JTokenType.Float:
                    apply((int)Math.Round((double)token));
                    return;
                case JTokenType.String:
                    if (int.TryParse((string)token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    {
                        apply(parsed);
                        return;
                    }

                    break;
            }

            warnings.Add($"{sourceName}: '{section.Path}.{key}' must be an integer; ignored.");
        }

        private static void ApplyBool(JObject section, string key, string sourceName, List<string> warnings, Action<bool> apply)
        {
            if (!TryGetToken(section, key, out var token))
            {
                return;
            }

            switch (token.Type)
            {
                case JTokenType.Boolean:
                    apply((bool)token);
                    return;
                case JTokenType.Integer:
                    apply((long)token != 0);
                    return;
                case JTokenType.String:
                    if (bool.TryParse((string)token, out var parsed))
                    {
                        apply(parsed);
                        return;
                    }

                    break;
            }

            warnings.Add($"{sourceName}: '{section.Path}.{key}' must be a boolean; ignored.");
        }

        private static void ApplyEnum<TEnum>(JObject section, string key, string sourceName, List<string> warnings, Action<TEnum> apply)
            where TEnum : struct, Enum
        {
            if (!TryGetToken(section, key, out var token))
            {
                return;
            }

            if (token.Type == JTokenType.String && Enum.TryParse<TEnum>(((string)token).Trim(), true, out var parsed) && Enum.IsDefined(typeof(TEnum), parsed))
            {
                apply(parsed);
                return;
            }

            if (token.Type == JTokenType.Integer && Enum.IsDefined(typeof(TEnum), (int)token))
            {
                apply((TEnum)Enum.ToObject(typeof(TEnum), (int)token));
                return;
            }

            warnings.Add($"{sourceName}: '{section.Path}.{key}' must be one of {string.Join(", ", Enum.GetNames(typeof(TEnum)))}; ignored.");
        }

        private static void ApplyColor(JObject section, string key, string sourceName, List<string> warnings, Action<Color> apply)
        {
            if (!TryGetToken(section, key, out var token))
            {
                return;
            }

            if (token.Type == JTokenType.String)
            {
                var text = ((string)token).Trim();
                if (text.Length > 0 && text[0] != '#')
                {
                    text = "#" + text;
                }

                if (ColorUtility.TryParseHtmlString(text, out var color))
                {
                    apply(color);
                    return;
                }
            }

            warnings.Add($"{sourceName}: '{section.Path}.{key}' must be an HTML color such as #RRGGBB; ignored.");
        }
    }
}
