using TMPro;
using UnityEngine;

namespace MultiTravel.Operator.UI
{
    /// <summary>
    /// Design system of the operator screen (OVERHAUL_PLAN.md §5, ARCHITECTURE.md §2.9): dark corporate theme, 8 px spacing
    /// grid, a six-step type scale and the project font. Everything is authored for a 1920x1080 reference canvas.
    /// </summary>
    public static class OperatorUiStyle
    {
        /// <summary>Resources name of the dynamic TMP font asset with Turkish glyph coverage (STACK_NOTES.md §7).</summary>
        public const string FontResourceName = "MultiTravelFont";

        private const string LogPrefix = "[MultiTravel.Operator] ";

        // ----- palette -----

        public static readonly Color Background = Hex(0x0C1424);
        public static readonly Color Card = Hex(0x142038);
        public static readonly Color Elevated = Hex(0x1B2A4A);
        public static readonly Color Border = Hex(0x2A4480);
        public static readonly Color TextPrimary = Hex(0xE8EEF8);
        public static readonly Color TextSecondary = Hex(0xAEB9CC);
        public static readonly Color TextMuted = Hex(0x7C89A3);

        /// <summary>Brand blue #0B3C8C (primary buttons, brand mark).</summary>
        public static readonly Color Primary = Hex(0x0B3C8C);

        /// <summary>Lighter blue used for focus rings and hover accents on dark surfaces.</summary>
        public static readonly Color PrimaryBright = Hex(0x3C78DC);

        /// <summary>Brand teal #19B394.</summary>
        public static readonly Color Accent = Hex(0x19B394);

        /// <summary>Brand orange #F39200.</summary>
        public static readonly Color Warning = Hex(0xF39200);

        /// <summary>Brand red #D9262C.</summary>
        public static readonly Color Danger = Hex(0xD9262C);

        /// <summary>Success green #2ECC8F.</summary>
        public static readonly Color Success = Hex(0x2ECC8F);

        /// <summary>Readable-on-dark red for inline error text.</summary>
        public static readonly Color DangerText = Hex(0xFF7C80);

        /// <summary>Dark text on teal / orange fills.</summary>
        public static readonly Color TextOnBright = Hex(0x04211B);

        public static readonly Color Dim = new Color(0.02f, 0.04f, 0.08f, 0.66f);
        public static readonly Color Transparent = new Color(0f, 0f, 0f, 0f);

        // ----- type scale (px at the 1920x1080 reference) -----

        public const float FontDisplay = 56f;
        public const float FontTitle = 40f;
        public const float FontHeading = 28f;
        public const float FontBody = 20f;
        public const float FontLabel = 16f;
        public const float FontCaption = 13f;

        /// <summary>Numerals only (timer, countdown): deliberately outside the text scale.</summary>
        public const float FontHero = 144f;

        /// <summary>Smallest size any label may use; smaller text is never created.</summary>
        public const float MinFontSize = 13f;

        /// <summary>TMP rich-text wrappers that make digits equally wide (tabular numbers).</summary>
        public const string TabularOpen = "<mspace=0.62em>";

        public const string TabularClose = "</mspace>";

        // ----- spacing grid -----

        public const float Space1 = 8f;
        public const float Space2 = 16f;
        public const float Space3 = 24f;
        public const float Space4 = 32f;
        public const float Space5 = 40f;
        public const float Space6 = 48f;

        // ----- geometry -----

        public const int RadiusCard = 16;
        public const int RadiusButton = 12;
        public const int RadiusField = 12;

        public const float TopBarHeight = 72f;
        public const float StepperWidth = 300f;
        public const float RailWidth = 400f;
        public const float ButtonHeight = 56f;
        public const float ButtonHeightSmall = 40f;
        public const float ButtonHeightLarge = 72f;
        public const float ButtonHeightHero = 88f;
        public const float FieldHeight = 64f;

        private static TMP_FontAsset font;
        private static bool fontResolved;
        private static bool fontChecked;

        /// <summary>
        /// Project font loaded with <c>Resources.Load&lt;TMP_FontAsset&gt;("MultiTravelFont")</c>; falls back to the
        /// TMP default font asset. May be null when neither exists.
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

        /// <summary>Text colour that reads on dark surfaces for a status colour (lightened).</summary>
        public static Color TextFor(Color status)
        {
            return Lighten(status, 0.4f);
        }

        /// <summary>
        /// Applies the project font and, explicitly, the font asset's own material to a label. Never leave a label on the TMP
        /// default material: a mismatching material is the usual cause of soft SDF text.
        /// </summary>
        public static void ApplyFont(TMP_Text label)
        {
            var asset = Font;
            if (asset == null)
            {
                return;
            }

            label.font = asset;
            var material = asset.material;
            if (material != null)
            {
                label.fontSharedMaterial = material;
            }
        }

        /// <summary>
        /// One-time runtime quality check of the TMP font asset against the actual canvas scale. Logs a warning with the
        /// recommended generator settings when the SDF atlas is sampled too coarsely for the largest text, or when the
        /// effective canvas scale makes the smallest text unreadable.
        /// </summary>
        public static void CheckFont(float canvasScale)
        {
            if (fontChecked)
            {
                return;
            }

            fontChecked = true;
            var asset = Font;
            if (asset == null)
            {
                return;
            }

            float sampling = asset.faceInfo.pointSize;
            int padding = asset.atlasPadding;
            bool isSdf = ((int)asset.atlasRenderMode & 0x1000) != 0;
            float scale = Mathf.Max(0.01f, canvasScale);
            float largest = FontDisplay * scale;
            const float recommendedSampling = 128f;
            const int recommendedPadding = 14;

            var problems = new System.Text.StringBuilder();
            if (!isSdf)
            {
                problems.Append("atlas render mode '").Append(asset.atlasRenderMode).Append("' is not SDF (text blurs when scaled); ");
            }

            if (sampling < recommendedSampling && largest > sampling * 0.55f)
            {
                problems.Append("sampling point size ").Append(sampling).Append(" is low for ").Append(Mathf.RoundToInt(largest))
                    .Append(" px headline text; ");
            }

            if (isSdf && padding < Mathf.CeilToInt(Mathf.Min(sampling, recommendedSampling) * 0.1f))
            {
                problems.Append("atlas padding ").Append(padding).Append(" is below 10% of the sampling size; ");
            }

            if (scale * FontCaption < 11f)
            {
                problems.Append("canvas scale ").Append(scale.ToString("0.00")).Append(" shrinks ").Append(FontCaption)
                    .Append(" px captions below 11 px (use a larger window); ");
            }

            if (problems.Length == 0)
            {
                return;
            }

            Debug.LogWarning(LogPrefix + "Font quality: " + problems + "font '" + asset.name + "' (sampling " + sampling +
                             ", padding " + padding + ", mode " + asset.atlasRenderMode + "). Recommended for MultiTravelFont: " +
                             "SDFAA render mode, sampling point size " + recommendedSampling + ", padding " + recommendedPadding +
                             ", atlas 2048x2048, Dynamic population.");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            font = null;
            fontResolved = false;
            fontChecked = false;
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
                    Debug.LogWarning(LogPrefix + "TMP font asset 'Resources/" + FontResourceName + "' not found; using the TMP default font '" +
                                     font.name + "'. Turkish characters may render through the dynamic fallback only. Run MultiTravel/Generate/Content.");
                }
                else
                {
                    Debug.LogError(LogPrefix + "No TMP font asset available ('Resources/" + FontResourceName +
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
