using System;
using System.Collections.Generic;
using MultiTravel.Core.Completion;
using UnityEngine;

namespace MultiTravel.Core.Config
{
    /// <summary>Effective backend settings (ARCHITECTURE.md §6, §4).</summary>
    public sealed class BackendConfig
    {
        public BackendConfig(
            string supabaseUrl,
            string supabaseAnonKey,
            string eventSlug,
            string eventAccessCode,
            string stationId,
            int requestTimeoutSeconds,
            int maxAutoRetries)
        {
            SupabaseUrl = (supabaseUrl ?? string.Empty).Trim().TrimEnd('/');
            SupabaseAnonKey = (supabaseAnonKey ?? string.Empty).Trim();
            EventSlug = (eventSlug ?? string.Empty).Trim();
            EventAccessCode = (eventAccessCode ?? string.Empty).Trim();
            StationId = (stationId ?? string.Empty).Trim();
            RequestTimeoutSeconds = requestTimeoutSeconds;
            MaxAutoRetries = maxAutoRetries;
        }

        public string SupabaseUrl { get; }

        /// <summary>Client-safe anon / publishable key.</summary>
        public string SupabaseAnonKey { get; }

        public string EventSlug { get; }

        public string EventAccessCode { get; }

        public string StationId { get; }

        public int RequestTimeoutSeconds { get; }

        public int MaxAutoRetries { get; }

        /// <summary>URL, key and slug are present: enough for public calls such as the leaderboard.</summary>
        public bool HasEndpoint =>
            SupabaseUrl.Length > 0 && SupabaseAnonKey.Length > 0 && EventSlug.Length > 0;

        /// <summary>Everything required for authenticated RPC calls is present.</summary>
        public bool IsConfigured => HasEndpoint && EventAccessCode.Length > 0;

        /// <summary>Builds <c>{SupabaseUrl}/rest/v1/rpc/{functionName}</c>.</summary>
        public string RpcUrl(string functionName)
        {
            return SupabaseUrl + "/rest/v1/rpc/" + functionName;
        }
    }

    /// <summary>Effective gameplay settings.</summary>
    public sealed class GameplayConfig
    {
        public GameplayConfig(
            CompletionMode completionMode,
            int timeLimitSeconds,
            bool revertScoreOnRemoval,
            int countdownSeconds,
            bool enableLocomotion,
            bool shuffleSpawnPositions)
        {
            CompletionMode = completionMode;
            TimeLimitSeconds = timeLimitSeconds;
            RevertScoreOnRemoval = revertScoreOnRemoval;
            CountdownSeconds = countdownSeconds;
            EnableLocomotion = enableLocomotion;
            ShuffleSpawnPositions = shuffleSpawnPositions;
        }

        public CompletionMode CompletionMode { get; }

        /// <summary>0 disables the time limit.</summary>
        public int TimeLimitSeconds { get; }

        public bool RevertScoreOnRemoval { get; }

        /// <summary>0 skips the countdown state.</summary>
        public int CountdownSeconds { get; }

        public bool EnableLocomotion { get; }

        public bool ShuffleSpawnPositions { get; }
    }

    /// <summary>Effective UI texts (Turkish).</summary>
    public sealed class TextsConfig
    {
        public TextsConfig(string welcomeTitle, string welcomeSubtitle, string instructionsText, string scenarioText)
        {
            WelcomeTitle = welcomeTitle ?? string.Empty;
            WelcomeSubtitle = welcomeSubtitle ?? string.Empty;
            InstructionsText = instructionsText ?? string.Empty;
            ScenarioText = scenarioText ?? string.Empty;
        }

        public string WelcomeTitle { get; }

        public string WelcomeSubtitle { get; }

        public string InstructionsText { get; }

        public string ScenarioText { get; }
    }

    /// <summary>Effective privacy settings.</summary>
    public sealed class PrivacyConfig
    {
        public PrivacyConfig(string consentText, string consentVersion)
        {
            ConsentText = consentText ?? string.Empty;
            ConsentVersion = consentVersion ?? string.Empty;
        }

        /// <summary>KVKK wording; empty means consent is not collected.</summary>
        public string ConsentText { get; }

        public string ConsentVersion { get; }

        /// <summary>True when the registration form must show and require the consent checkbox.</summary>
        public bool IsConsentRequired => !string.IsNullOrWhiteSpace(ConsentText);
    }

    /// <summary>Effective branding settings.</summary>
    public sealed class BrandingConfig
    {
        public BrandingConfig(string productTitle, Sprite logoSprite, Color primaryColor, Color accentColor)
        {
            ProductTitle = productTitle ?? string.Empty;
            LogoSprite = logoSprite;
            PrimaryColor = primaryColor;
            AccentColor = accentColor;
        }

        public string ProductTitle { get; }

        /// <summary>May be null; the UI then shows the text wordmark.</summary>
        public Sprite LogoSprite { get; }

        public Color PrimaryColor { get; }

        public Color AccentColor { get; }
    }

    /// <summary>Effective debug settings.</summary>
    public sealed class DebugConfig
    {
        public DebugConfig(bool enableDeviceSimulatorWhenNoHmd)
        {
            EnableDeviceSimulatorWhenNoHmd = enableDeviceSimulatorWhenNoHmd;
        }

        public bool EnableDeviceSimulatorWhenNoHmd { get; }
    }

    /// <summary>
    /// Immutable merged configuration registered in <c>AppServices</c> (ARCHITECTURE.md §6):
    /// <c>AppConfig</c> defaults + <c>StreamingAssets/multitravel.config.json</c> + <c>persistentDataPath/multitravel.config.json</c>.
    /// </summary>
    public sealed class RuntimeConfig
    {
        /// <summary>File name of the JSON override, looked up in StreamingAssets and persistentDataPath.</summary>
        public const string OverrideFileName = "multitravel.config.json";

        public RuntimeConfig(
            BackendConfig backend,
            GameplayConfig gameplay,
            TextsConfig texts,
            PrivacyConfig privacy,
            BrandingConfig branding,
            DebugConfig debug,
            string clientVersion,
            IReadOnlyList<string> appliedSources,
            IReadOnlyList<string> warnings)
        {
            Backend = backend ?? throw new ArgumentNullException(nameof(backend));
            Gameplay = gameplay ?? throw new ArgumentNullException(nameof(gameplay));
            Texts = texts ?? throw new ArgumentNullException(nameof(texts));
            Privacy = privacy ?? throw new ArgumentNullException(nameof(privacy));
            Branding = branding ?? throw new ArgumentNullException(nameof(branding));
            Debug = debug ?? throw new ArgumentNullException(nameof(debug));
            ClientVersion = string.IsNullOrWhiteSpace(clientVersion) ? "0.0.0" : clientVersion.Trim();
            AppliedSources = appliedSources ?? Array.Empty<string>();
            Warnings = warnings ?? Array.Empty<string>();
        }

        public BackendConfig Backend { get; }

        public GameplayConfig Gameplay { get; }

        public TextsConfig Texts { get; }

        public PrivacyConfig Privacy { get; }

        public BrandingConfig Branding { get; }

        public DebugConfig Debug { get; }

        /// <summary>Application version sent as <c>p_client_version</c>.</summary>
        public string ClientVersion { get; }

        /// <summary>Names of the override sources that were applied, in order.</summary>
        public IReadOnlyList<string> AppliedSources { get; }

        /// <summary>Developer-facing warnings produced during the merge (also logged by <see cref="ConfigLoader.LoadFromDisk"/>).</summary>
        public IReadOnlyList<string> Warnings { get; }
    }
}
