using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiTravel.Operator.UI
{
    /// <summary>Validation indicator of a <see cref="UiField"/>.</summary>
    public enum FieldState
    {
        None,
        Valid,
        Error
    }

    /// <summary>
    /// Text input with a floating label (rests in the middle, floats up and shrinks while focused or filled), an animated
    /// outline, an inline error line below and a validation icon (check / warning) on the right.
    /// </summary>
    public sealed class UiField : IUiTweenTarget
    {
        private const float LabelRestOffset = 0f;
        private const float LabelFloatOffset = 17f;
        private const float LabelFloatScale = 0.7f;
        private const float ErrorRowHeight = 22f;

        private readonly Image outline;
        private readonly TextMeshProUGUI label;
        private readonly TextMeshProUGUI placeholder;
        private readonly Image icon;
        private readonly TextMeshProUGUI errorLabel;
        private readonly Image errorIcon;
        private float floatAmount;
        private bool focused;
        private FieldState state;

        private UiField(RectTransform root, TMP_InputField input, Image outline, TextMeshProUGUI label, TextMeshProUGUI placeholder,
            Image icon, TextMeshProUGUI errorLabel, Image errorIcon)
        {
            Root = root;
            Input = input;
            this.outline = outline;
            this.label = label;
            this.placeholder = placeholder;
            this.icon = icon;
            this.errorLabel = errorLabel;
            this.errorIcon = errorIcon;
        }

        /// <summary>Raised when the text changed through user input.</summary>
        public event Action<UiField> Changed;

        /// <summary>Raised when Enter was pressed inside the field.</summary>
        public event Action<UiField> Submitted;

        /// <summary>Raised when the field lost focus.</summary>
        public event Action<UiField> Blurred;

        public RectTransform Root { get; }

        public TMP_InputField Input { get; }

        public string Text => Input.text;

        public bool IsFocused => focused;

        public FieldState State => state;

        public static UiField Create(
            Transform parent,
            string name,
            string labelText,
            string hint,
            TMP_InputField.ContentType contentType,
            int characterLimit)
        {
            var group = UiFactory.CreateRect(name, parent);
            UiFactory.AddVerticalLayout(group.gameObject, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.UpperLeft, true, true, true, false);
            UiFactory.SetLayout(group, 0f, -1f, 1f, 0f);

            var box = UiFactory.CreateRect("Box", group);
            box.gameObject.SetActive(false);
            UiFactory.SetLayout(box, -1f, OperatorUiStyle.FieldHeight, 1f, 0f, -1f, OperatorUiStyle.FieldHeight);

            var fill = box.gameObject.AddComponent<Image>();
            UiFactory.ApplyRounded(fill, OperatorUiStyle.RadiusField);
            fill.color = OperatorUiStyle.Elevated;
            fill.raycastTarget = true;

            var outlineImage = UiFactory.CreateImage("Outline", box, OperatorUiStyle.WithAlpha(OperatorUiStyle.Border, 0.9f), false);
            outlineImage.sprite = UiSprites.Outline(OperatorUiStyle.RadiusField);
            outlineImage.type = Image.Type.Sliced;
            UiFactory.Stretch(outlineImage.rectTransform);

            var textArea = UiFactory.CreateRect("Text Area", box);
            UiFactory.Stretch(textArea, 16f, 26f, 52f, 8f);
            var mask = textArea.gameObject.AddComponent<RectMask2D>();
            mask.padding = new Vector4(-4f, -4f, -4f, -4f);

            var placeholderLabel = UiFactory.CreateLabel(textArea, "Placeholder", hint ?? string.Empty, OperatorUiStyle.FontBody,
                OperatorUiStyle.WithAlpha(OperatorUiStyle.TextMuted, 0f), FontStyles.Normal, TextAlignmentOptions.Left);
            UiFactory.Stretch(placeholderLabel.rectTransform);
            placeholderLabel.textWrappingMode = TextWrappingModes.NoWrap;
            placeholderLabel.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            var text = UiFactory.CreateLabel(textArea, "Text", string.Empty, OperatorUiStyle.FontBody, OperatorUiStyle.TextPrimary,
                FontStyles.Normal, TextAlignmentOptions.Left);
            UiFactory.Stretch(text.rectTransform);
            text.textWrappingMode = TextWrappingModes.NoWrap;

            var floating = UiFactory.CreateLabel(box, "Label", labelText, OperatorUiStyle.FontBody, OperatorUiStyle.TextMuted,
                FontStyles.Normal, TextAlignmentOptions.Left);
            UiFactory.MakeSingleLine(floating);
            var floatingRect = floating.rectTransform;
            floatingRect.anchorMin = new Vector2(0f, 0.5f);
            floatingRect.anchorMax = new Vector2(1f, 0.5f);
            floatingRect.pivot = new Vector2(0f, 0.5f);

            var stateIcon = UiFactory.CreateIcon(box, "StateIcon", UiIcon.Check, 24f, OperatorUiStyle.Success);
            UiFactory.IgnoreLayout(stateIcon);
            var iconRect = stateIcon.rectTransform;
            iconRect.anchorMin = new Vector2(1f, 0.5f);
            iconRect.anchorMax = new Vector2(1f, 0.5f);
            iconRect.pivot = new Vector2(1f, 0.5f);
            iconRect.sizeDelta = new Vector2(24f, 24f);
            iconRect.anchoredPosition = new Vector2(-16f, 0f);
            stateIcon.enabled = false;

            var input = box.gameObject.AddComponent<TMP_InputField>();
            input.textViewport = textArea;
            input.textComponent = text;
            input.placeholder = placeholderLabel;
            if (text.font != null)
            {
                input.fontAsset = text.font;
            }

            input.pointSize = OperatorUiStyle.FontBody;
            input.targetGraphic = fill;
            input.transition = Selectable.Transition.None;
            input.contentType = contentType;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = characterLimit;
            input.richText = false;
            input.caretWidth = 2;
            input.caretBlinkRate = 0.85f;
            input.customCaretColor = true;
            input.caretColor = OperatorUiStyle.PrimaryBright;
            input.selectionColor = OperatorUiStyle.WithAlpha(OperatorUiStyle.Accent, 0.35f);
            input.onFocusSelectAll = false;
            input.resetOnDeActivation = true;
            input.SetTextWithoutNotify(string.Empty);
            box.gameObject.SetActive(true);

            var errorRow = UiFactory.CreateRow(group, "ErrorRow", 6f, TextAnchor.MiddleLeft, false);
            UiFactory.SetLayout(errorRow, -1f, ErrorRowHeight, 1f, 0f, -1f, ErrorRowHeight);
            var warning = UiFactory.CreateIcon(errorRow, "ErrorIcon", UiIcon.Warning, 16f, OperatorUiStyle.DangerText);
            warning.gameObject.SetActive(false);
            var error = UiFactory.CreateLabel(errorRow, "Error", string.Empty, OperatorUiStyle.FontCaption, OperatorUiStyle.DangerText);
            UiFactory.MakeSingleLine(error);
            UiFactory.SetLayout(error, 0f, -1f, 1f, -1f);

            var field = new UiField(group, input, outlineImage, floating, placeholderLabel, stateIcon, error, warning);
            field.ApplyFloat(0f);
            input.onSelect.AddListener(field.OnSelected);
            input.onDeselect.AddListener(field.OnDeselected);
            input.onValueChanged.AddListener(field.OnValueChanged);
            input.onSubmit.AddListener(field.OnSubmitted);
            return field;
        }

        /// <summary>Sets the text without raising <see cref="Changed"/>.</summary>
        public void SetText(string value)
        {
            Input.SetTextWithoutNotify(value ?? string.Empty);
            SyncFloat(false);
        }

        /// <summary>Clears text, error and state (new participant cycle).</summary>
        public void Clear()
        {
            Input.SetTextWithoutNotify(string.Empty);
            SetState(FieldState.None, null);
            SyncFloat(false);
        }

        /// <summary>Shows a validation state: error text + warning icon, a check, or nothing.</summary>
        public void SetState(FieldState newState, string message)
        {
            state = newState;
            bool hasError = newState == FieldState.Error && !string.IsNullOrEmpty(message);
            if (hasError)
            {
                errorLabel.text = message;
            }
            else if (errorLabel.text.Length > 0)
            {
                errorLabel.text = string.Empty;
            }

            UiFactory.SetActive(errorIcon, hasError);
            if (hasError)
            {
                UiFactory.SetIcon(icon, UiIcon.Warning, 24f);
                icon.color = OperatorUiStyle.DangerText;
                icon.enabled = true;
            }
            else if (newState == FieldState.Valid)
            {
                UiFactory.SetIcon(icon, UiIcon.Check, 24f);
                icon.color = OperatorUiStyle.Success;
                icon.enabled = true;
            }
            else
            {
                icon.enabled = false;
            }

            RefreshColors();
        }

        /// <summary>Moves keyboard focus into the field.</summary>
        public void Focus()
        {
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            if (eventSystem != null)
            {
                eventSystem.SetSelectedGameObject(Input.gameObject);
            }

            Input.ActivateInputField();
        }

        public void OnTween(int id, float value)
        {
            ApplyFloat(value);
        }

        private void OnSelected(string _)
        {
            focused = true;
            SyncFloat(true);
            RefreshColors();
        }

        private void OnDeselected(string _)
        {
            focused = false;
            SyncFloat(true);
            RefreshColors();
            Blurred?.Invoke(this);
        }

        private void OnValueChanged(string _)
        {
            SyncFloat(true);
            Changed?.Invoke(this);
        }

        private void OnSubmitted(string _)
        {
            Submitted?.Invoke(this);
        }

        private void SyncFloat(bool animate)
        {
            float target = focused || Input.text.Length > 0 ? 1f : 0f;
            if (Mathf.Approximately(target, floatAmount))
            {
                return;
            }

            if (animate && Root.gameObject.activeInHierarchy)
            {
                UiTween.Value(this, 0, floatAmount, target, UiTween.Fast);
            }
            else
            {
                UiTween.Cancel(this);
                ApplyFloat(target);
            }
        }

        private void ApplyFloat(float amount)
        {
            floatAmount = amount;
            float y = Mathf.Lerp(LabelRestOffset, LabelFloatOffset, amount);
            var rect = label.rectTransform;
            rect.offsetMin = new Vector2(16f, -14f + y);
            rect.offsetMax = new Vector2(-52f, 14f + y);
            float scale = Mathf.Lerp(1f, LabelFloatScale, amount);
            rect.localScale = new Vector3(scale, scale, 1f);
        }

        private void RefreshColors()
        {
            Color line;
            Color text;
            if (state == FieldState.Error)
            {
                line = OperatorUiStyle.Danger;
                text = OperatorUiStyle.DangerText;
            }
            else if (focused)
            {
                line = OperatorUiStyle.PrimaryBright;
                text = OperatorUiStyle.TextFor(OperatorUiStyle.PrimaryBright);
            }
            else
            {
                line = OperatorUiStyle.WithAlpha(OperatorUiStyle.Border, 0.9f);
                text = OperatorUiStyle.TextMuted;
            }

            UiTween.ColorTo(outline, line, UiTween.Fast);
            UiTween.ColorTo(label, text, UiTween.Fast);
            UiTween.ColorTo(placeholder, OperatorUiStyle.WithAlpha(OperatorUiStyle.TextMuted, focused ? 0.7f : 0f), UiTween.Fast);
        }
    }
}
