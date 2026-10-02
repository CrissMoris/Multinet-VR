using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiTravel.Operator.UI
{
    /// <summary>Checkbox row: rounded box with a check icon and a wrapping label (the label is clickable too).</summary>
    public sealed class UiToggle
    {
        private readonly Image box;
        private readonly Image outline;

        private UiToggle(RectTransform root, Toggle toggle, Image box, Image outline, TextMeshProUGUI label)
        {
            Root = root;
            Toggle = toggle;
            this.box = box;
            this.outline = outline;
            Label = label;
        }

        /// <summary>Raised when the user changes the value.</summary>
        public event Action<bool> Changed;

        public RectTransform Root { get; }

        public Toggle Toggle { get; }

        public TextMeshProUGUI Label { get; }

        public bool IsOn => Toggle.isOn;

        public static UiToggle Create(Transform parent, string name, string text, float fontSize = OperatorUiStyle.FontBody)
        {
            var rect = UiFactory.CreateRect(name, parent);
            rect.gameObject.SetActive(false);
            UiFactory.AddHorizontalLayout(rect.gameObject, 16f, new RectOffset(0, 0, 0, 0), TextAnchor.UpperLeft, true, true, false, false);

            var holder = UiFactory.CreateRect("Box", rect);
            UiFactory.SetLayout(holder, 28f, 28f, 0f, 0f, 28f, 28f);
            var fill = UiFactory.CreateRounded("Fill", holder, OperatorUiStyle.Elevated, 8, true);
            UiFactory.Stretch(fill.rectTransform);
            var line = UiFactory.CreateImage("Outline", holder, OperatorUiStyle.Border, false);
            line.sprite = UiSprites.Outline(8);
            line.type = Image.Type.Sliced;
            UiFactory.Stretch(line.rectTransform);
            var check = UiFactory.CreateIcon(holder, "Check", UiIcon.Check, 20f, OperatorUiStyle.TextOnBright);
            UiFactory.IgnoreLayout(check);
            UiFactory.AnchorCenter(check.rectTransform, 20f, 20f);

            var label = UiFactory.CreateLabel(rect, "Label", text, fontSize, OperatorUiStyle.TextPrimary, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            label.raycastTarget = true;
            UiFactory.SetLayout(label, 0f, -1f, 1f, -1f);

            var toggle = rect.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = fill;
            toggle.graphic = check;
            toggle.toggleTransition = Toggle.ToggleTransition.None;
            toggle.transition = Selectable.Transition.None;

            var result = new UiToggle(rect, toggle, fill, line, label);
            toggle.onValueChanged.AddListener(result.OnValueChanged);
            rect.gameObject.SetActive(true);
            result.ApplyVisual(toggle.isOn, false);
            return result;
        }

        /// <summary>Sets the value; <paramref name="notify"/> raises <see cref="Changed"/>.</summary>
        public void SetIsOn(bool value, bool notify = false)
        {
            if (notify)
            {
                Toggle.isOn = value;
            }
            else
            {
                Toggle.SetIsOnWithoutNotify(value);
                ApplyVisual(value, false);
            }
        }

        private void OnValueChanged(bool value)
        {
            ApplyVisual(value, true);
            Changed?.Invoke(value);
        }

        private void ApplyVisual(bool on, bool animate)
        {
            UiTween.ColorTo(box, on ? OperatorUiStyle.Accent : OperatorUiStyle.Elevated, animate ? UiTween.Fast : 0f);
            UiTween.ColorTo(outline, on ? OperatorUiStyle.Accent : OperatorUiStyle.Border, animate ? UiTween.Fast : 0f);
        }
    }
}
