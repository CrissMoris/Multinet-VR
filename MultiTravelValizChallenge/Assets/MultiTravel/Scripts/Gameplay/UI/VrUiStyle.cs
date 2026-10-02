using TMPro;
using UnityEngine;

namespace MultiTravel.Gameplay.UI
{
    /// <summary>
    /// Brand palette, sizes and the shared TMP font for the VR-side UI (backdrop panel, floating score labels,
    /// confirm button). Sizes are in canvas units for a world-space canvas scaled 0.001 (1 unit = 1 mm).
    /// </summary>
    public static class VrUiStyle
    {
        /// <summary>Resources name of the Turkish-capable dynamic TMP font created by the content generator.</summary>
        public const string FontResourceName = "MultiTravelFont";

        /// <summary>Deep blue #0B3C8C.</summary>
        public static readonly Color DeepBlue = new Color32(0x0B, 0x3C, 0x8C, 0xFF);

        /// <summary>Teal #19B394 (positive / success).</summary>
        public static readonly Color Teal = new Color32(0x19, 0xB3, 0x94, 0xFF);

        /// <summary>Orange #F39200 (accent / negative).</summary>
        public static readonly Color Orange = new Color32(0xF3, 0x92, 0x00, 0xFF);

        /// <summary>White.</summary>
        public static readonly Color White = Color.white;

        /// <summary>Panel background (deep blue, slightly transparent).</summary>
        public static readonly Color PanelBackground = new Color32(0x0B, 0x3C, 0x8C, 0xF0);

        /// <summary>Darker strip used behind the HUD row.</summary>
        public static readonly Color PanelStrip = new Color32(0x07, 0x2A, 0x63, 0xFF);

        /// <summary>Soft white for secondary text.</summary>
        public static readonly Color SecondaryText = new Color(1f, 1f, 1f, 0.82f);

        /// <summary>Error red used for failure texts.</summary>
        public static readonly Color ErrorText = new Color32(0xFF, 0x8A, 0x80, 0xFF);

        // Font sizes (canvas units = millimetres at scale 0.001). 60 mm cap height ≈ 36 pt+ legibility at 2 m.
        public const float TitleSize = 84f;
        public const float SubtitleSize = 52f;
        public const float HudSize = 64f;
        public const float HeadingSize = 96f;
        public const float BodySize = 50f;
        public const float CountdownSize = 360f;
        public const float FloatingLabelSize = 1.4f;

        private static TMP_FontAsset cachedFont;
        private static bool fontLoaded;

        /// <summary>
        /// The project font (<c>Resources/MultiTravelFont</c>) or, when missing, TMP's default font asset.
        /// The result is cached; null only when neither exists.
        /// </summary>
        public static TMP_FontAsset Font
        {
            get
            {
                if (!fontLoaded)
                {
                    fontLoaded = true;
                    cachedFont = Resources.Load<TMP_FontAsset>(FontResourceName);
                    if (cachedFont == null)
                    {
                        Debug.LogWarning(
                            "[MultiTravel] TMP font 'Resources/" + FontResourceName + "' not found; using the TMP default font " +
                            "(Turkish glyphs may be missing). Run MultiTravel/Generate/Content.");
                        cachedFont = TMP_Settings.defaultFontAsset;
                    }
                }

                return cachedFont;
            }
        }
    }
}
