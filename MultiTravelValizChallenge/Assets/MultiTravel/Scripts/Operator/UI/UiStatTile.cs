using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiTravel.Operator.UI
{
    /// <summary>
    /// Stat tile: elevated rounded card with a caption and a large value. Integer values roll up with
    /// <see cref="UiTween.Count"/>; digits are tabular so the value does not jitter while counting.
    /// </summary>
    public sealed class UiStatTile
    {
        /// <summary>Format used for tabular integers.</summary>
        public const string TabularFormat = OperatorUiStyle.TabularOpen + "{0}" + OperatorUiStyle.TabularClose;

        private readonly string format;
        private int shown = int.MinValue;

        private UiStatTile(RectTransform root, Image fill, TextMeshProUGUI caption, TextMeshProUGUI value, string format)
        {
            Root = root;
            Fill = fill;
            Caption = caption;
            Value = value;
            this.format = format;
        }

        public RectTransform Root { get; }

        public Image Fill { get; }

        public TextMeshProUGUI Caption { get; }

        public TextMeshProUGUI Value { get; }

        public static UiStatTile Create(
            Transform parent,
            string name,
            string caption,
            float valueSize,
            Color valueColor,
            string format = TabularFormat,
            UiIcon icon = UiIcon.None,
            bool elevated = true)
        {
            var root = UiFactory.CreateSurface(parent, name, elevated ? OperatorUiStyle.Elevated : OperatorUiStyle.Card, OperatorUiStyle.RadiusCard,
                false, true, out var fill);
            UiFactory.AddVerticalLayout(root.gameObject, 4f, new RectOffset(24, 24, 20, 20), TextAnchor.MiddleCenter, true, true, true, false);

            var header = UiFactory.CreateRow(root, "Header", 8f, TextAnchor.MiddleCenter, false);
            if (icon != UiIcon.None)
            {
                UiFactory.CreateIcon(header, "Icon", icon, 20f, OperatorUiStyle.TextSecondary);
            }

            var captionLabel = UiFactory.CreateLabel(header, "Caption", caption, OperatorUiStyle.FontLabel, OperatorUiStyle.TextSecondary,
                FontStyles.Bold, TextAlignmentOptions.Center);
            UiFactory.MakeSingleLine(captionLabel);

            var value = UiFactory.CreateLabel(root, "Value", string.Empty, valueSize, valueColor, FontStyles.Bold, TextAlignmentOptions.Center);
            value.richText = true;
            UiFactory.MakeSingleLine(value);
            return new UiStatTile(root, fill, captionLabel, value, format);
        }

        /// <summary>Shows an integer; with <paramref name="animate"/> the value rolls up from the previous one.</summary>
        public void SetValue(int value, bool animate = true)
        {
            if (value == shown)
            {
                return;
            }

            if (animate && shown != int.MinValue && Root.gameObject.activeInHierarchy)
            {
                UiTween.Count(Value, format, shown, value, Mathf.Clamp(Mathf.Abs(value - shown) * 0.05f, UiTween.Slow, 0.8f));
            }
            else
            {
                UiTween.Cancel(Value);
                Value.SetText(format, value);
            }

            shown = value;
        }

        /// <summary>Rolls up from zero (first display of a result).</summary>
        public void RollUpFromZero(int value)
        {
            shown = 0;
            Value.SetText(format, 0f);
            SetValue(value, true);
        }

        /// <summary>Shows free text (rich text allowed).</summary>
        public void SetText(string text)
        {
            UiTween.Cancel(Value);
            shown = int.MinValue;
            Value.text = text ?? string.Empty;
        }

        /// <summary>Forgets the shown integer so the next <see cref="SetValue"/> does not animate.</summary>
        public void Reset()
        {
            UiTween.Cancel(Value);
            shown = int.MinValue;
        }
    }

    /// <summary>Radial progress ring (Image fillAmount) with a centred "n / N" label.</summary>
    public sealed class UiProgressRing : IUiTweenTarget
    {
        private readonly Image fill;
        private readonly TextMeshProUGUI center;
        private float shownFill;
        private int placed = -1;
        private int total = -1;

        private UiProgressRing(RectTransform root, Image fill, TextMeshProUGUI center)
        {
            Root = root;
            this.fill = fill;
            this.center = center;
        }

        public RectTransform Root { get; }

        public static UiProgressRing Create(Transform parent, string name, int diameter, Color color)
        {
            var root = UiFactory.CreateRect(name, parent);
            UiFactory.SetLayout(root, diameter, diameter, 0f, 0f, diameter, diameter);

            var track = UiFactory.CreateImage("Track", root, OperatorUiStyle.WithAlpha(OperatorUiStyle.Border, 0.45f), false);
            track.sprite = UiSprites.Ring(diameter);
            UiFactory.Stretch(track.rectTransform);

            var fillImage = UiFactory.CreateImage("Fill", root, color, false);
            fillImage.sprite = UiSprites.Ring(diameter);
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Radial360;
            fillImage.fillOrigin = (int)Image.Origin360.Top;
            fillImage.fillClockwise = true;
            fillImage.fillAmount = 0f;
            UiFactory.Stretch(fillImage.rectTransform);

            var label = UiFactory.CreateLabel(root, "Center", string.Empty, OperatorUiStyle.FontHeading, OperatorUiStyle.TextPrimary,
                FontStyles.Bold, TextAlignmentOptions.Center);
            label.richText = true;
            UiFactory.Stretch(label.rectTransform);
            return new UiProgressRing(root, fillImage, label);
        }

        /// <summary>Updates the ring and the "n / N" text (no-op when unchanged).</summary>
        public void SetProgress(int placedCount, int totalCount, bool animate = true)
        {
            if (placedCount == placed && totalCount == total)
            {
                return;
            }

            placed = placedCount;
            total = totalCount;
            center.SetText(OperatorUiStyle.TabularOpen + "{0}" + OperatorUiStyle.TabularClose + "<size=70%> / " +
                           OperatorUiStyle.TabularOpen + "{1}" + OperatorUiStyle.TabularClose + "</size>", placedCount, totalCount);
            float target = totalCount > 0 ? Mathf.Clamp01(placedCount / (float)totalCount) : 0f;
            if (animate && Root.gameObject.activeInHierarchy)
            {
                UiTween.Value(this, 0, shownFill, target, UiTween.Slow);
            }
            else
            {
                UiTween.Cancel(this);
                OnTween(0, target);
            }
        }

        /// <summary>Forces the next <see cref="SetProgress"/> to refresh.</summary>
        public void Invalidate()
        {
            placed = -1;
            total = -1;
        }

        public void OnTween(int id, float value)
        {
            shownFill = value;
            fill.fillAmount = value;
        }
    }
}
