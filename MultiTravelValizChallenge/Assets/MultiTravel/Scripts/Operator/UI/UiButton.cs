using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MultiTravel.Operator.UI
{
    /// <summary>Visual variants of <see cref="UiButton"/>.</summary>
    public enum ButtonStyle
    {
        /// <summary>Brand blue, white text: the main action of a panel.</summary>
        Primary,

        /// <summary>Teal, dark text: positive / confirming action.</summary>
        Accent,

        /// <summary>Orange, dark text: actions that need attention (force finish, resend).</summary>
        Warning,

        /// <summary>Red, white text: destructive actions.</summary>
        Danger,

        /// <summary>Elevated surface with an outline: secondary actions.</summary>
        Secondary,

        /// <summary>No fill until hovered: tertiary actions (cancel, back).</summary>
        Ghost
    }

    /// <summary>Handles of a button built by <see cref="Create"/>.</summary>
    public sealed class UiButton
    {
        private readonly float iconSize;

        private UiButton(Button button, Image background, Image outline, TextMeshProUGUI label, Image icon, float iconSize, LayoutElement layout, UiButtonFx fx)
        {
            this.iconSize = iconSize;
            Button = button;
            Background = background;
            Outline = outline;
            Label = label;
            Icon = icon;
            Layout = layout;
            Fx = fx;
        }

        public Button Button { get; }

        public Image Background { get; }

        public Image Outline { get; }

        public TextMeshProUGUI Label { get; }

        public Image Icon { get; }

        public LayoutElement Layout { get; }

        public UiButtonFx Fx { get; }

        public GameObject GameObject => Button.gameObject;

        public bool Interactable
        {
            get => Button.interactable;
            set
            {
                if (Button.interactable == value)
                {
                    return;
                }

                Button.interactable = value;
                Fx.Refresh(true);
            }
        }

        /// <summary>Creates a rounded button with hover brighten and a 0.98 press scale.</summary>
        public static UiButton Create(
            Transform parent,
            string name,
            string text,
            ButtonStyle style,
            UnityAction onClick,
            float height = OperatorUiStyle.ButtonHeight,
            float fontSize = OperatorUiStyle.FontBody,
            float preferredWidth = -1f,
            UiIcon icon = UiIcon.None)
        {
            var rect = UiFactory.CreateRect(name, parent);
            var background = rect.gameObject.AddComponent<Image>();
            UiFactory.ApplyRounded(background, OperatorUiStyle.RadiusButton);
            background.raycastTarget = true;

            var outline = UiFactory.CreateImage("Outline", rect, OperatorUiStyle.Border, false);
            outline.sprite = UiSprites.Outline(OperatorUiStyle.RadiusButton);
            outline.type = Image.Type.Sliced;
            UiFactory.IgnoreLayout(outline);
            UiFactory.Stretch(outline.rectTransform);

            UiFactory.AddHorizontalLayout(rect.gameObject, 10f, new RectOffset(20, 20, 0, 0), TextAnchor.MiddleCenter, true, true, false, false);

            float iconSize = Mathf.Clamp(Mathf.Round(fontSize * 1.2f / 4f) * 4f, 16f, 40f);
            var iconImage = UiFactory.CreateIcon(rect, "Icon", icon == UiIcon.None ? UiIcon.Check : icon, iconSize, Color.white);
            iconImage.gameObject.SetActive(icon != UiIcon.None);

            var label = UiFactory.CreateLabel(rect, "Label", text, fontSize, Color.white, FontStyles.Bold, TextAlignmentOptions.Center);
            UiFactory.MakeSingleLine(label);

            var layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
            if (preferredWidth > 0f)
            {
                layout.minWidth = preferredWidth;
                layout.preferredWidth = preferredWidth;
            }

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.None;
            var navigation = button.navigation;
            navigation.mode = Navigation.Mode.Automatic;
            button.navigation = navigation;
            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }

            var fx = rect.gameObject.AddComponent<UiButtonFx>();
            var handle = new UiButton(button, background, outline, label, iconImage, iconSize, layout, fx);
            fx.Configure(handle, style);
            return handle;
        }

        public void SetActive(bool active)
        {
            if (Button.gameObject.activeSelf != active)
            {
                Button.gameObject.SetActive(active);
            }
        }

        public void SetLabel(string text)
        {
            string value = text ?? string.Empty;
            if (Label.text != value)
            {
                Label.text = value;
            }
        }

        public void SetStyle(ButtonStyle style)
        {
            Fx.Configure(this, style);
        }

        public void SetIcon(UiIcon icon)
        {
            if (icon != UiIcon.None)
            {
                UiFactory.SetIcon(Icon, icon, iconSize);
            }

            Icon.gameObject.SetActive(icon != UiIcon.None);
        }

        /// <summary>Tooltip shown on hover (also while the button is disabled). Empty text removes it.</summary>
        public void SetTooltip(string text)
        {
            var trigger = Button.GetComponent<UiTooltipTrigger>();
            if (trigger == null)
            {
                if (string.IsNullOrEmpty(text))
                {
                    return;
                }

                trigger = Button.gameObject.AddComponent<UiTooltipTrigger>();
            }

            trigger.Text = text;
        }
    }

    /// <summary>
    /// Drives the colours and the press scale of a <see cref="UiButton"/>: hover brightens, pressed darkens and scales to 0.98
    /// in 90 ms, disabled dims. Colour changes go through <see cref="UiTween"/> (no allocations).
    /// </summary>
    public sealed class UiButtonFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        private UiButton button;
        private ButtonStyle style;
        private Color normal;
        private Color hover;
        private Color pressed;
        private Color disabled;
        private Color textNormal;
        private Color textDisabled;
        private Color outlineNormal;
        private Color outlineHover;
        private bool isHover;
        private bool isPressed;
        private bool configured;

        internal void Configure(UiButton owner, ButtonStyle buttonStyle)
        {
            button = owner;
            style = buttonStyle;
            configured = true;

            var elevated = OperatorUiStyle.Elevated;
            textDisabled = OperatorUiStyle.WithAlpha(OperatorUiStyle.TextMuted, 0.7f);
            outlineNormal = OperatorUiStyle.Transparent;
            outlineHover = OperatorUiStyle.Transparent;
            disabled = OperatorUiStyle.WithAlpha(elevated, 0.55f);

            switch (buttonStyle)
            {
                case ButtonStyle.Accent:
                    SetFill(OperatorUiStyle.Accent, OperatorUiStyle.TextOnBright);
                    break;
                case ButtonStyle.Warning:
                    SetFill(OperatorUiStyle.Warning, OperatorUiStyle.TextOnBright);
                    break;
                case ButtonStyle.Danger:
                    SetFill(OperatorUiStyle.Danger, Color.white);
                    break;
                case ButtonStyle.Secondary:
                    normal = elevated;
                    hover = OperatorUiStyle.Lighten(elevated, 0.1f);
                    pressed = OperatorUiStyle.Darken(elevated, 0.15f);
                    textNormal = OperatorUiStyle.TextPrimary;
                    outlineNormal = OperatorUiStyle.Border;
                    outlineHover = OperatorUiStyle.PrimaryBright;
                    break;
                case ButtonStyle.Ghost:
                    normal = OperatorUiStyle.WithAlpha(elevated, 0f);
                    hover = OperatorUiStyle.WithAlpha(elevated, 0.8f);
                    pressed = elevated;
                    textNormal = OperatorUiStyle.TextSecondary;
                    disabled = normal;
                    break;
                default:
                    normal = OperatorUiStyle.Primary;
                    hover = Color.Lerp(OperatorUiStyle.Primary, OperatorUiStyle.PrimaryBright, 0.55f);
                    pressed = OperatorUiStyle.Darken(OperatorUiStyle.Primary, 0.15f);
                    textNormal = Color.white;
                    break;
            }

            Refresh(false);
        }

        /// <summary>Re-applies colours and scale for the current pointer / interactable state.</summary>
        public void Refresh(bool animate)
        {
            if (!configured)
            {
                return;
            }

            bool interactable = button.Button.interactable;
            Color fill = !interactable ? disabled : isPressed ? pressed : isHover ? hover : normal;
            Color text = !interactable ? textDisabled : (style == ButtonStyle.Ghost && (isHover || isPressed) ? OperatorUiStyle.TextPrimary : textNormal);
            Color line = !interactable ? OperatorUiStyle.WithAlpha(outlineNormal, outlineNormal.a * 0.5f) : (isHover || isPressed ? outlineHover : outlineNormal);
            float scale = interactable && isPressed ? 0.98f : 1f;

            float duration = animate ? 0.12f : 0f;
            UiTween.ColorTo(button.Background, fill, duration);
            UiTween.ColorTo(button.Outline, line, duration);
            button.Label.color = text;
            if (button.Icon != null)
            {
                button.Icon.color = text;
            }

            UiTween.ScaleTo(transform, scale, animate ? 0.09f : 0f);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            isHover = true;
            Refresh(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            isHover = false;
            isPressed = false;
            Refresh(true);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            isPressed = true;
            Refresh(true);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            isPressed = false;
            Refresh(true);
        }

        private void OnDisable()
        {
            if (!isHover && !isPressed)
            {
                return;
            }

            isHover = false;
            isPressed = false;
            Refresh(false);
        }

        private void SetFill(Color color, Color textColor)
        {
            normal = color;
            hover = OperatorUiStyle.Lighten(color, 0.12f);
            pressed = OperatorUiStyle.Darken(color, 0.14f);
            textNormal = textColor;
        }
    }
}
