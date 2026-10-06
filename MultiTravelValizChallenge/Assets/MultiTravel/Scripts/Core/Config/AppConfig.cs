using System;
using MultiTravel.Core.Completion;
using UnityEngine;

namespace MultiTravel.Core.Config
{
    /// <summary>
    /// Design-time configuration asset (<c>Assets/MultiTravel/Resources/AppConfig.asset</c>, ARCHITECTURE.md §6).
    /// The effective runtime values are produced by <see cref="ConfigLoader"/>, which layers the optional JSON override
    /// files on top of these defaults and returns an immutable <see cref="RuntimeConfig"/>.
    /// <para>
    /// SECURITY: <see cref="BackendSection.SupabaseAnonKey"/> is the client-safe anon / publishable key only.
    /// The Supabase <c>service_role</c> key or database password must never be stored in this asset or in the repository.
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "AppConfig", menuName = "MultiTravel/App Config")]
    public sealed class AppConfig : ScriptableObject
    {
        /// <summary>Resource name used by the bootstrap: <c>Resources.Load&lt;AppConfig&gt;("AppConfig")</c>.</summary>
        public const string ResourceName = "AppConfig";

        [Serializable]
        public sealed class BackendSection
        {
            [Tooltip("Supabase project URL, e.g. https://xyz.supabase.co (no trailing slash).")]
            public string SupabaseUrl = string.Empty;

            [Tooltip("CLIENT-SAFE anon / publishable key only. Never the service_role key.")]
            public string SupabaseAnonKey = string.Empty;

            [Tooltip("Event slug configured in the events table.")]
            public string EventSlug = string.Empty;

            [Tooltip("Event access code; every RPC validates it.")]
            public string EventAccessCode = string.Empty;

            [Tooltip("Identifier of this VR station. Empty = machine name.")]
            public string StationId = string.Empty;

            [Tooltip("HTTP timeout per request in seconds.")]
            [Min(1)]
            public int RequestTimeoutSeconds = 10;

            [Tooltip("Automatic retries after the first failed submission attempt (backoff 2, 4, 8, 16, 30 s).")]
            [Min(0)]
            public int MaxAutoRetries = 5;
        }

        [Serializable]
        public sealed class GameplaySection
        {
            public CompletionMode CompletionMode = CompletionMode.RequiredItemsPlaced;

            [Tooltip("0 disables the time limit.")]
            [Min(0)]
            public int TimeLimitSeconds = 0;

            [Tooltip("Subtract the score again when a counted item is taken out of the suitcase.")]
            public bool RevertScoreOnRemoval = true;

            [Tooltip("Seconds of VR countdown before the timer starts. 0 skips the countdown.")]
            [Min(0)]
            public int CountdownSeconds = 3;

            [Tooltip("Enable teleport / snap turn / move providers on the XR rig.")]
            public bool EnableLocomotion = false;

            [Tooltip("Shuffle item spawn slots every session.")]
            public bool ShuffleSpawnPositions = true;
        }

        [Serializable]
        public sealed class TextsSection
        {
            public string WelcomeTitle = "MultiTravel: Packing Challenge";

            public string WelcomeSubtitle = "Valizini doğru hazırla, en hızlı sen ol!";

            [TextArea(4, 12)]
            public string InstructionsText =
                "Odadaki eşyalar arasından bu seyahat için gerekli olanları seç ve valize yerleştir. " +
                "Doğru ürünler puan kazandırır, yanlış ürünler puan kaybettirir. " +
                "Gerekli tüm ürünler valize girdiğinde oyun tamamlanır; süren de sıralamanı belirler!";

            [TextArea(2, 6)]
            public string ScenarioText = "Arabayla, 1 gece konaklamalı toplantı seyahati";
        }

        [Serializable]
        public sealed class PrivacySection
        {
            [Tooltip("KVKK consent wording supplied by the customer. Empty = consent is not collected.")]
            [TextArea(4, 16)]
            public string ConsentText = string.Empty;

            [Tooltip("Version label stored with every consent, e.g. 2026-01.")]
            public string ConsentVersion = "1.0";
        }

        [Serializable]
        public sealed class BrandingSection
        {
            public string ProductTitle = "MultiTravel: Packing Challenge";

            [Tooltip("Optional logo. When null the UI shows the text wordmark.")]
            public Sprite LogoSprite;

            public Color PrimaryColor = new Color(0.047f, 0.267f, 0.549f, 1f);

            public Color AccentColor = new Color(0.949f, 0.541f, 0.133f, 1f);
        }

        [Serializable]
        public sealed class DebugSection
        {
            [Tooltip("Enable the XR Device Simulator when no HMD is present (editor / development builds only).")]
            public bool EnableDeviceSimulatorWhenNoHmd = true;
        }

        public BackendSection Backend = new BackendSection();

        public GameplaySection Gameplay = new GameplaySection();

        public TextsSection Texts = new TextsSection();

        public PrivacySection Privacy = new PrivacySection();

        public BrandingSection Branding = new BrandingSection();

        public DebugSection Debug = new DebugSection();

        /// <summary>Loads the asset from <c>Resources/AppConfig</c>; null when missing.</summary>
        public static AppConfig LoadFromResources()
        {
            return Resources.Load<AppConfig>(ResourceName);
        }
    }
}
