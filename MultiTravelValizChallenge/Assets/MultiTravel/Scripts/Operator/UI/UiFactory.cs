using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MultiTravel.Operator.UI
{
    /// <summary>Visual variants of <see cref="UiFactory.CreateButton"/>.</summary>
    public enum ButtonStyle
    {
        /// <summary>Deep blue, white text: the main action of a panel.</summary>
        Primary,

        /// <summary>Teal, white text: positive / confirming action.</summary>
        Accent,

        /// <summary>Orange, white text: actions that need attention (force finish, retry).</summary>
        Warning,

        /// <summary>Red, white text: destructive actions (cancel session).</summary>
        Danger,

        /// <summary>Light grey, blue text: secondary actions.</summary>
        Secondary
    }

    /// <summary>Handles of a button built by <see cref="UiFactory.CreateButton"/>.</summary>
    public sealed class UiButton
    {
        public UiButton(Button button, Image background, TextMeshProUGUI label, LayoutElement layout)
        {
            Button = button;
            Background = background;
            Label = label;
            Layout = layout;
        }

        public Button Button { get; }

        public Image Background { get; }

        public TextMeshProUGUI Label { get; }

        public LayoutElement Layout { get; }

        public GameObject GameObject => Button.gameObject;

        public bool Interactable
        {
            get => Button.interactable;
            set => Button.interactable = value;
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
            Label.text = text ?? string.Empty;
        }

        public void SetStyle(ButtonStyle style)
        {
            UiFactory.ApplyButtonStyle(this, style);
        }
    }

    /// <summary>
    /// Builds uGUI / TextMeshPro controls from code (no prefabs, no sprites). All controls use flat
    /// <see cref="Image"/> graphics, the <see cref="OperatorUiStyle"/> palette and the project font.
    /// Construction allocates; nothing here is meant to be called per frame.
    /// </summary>
    public static class UiFactory
    {
        private const int UiLayer = 5;

        // ----- primitives -----

        /// <summary>Creates a child GameObject with a RectTransform on the parent's layer.</summary>
        public static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent != null ? parent.gameObject.layer : UiLayer;
            var rect = (RectTransform)go.transform;
            if (parent != null)
            {
                rect.SetParent(parent, false);
            }

            rect.localScale = Vector3.one;
            return rect;
        }

        /// <summary>Stretches a rect over its parent with the given insets (pixels at the reference resolution).</summary>
        public static void Stretch(RectTransform rect, float left = 0f, float top = 0f, float right = 0f, float bottom = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>Anchors a rect to the top edge of its parent with a fixed height.</summary>
        public static void AnchorTop(RectTransform rect, float height, float topOffset = 0f)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(0f, -topOffset - height);
            rect.offsetMax = new Vector2(0f, -topOffset);
        }

        /// <summary>Anchors a rect to the bottom edge of its parent with a fixed height.</summary>
        public static void AnchorBottom(RectTransform rect, float height)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = new Vector2(0f, height);
        }

        /// <summary>Centres a rect in its parent with a fixed width (height driven by a ContentSizeFitter or set later).</summary>
        public static void AnchorCenter(RectTransform rect, float width, float height)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(width, height);
        }

        /// <summary>Creates a flat image.</summary>
        public static Image CreateImage(string name, Transform parent, Color color, bool raycastTarget = false)
        {
            var rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycastTarget;
            return image;
        }

        /// <summary>Creates a full-screen Screen Space Overlay canvas (1920x1080 reference, match 0.5) with a GraphicRaycaster.</summary>
        public static Canvas CreateOverlayCanvas(string name, Transform parent, int sortingOrder)
        {
            var rect = CreateRect(name, parent);
            rect.gameObject.layer = UiLayer;
            var canvas = rect.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            canvas.pixelPerfect = false;
            canvas.targetDisplay = 0;

            var scaler = rect.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referencePixelsPerUnit = 100f;

            rect.gameObject.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        // ----- text -----

        /// <summary>Creates a TextMeshPro UGUI label using the project font. Rich text is off (labels may show participant input).</summary>
        public static TextMeshProUGUI CreateLabel(
            Transform parent,
            string name,
            string text,
            float fontSize,
            Color color,
            FontStyles style = FontStyles.Normal,
            TextAlignmentOptions alignment = TextAlignmentOptions.Left)
        {
            var rect = CreateRect(name, parent);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            var font = OperatorUiStyle.Font;
            if (font != null)
            {
                label.font = font;
            }

            label.richText = false;
            label.fontSize = fontSize;
            label.color = color;
            label.fontStyle = style;
            label.alignment = alignment;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
            label.text = text ?? string.Empty;
            return label;
        }

        /// <summary>Makes a label single-line with an ellipsis when it does not fit.</summary>
        public static void MakeSingleLine(TextMeshProUGUI label)
        {
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }

        // ----- buttons -----

        /// <summary>Creates a flat button with colour-tint transitions and a centred bold label.</summary>
        public static UiButton CreateButton(
            Transform parent,
            string name,
            string text,
            ButtonStyle style,
            UnityAction onClick,
            float preferredHeight = OperatorUiStyle.ButtonHeight,
            float fontSize = OperatorUiStyle.FontButton,
            float preferredWidth = -1f)
        {
            var rect = CreateRect(name, parent);
            var background = rect.gameObject.AddComponent<Image>();
            background.color = Color.white;
            background.raycastTarget = true;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            var navigation = button.navigation;
            navigation.mode = Navigation.Mode.Automatic;
            button.navigation = navigation;

            var label = CreateLabel(rect, "Label", text, fontSize, OperatorUiStyle.TextOnDark, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(label.rectTransform, 14f, 4f, 14f, 4f);
            label.enableAutoSizing = true;
            label.fontSizeMax = fontSize;
            label.fontSizeMin = Mathf.Min(fontSize, OperatorUiStyle.FontTiny);
            label.overflowMode = TextOverflowModes.Ellipsis;

            var layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = preferredHeight;
            layout.preferredHeight = preferredHeight;
            if (preferredWidth > 0f)
            {
                layout.minWidth = preferredWidth;
                layout.preferredWidth = preferredWidth;
            }

            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }

            var handle = new UiButton(button, background, label, layout);
            ApplyButtonStyle(handle, style);
            return handle;
        }

        /// <summary>Applies the palette of a <see cref="ButtonStyle"/> (ColorTint transition on a white image).</summary>
        public static void ApplyButtonStyle(UiButton button, ButtonStyle style)
        {
            Color baseColor;
            Color textColor = OperatorUiStyle.TextOnDark;
            switch (style)
            {
                case ButtonStyle.Accent:
                    baseColor = OperatorUiStyle.Accent;
                    break;
                case ButtonStyle.Warning:
                    baseColor = OperatorUiStyle.Warning;
                    break;
                case ButtonStyle.Danger:
                    baseColor = OperatorUiStyle.Danger;
                    break;
                case ButtonStyle.Secondary:
                    baseColor = OperatorUiStyle.Neutral;
                    textColor = OperatorUiStyle.Primary;
                    break;
                default:
                    baseColor = OperatorUiStyle.Primary;
                    break;
            }

            button.Button.transition = Selectable.Transition.ColorTint;
            button.Button.colors = BuildColorBlock(baseColor);
            button.Label.color = textColor;
        }

        /// <summary>Colour block with hover / pressed / selected / disabled shades of <paramref name="baseColor"/>.</summary>
        public static ColorBlock BuildColorBlock(Color baseColor)
        {
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = baseColor;
            colors.highlightedColor = OperatorUiStyle.Lighten(baseColor, 0.15f);
            colors.pressedColor = OperatorUiStyle.Darken(baseColor, 0.2f);
            colors.selectedColor = OperatorUiStyle.Lighten(baseColor, 0.08f);
            colors.disabledColor = OperatorUiStyle.Disabled;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            return colors;
        }

        // ----- input -----

        /// <summary>
        /// Builds a single-line TMP_InputField from scratch: border image, tinted background, "Text Area" viewport with a
        /// RectMask2D, placeholder and text children; caret / selection colours configured. The hierarchy is assembled while
        /// inactive so the input field initialises with every reference already assigned.
        /// </summary>
        public static TMP_InputField CreateInputField(
            Transform parent,
            string name,
            string placeholderText,
            TMP_InputField.ContentType contentType,
            int characterLimit,
            float height = OperatorUiStyle.InputHeight,
            float fontSize = OperatorUiStyle.FontBody)
        {
            var rect = CreateRect(name, parent);
            rect.gameObject.SetActive(false);

            var border = rect.gameObject.AddComponent<Image>();
            border.color = OperatorUiStyle.Border;
            border.raycastTarget = true;

            var layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;

            var background = CreateImage("Background", rect, Color.white, true);
            Stretch(background.rectTransform, 2f, 2f, 2f, 2f);

            var textArea = CreateRect("Text Area", rect);
            Stretch(textArea, 16f, 6f, 16f, 6f);
            var mask = textArea.gameObject.AddComponent<RectMask2D>();
            mask.padding = new Vector4(-8f, -5f, -8f, -5f);

            var placeholder = CreateLabel(textArea, "Placeholder", placeholderText, fontSize, OperatorUiStyle.TextMuted, FontStyles.Italic, TextAlignmentOptions.Left);
            Stretch(placeholder.rectTransform);
            placeholder.textWrappingMode = TextWrappingModes.NoWrap;
            placeholder.extraPadding = true;
            placeholder.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            var text = CreateLabel(textArea, "Text", string.Empty, fontSize, OperatorUiStyle.TextPrimary, FontStyles.Normal, TextAlignmentOptions.Left);
            Stretch(text.rectTransform);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.extraPadding = true;

            var field = rect.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = textArea;
            field.textComponent = text;
            field.placeholder = placeholder;
            if (text.font != null)
            {
                field.fontAsset = text.font;
            }

            field.pointSize = fontSize;
            field.targetGraphic = background;
            field.transition = Selectable.Transition.ColorTint;
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = Color.white;
            colors.highlightedColor = OperatorUiStyle.Lighten(OperatorUiStyle.Primary, 0.94f);
            colors.pressedColor = OperatorUiStyle.Lighten(OperatorUiStyle.Primary, 0.88f);
            colors.selectedColor = OperatorUiStyle.Lighten(OperatorUiStyle.Accent, 0.9f);
            colors.disabledColor = OperatorUiStyle.Neutral;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            field.colors = colors;

            field.contentType = contentType;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = characterLimit;
            field.richText = false;
            field.caretWidth = 2;
            field.caretBlinkRate = 0.85f;
            field.customCaretColor = true;
            field.caretColor = OperatorUiStyle.Primary;
            field.selectionColor = OperatorUiStyle.WithAlpha(OperatorUiStyle.Accent, 0.35f);
            field.onFocusSelectAll = false;
            field.resetOnDeActivation = true;
            field.SetTextWithoutNotify(string.Empty);

            rect.gameObject.SetActive(true);
            return field;
        }

        // ----- toggle -----

        /// <summary>Builds a toggle row: bordered box with a teal checkmark and a wrapping label (the label is clickable too).</summary>
        public static Toggle CreateToggle(Transform parent, string name, string labelText, float fontSize, out TextMeshProUGUI label)
        {
            var rect = CreateRect(name, parent);
            rect.gameObject.SetActive(false);
            AddHorizontalLayout(rect.gameObject, 16f, new RectOffset(0, 0, 0, 0), TextAnchor.UpperLeft, true, true, false, false);

            var box = CreateImage("Background", rect, OperatorUiStyle.Primary, true);
            SetLayout(box, 40f, 40f, 0f, 0f, 40f, 40f);

            var boxInner = CreateImage("Fill", box.rectTransform, Color.white, true);
            Stretch(boxInner.rectTransform, 3f, 3f, 3f, 3f);

            var checkmark = CreateImage("Checkmark", box.rectTransform, OperatorUiStyle.Accent, false);
            Stretch(checkmark.rectTransform, 8f, 8f, 8f, 8f);

            label = CreateLabel(rect, "Label", labelText, fontSize, OperatorUiStyle.TextPrimary, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            label.raycastTarget = true;
            SetLayout(label, -1f, -1f, 1f, -1f);

            var toggle = rect.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = boxInner;
            toggle.graphic = checkmark;
            toggle.toggleTransition = Toggle.ToggleTransition.None;
            toggle.transition = Selectable.Transition.ColorTint;
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = Color.white;
            colors.highlightedColor = OperatorUiStyle.Lighten(OperatorUiStyle.Accent, 0.85f);
            colors.pressedColor = OperatorUiStyle.Lighten(OperatorUiStyle.Accent, 0.7f);
            colors.selectedColor = OperatorUiStyle.Lighten(OperatorUiStyle.Accent, 0.9f);
            colors.disabledColor = OperatorUiStyle.Neutral;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            toggle.colors = colors;

            // Toggle.OnEnable / Set call PlayEffect, which (with ToggleTransition.None) sets the checkmark alpha instantly.
            rect.gameObject.SetActive(true);
            return toggle;
        }

        // ----- containers and layout -----

        /// <summary>
        /// White card centred in the parent with a vertical layout and a ContentSizeFitter (height follows content).
        /// </summary>
        public static RectTransform CreateCard(Transform parent, string name, float width, int padding = 40, float spacing = 18f)
        {
            var card = CreateImage(name, parent, OperatorUiStyle.Card, true);
            AnchorCenter(card.rectTransform, width, 200f);
            var shadow = card.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.12f);
            shadow.effectDistance = new Vector2(0f, -4f);

            AddVerticalLayout(card.gameObject, spacing, new RectOffset(padding, padding, padding, padding), TextAnchor.UpperCenter, true, true, true, false);
            var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return card.rectTransform;
        }

        /// <summary>Horizontal row container (no graphic) inside a layout group.</summary>
        public static RectTransform CreateRow(Transform parent, string name, float spacing, TextAnchor alignment = TextAnchor.MiddleCenter, bool expandWidth = true)
        {
            var rect = CreateRect(name, parent);
            AddHorizontalLayout(rect.gameObject, spacing, new RectOffset(0, 0, 0, 0), alignment, true, true, expandWidth, false);
            return rect;
        }

        /// <summary>Vertical column container (no graphic) inside a layout group.</summary>
        public static RectTransform CreateColumn(Transform parent, string name, float spacing, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            var rect = CreateRect(name, parent);
            AddVerticalLayout(rect.gameObject, spacing, new RectOffset(0, 0, 0, 0), alignment, true, true, true, false);
            return rect;
        }

        /// <summary>Fixed-size empty element for spacing inside layout groups.</summary>
        public static RectTransform CreateSpacer(Transform parent, float width, float height)
        {
            var rect = CreateRect("Spacer", parent);
            SetLayout(rect, width, height, 0f, 0f, width, height);
            return rect;
        }

        /// <summary>Square status dot.</summary>
        public static Image CreateDot(Transform parent, string name, float size, Color color)
        {
            var dot = CreateImage(name, parent, color, false);
            SetLayout(dot, size, size, 0f, 0f, size, size);
            return dot;
        }

        public static VerticalLayoutGroup AddVerticalLayout(
            GameObject target,
            float spacing,
            RectOffset padding,
            TextAnchor alignment = TextAnchor.UpperLeft,
            bool controlWidth = true,
            bool controlHeight = true,
            bool expandWidth = true,
            bool expandHeight = false)
        {
            var group = target.AddComponent<VerticalLayoutGroup>();
            group.spacing = spacing;
            group.padding = padding ?? new RectOffset(0, 0, 0, 0);
            group.childAlignment = alignment;
            group.childControlWidth = controlWidth;
            group.childControlHeight = controlHeight;
            group.childForceExpandWidth = expandWidth;
            group.childForceExpandHeight = expandHeight;
            group.childScaleWidth = false;
            group.childScaleHeight = false;
            return group;
        }

        public static HorizontalLayoutGroup AddHorizontalLayout(
            GameObject target,
            float spacing,
            RectOffset padding,
            TextAnchor alignment = TextAnchor.MiddleLeft,
            bool controlWidth = true,
            bool controlHeight = true,
            bool expandWidth = false,
            bool expandHeight = false)
        {
            var group = target.AddComponent<HorizontalLayoutGroup>();
            group.spacing = spacing;
            group.padding = padding ?? new RectOffset(0, 0, 0, 0);
            group.childAlignment = alignment;
            group.childControlWidth = controlWidth;
            group.childControlHeight = controlHeight;
            group.childForceExpandWidth = expandWidth;
            group.childForceExpandHeight = expandHeight;
            group.childScaleWidth = false;
            group.childScaleHeight = false;
            return group;
        }

        /// <summary>
        /// Adds (or updates) a LayoutElement. Negative values leave the corresponding property unset (-1 = not used by uGUI).
        /// </summary>
        public static LayoutElement SetLayout(
            Component target,
            float preferredWidth = -1f,
            float preferredHeight = -1f,
            float flexibleWidth = -1f,
            float flexibleHeight = -1f,
            float minWidth = -1f,
            float minHeight = -1f)
        {
            var element = target.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = target.gameObject.AddComponent<LayoutElement>();
            }

            element.preferredWidth = preferredWidth;
            element.preferredHeight = preferredHeight;
            element.flexibleWidth = flexibleWidth;
            element.flexibleHeight = flexibleHeight;
            element.minWidth = minWidth;
            element.minHeight = minHeight;
            return element;
        }

        /// <summary>Activates / deactivates a GameObject only when the state changes.</summary>
        public static void SetActive(Component target, bool active)
        {
            if (target != null && target.gameObject.activeSelf != active)
            {
                target.gameObject.SetActive(active);
            }
        }
    }
}
