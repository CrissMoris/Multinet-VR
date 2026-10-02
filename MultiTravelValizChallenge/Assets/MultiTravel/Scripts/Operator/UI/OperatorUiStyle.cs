using TMPro;
using UnityEngine;

namespace MultiTravel.Operator.UI
{
    /// <summary>
    /// Colours, type sizes and the font used by the operator screen (ARCHITECTURE.md §2.9).
    /// Brand palette: deep blue #0B3C8C, teal #19B394, orange #F39200 on a light grey background with white cards.
    /// </summary>
    public static class OperatorUiStyle
    {
        /// <summary>Resources name of the dynamic TMP font asset with Turkish glyph coverage (STACK_NOTES.md §7).</summary>
        public const string FontResourceName = "MultiTravelFont";

        public static readonly Color Primary = Hex(0x0B3C8C);
        public static readonly Color Accent = Hex(0x19B394);
        public static readonly Color Warning = Hex(0xF39200);
        public static readonly Color Danger = Hex(0xD64545);
        public static readonly Color Background = Hex(0xEEF1F5);
        public static readonly Color Card = Color.white;
        public static readonly Color StatusStrip = Hex(0xF8FAFC);
        public static readonly Color Border = Hex(0xD5DBE3);
        public static readonly Color TextPrimary = Hex(0x1B2433);
        public static readonly Color TextSecondary = Hex(0x5B6575);
        public static readonly Color TextMuted = Hex(0x9AA3AF);
        public static readonly Color TextOnDark = Color.white;
        public static readonly Color Disabled = Hex(0xC4CAD3);
        public static readonly Color RowAlternate = Hex(0xF3F6FA);
        public static readonly Color Neutral = Hex(0xE4E8EE);
        public static readonly Color Overlay = new Color(0.04f, 0.09f, 0.18f, 0.35f);

        public const float FontHuge = 140f;
        public const float FontStat = 64f;
        public const float FontTitle = 44f;
        public const float FontHeading = 32f;
        public const float FontBody = 24f;
        public const float FontButton = 26f;
        public const float FontSmall = 19f;
        public const float FontTiny = 16f;

        public const float ButtonHeight = 64f;
        public const float BigButtonHeight = 160f;
        public const float SmallButtonHeight = 40f;
        public const float InputHeight = 60f;
        public const float CardWidth = 1040f;

        private static TMP_FontAsset font;
        private static bool fontResolved;

        /// <summary>
        /// Project font loaded with <c>Resources.Load&lt;TMP_FontAsset&gt;("MultiTravelFont")</c>; falls back to the
        /// TMP default font asset (which only renders Turkish through its dynamic fallback). May be null when neither exists.
        /// </summary>
        public static TMP_FontAsset Font
        {
            get
            {
                if (!fontResolved || font == null)
                {
                    ResolveFont();
                }

                return font;
            }
        }

        /// <summary>Colour between <paramref name="color"/> and white.</summary>
        public static Color Lighten(Color color, float amount)
        {
            var result = Color.Lerp(color, Color.white, amount);
            result.a = color.a;
            return result;
        }

        /// <summary>Colour between <paramref name="color"/> and black.</summary>
        public static Color Darken(Color color, float amount)
        {
            var result = Color.Lerp(color, Color.black, amount);
            result.a = color.a;
            return result;
        }

        /// <summary>Same colour with a different alpha.</summary>
        public static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            font = null;
            fontResolved = false;
        }

        private static void ResolveFont()
        {
            bool firstAttempt = !fontResolved;
            fontResolved = true;
            font = Resources.Load<TMP_FontAsset>(FontResourceName);
            if (font != null)
            {
                return;
            }

            font = TMP_Settings.defaultFontAsset;
            if (firstAttempt)
            {
                if (font != null)
                {
                    Debug.LogWarning("[MultiTravel.Operator] TMP font asset 'Resources/" + FontResourceName +
                                     "' not found; using the TMP default font '" + font.name +
                                     "'. Turkish characters may render through the dynamic fallback only. Run MultiTravel/Generate/Content.");
                }
                else
                {
                    Debug.LogError("[MultiTravel.Operator] No TMP font asset available ('Resources/" + FontResourceName +
                                   "' missing and no TMP default font). Operator texts cannot render.");
                }
            }
        }

        private static Color Hex(int rgb)
        {
            return new Color32((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF), 255);
        }
    }
}
