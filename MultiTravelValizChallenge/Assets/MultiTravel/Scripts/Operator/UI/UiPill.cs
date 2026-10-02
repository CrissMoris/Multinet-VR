using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiTravel.Operator.UI
{
    /// <summary>
    /// Status pill: tinted capsule with a coloured dot and a label. <see cref="Set"/> cross-fades the colour in 200 ms.
    /// </summary>
    public sealed class UiPill : IUiTweenTarget
    {
        private readonly Image background;
        private readonly Image dot;
        private readonly TextMeshProUGUI label;
        private Color from;
        private Color to;
        private Color current;
        private bool hasColor;

        private UiPill(RectTransform root, Image background, Image dot, TextMeshProUGUI label)
        {
            Root = root;
            this.background = background;
            this.dot = dot;
            this.label = label;
        }

        public RectTransform Root { get; }

        public TextMeshProUGUI Label => label;

        public static UiPill Create(Transform parent, string name, float height = 32f, float fontSize = OperatorUiStyle.FontLabel, bool showDot = true, bool selfSize = false)
        {
            var image = UiFactory.CreateRounded(name, parent, OperatorUiStyle.WithAlpha(OperatorUiStyle.TextMuted, 0.18f), Mathf.RoundToInt(height * 0.5f));
            var root = image.rectTransform;
            UiFactory.AddHorizontalLayout(root.gameObject, 8f, new RectOffset(showDot ? 12 : 14, 14, 0, 0), TextAnchor.MiddleCenter, true, true, false, false);
            var fitter = root.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            UiFactory.SetLayout(root, -1f, height, 0f, 0f, -1f, height);

            Image dotImage = UiFactory.CreateCircle("Dot", root, 8f, OperatorUiStyle.TextMuted);
            dotImage.gameObject.SetActive(showDot);

            var text = UiFactory.CreateLabel(root, "Text", string.Empty, fontSize, OperatorUiStyle.TextSecondary, FontStyles.Bold, TextAlignmentOptions.Center);
            UiFactory.MakeSingleLine(text);
            return new UiPill(root, image, dotImage, text);
        }

        /// <summary>Sets text and status colour; the colour change is animated unless <paramref name="animate"/> is false.</summary>
        public void Set(string text, Color color, bool animate = true)
        {
            string value = text ?? string.Empty;
            if (label.text != value)
            {
                label.text = value;
            }

            if (hasColor && to == color)
            {
                return;
            }

            if (!hasColor || !animate || !Root.gameObject.activeInHierarchy)
            {
                hasColor = true;
                from = color;
                to = color;
                UiTween.Cancel(this);
                Apply(color);
                return;
            }

            from = current;
            to = color;
            UiTween.Value(this, 0, 0f, 1f, 0.2f);
        }

        public void OnTween(int id, float value)
        {
            Apply(Color.Lerp(from, to, value));
        }

        private void Apply(Color color)
        {
            current = color;
            background.color = OperatorUiStyle.WithAlpha(color, 0.18f);
            dot.color = color;
            label.color = OperatorUiStyle.TextFor(color);
        }
    }
}
