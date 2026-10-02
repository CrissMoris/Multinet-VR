using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MultiTravel.Operator.UI
{
    /// <summary>Colour / icon family of a <see cref="UiToast"/>.</summary>
    public enum ToastKind
    {
        Info,
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// Single toast at the bottom centre of the canvas. <see cref="Show"/> replaces the current message; it slides in and fades
    /// out after a few seconds (timing driven by <see cref="Tick"/>, no coroutines, no allocations once built).
    /// </summary>
    public sealed class UiToast
    {
        private readonly CanvasGroup group;
        private readonly RectTransform rect;
        private readonly Image icon;
        private readonly Image bar;
        private readonly TextMeshProUGUI label;
        private float hideAt = -1f;

        private UiToast(CanvasGroup group, RectTransform rect, Image icon, Image bar, TextMeshProUGUI label)
        {
            this.group = group;
            this.rect = rect;
            this.icon = icon;
            this.bar = bar;
            this.label = label;
        }

        public static UiToast Create(RectTransform canvasRoot)
        {
            var root = UiFactory.CreateSurface(canvasRoot, "Toast", OperatorUiStyle.Elevated, OperatorUiStyle.RadiusButton, true, true, out _);
            root.anchorMin = new Vector2(0.5f, 0f);
            root.anchorMax = new Vector2(0.5f, 0f);
            root.pivot = new Vector2(0.5f, 0f);
            root.anchoredPosition = new Vector2(0f, 40f);
            UiFactory.AddHorizontalLayout(root.gameObject, 12f, new RectOffset(20, 24, 14, 14), TextAnchor.MiddleLeft, true, true, false, false);
            var fitter = root.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            var bar = UiFactory.CreateRounded("Bar", root, OperatorUiStyle.Accent, 3, false);
            UiFactory.IgnoreLayout(bar);
            var barRect = bar.rectTransform;
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(0f, 1f);
            barRect.pivot = new Vector2(0f, 0.5f);
            barRect.offsetMin = new Vector2(8f, 12f);
            barRect.offsetMax = new Vector2(11f, -12f);

            var icon = UiFactory.CreateIcon(root, "Icon", UiIcon.Check, 24f, OperatorUiStyle.Accent);
            var label = UiFactory.CreateLabel(root, "Text", string.Empty, OperatorUiStyle.FontBody, OperatorUiStyle.TextPrimary);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            return new UiToast(group, root, icon, bar, label);
        }

        /// <summary>Shows a message for <paramref name="seconds"/>.</summary>
        public void Show(string text, ToastKind kind = ToastKind.Info, float seconds = 3f)
        {
            Color color;
            UiIcon symbol;
            switch (kind)
            {
                case ToastKind.Success:
                    color = OperatorUiStyle.Success;
                    symbol = UiIcon.Check;
                    break;
                case ToastKind.Warning:
                    color = OperatorUiStyle.Warning;
                    symbol = UiIcon.Warning;
                    break;
                case ToastKind.Error:
                    color = OperatorUiStyle.DangerText;
                    symbol = UiIcon.Warning;
                    break;
                default:
                    color = OperatorUiStyle.PrimaryBright;
                    symbol = UiIcon.Recenter;
                    break;
            }

            label.text = text ?? string.Empty;
            icon.color = color;
            bar.color = color;
            UiFactory.SetIcon(icon, symbol, 24f);
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            UiTween.Cancel(group);
            rect.anchoredPosition = new Vector2(0f, 40f);
            UiTween.PanelIn(group, rect, 16f, UiTween.Normal);
            hideAt = Time.unscaledTime + Mathf.Max(1f, seconds);
        }

        /// <summary>Hides the toast when its time is up. Call once per frame.</summary>
        public void Tick(float now)
        {
            if (hideAt > 0f && now >= hideAt)
            {
                hideAt = -1f;
                UiTween.Fade(group, 0f, UiTween.Slow);
            }
        }
    }

    /// <summary>
    /// Centred confirmation dialog over a dimmed screen ("Emin misiniz?"). The confirm action runs only when the operator presses
    /// the confirm button; Esc, "Vazgeç", a state change or hiding the panel drop it. The cancel button gets the initial focus so
    /// a stray Enter never confirms a destructive action.
    /// </summary>
    public sealed class ConfirmModal
    {
        private readonly RectTransform root;
        private readonly RectTransform card;
        private readonly CanvasGroup group;
        private readonly TextMeshProUGUI title;
        private readonly TextMeshProUGUI message;
        private readonly UiButton cancelButton;
        private readonly UiButton confirmButton;
        private Action pendingAction;

        private ConfirmModal(RectTransform root, RectTransform card, CanvasGroup group, TextMeshProUGUI title, TextMeshProUGUI message,
            UiButton cancelButton, UiButton confirmButton)
        {
            this.root = root;
            this.card = card;
            this.group = group;
            this.title = title;
            this.message = message;
            this.cancelButton = cancelButton;
            this.confirmButton = confirmButton;
        }

        /// <summary>True while the dialog is visible and waiting for an answer.</summary>
        public bool IsOpen => root.gameObject.activeSelf;

        public static ConfirmModal Create(RectTransform canvasRoot)
        {
            var dim = UiFactory.CreateImage("ConfirmModal", canvasRoot, OperatorUiStyle.Dim, true);
            var root = dim.rectTransform;
            UiFactory.Stretch(root);
            var group = root.gameObject.AddComponent<CanvasGroup>();

            var card = UiFactory.CreateCard(root, "Card", 600f, 32, 16f);
            var title = UiFactory.CreateLabel(card, "Title", string.Empty, OperatorUiStyle.FontHeading, OperatorUiStyle.TextPrimary, FontStyles.Bold);
            var message = UiFactory.CreateLabel(card, "Message", string.Empty, OperatorUiStyle.FontBody, OperatorUiStyle.TextSecondary);
            UiFactory.CreateSpacer(card, 10f, 8f);
            var buttons = UiFactory.CreateRow(card, "Buttons", 16f, TextAnchor.MiddleRight, false);
            ConfirmModal modal = null;
            var cancel = UiFactory.CreateButton(buttons, "Cancel", "Vazgeç", ButtonStyle.Secondary, () => modal.Close(),
                OperatorUiStyle.ButtonHeight, OperatorUiStyle.FontBody, 160f);
            var confirm = UiFactory.CreateButton(buttons, "Confirm", "Onayla", ButtonStyle.Danger, () => modal.OnConfirm(),
                OperatorUiStyle.ButtonHeight, OperatorUiStyle.FontBody, 240f);

            modal = new ConfirmModal(root, card, group, title, message, cancel, confirm);
            root.gameObject.SetActive(false);
            return modal;
        }

        /// <summary>Shows the dialog; <paramref name="onConfirm"/> runs when the operator confirms.</summary>
        public void Ask(string heading, string question, string confirmLabel, ButtonStyle confirmStyle, Action onConfirm)
        {
            pendingAction = onConfirm;
            title.text = heading ?? string.Empty;
            message.text = question ?? string.Empty;
            confirmButton.SetLabel(confirmLabel);
            confirmButton.SetStyle(confirmStyle);
            root.gameObject.SetActive(true);
            root.SetAsLastSibling();
            UiTween.PanelIn(group, card, 16f, UiTween.Fast);
            var eventSystem = EventSystem.current;
            if (eventSystem != null && !eventSystem.alreadySelecting)
            {
                eventSystem.SetSelectedGameObject(cancelButton.GameObject);
            }
        }

        /// <summary>Hides the dialog and forgets the pending action.</summary>
        public void Close()
        {
            pendingAction = null;
            if (root.gameObject.activeSelf)
            {
                UiTween.Cancel(group);
                group.alpha = 1f;
                card.anchoredPosition = Vector2.zero;
                root.gameObject.SetActive(false);
            }
        }

        /// <summary>Esc closes the dialog. Call once per frame.</summary>
        public void Tick()
        {
            if (!IsOpen)
            {
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Close();
            }
        }

        private void OnConfirm()
        {
            var action = pendingAction;
            Close();
            action?.Invoke();
        }
    }

    /// <summary>Shared tooltip bubble; <see cref="UiTooltipTrigger"/> components show / hide it.</summary>
    public sealed class UiTooltipHost
    {
        private readonly RectTransform canvasRect;
        private readonly RectTransform bubble;
        private readonly TextMeshProUGUI label;
        private readonly Vector3[] corners = new Vector3[4];
        private UiTooltipTrigger owner;

        private UiTooltipHost(RectTransform canvasRect, RectTransform bubble, TextMeshProUGUI label)
        {
            this.canvasRect = canvasRect;
            this.bubble = bubble;
            this.label = label;
        }

        /// <summary>The host created by the screen (null in tests / before construction).</summary>
        public static UiTooltipHost Current { get; private set; }

        public static UiTooltipHost Create(RectTransform canvasRoot)
        {
            var root = UiFactory.CreateSurface(canvasRoot, "Tooltip", OperatorUiStyle.Elevated, 8, true, true, out _);
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0f);
            UiFactory.AddHorizontalLayout(root.gameObject, 0f, new RectOffset(14, 14, 8, 8), TextAnchor.MiddleCenter, true, true, false, false);
            var fitter = root.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var label = UiFactory.CreateLabel(root, "Text", string.Empty, OperatorUiStyle.FontLabel, OperatorUiStyle.TextPrimary);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            root.gameObject.SetActive(false);
            var host = new UiTooltipHost(canvasRoot, root, label);
            Current = host;
            return host;
        }

        public static void Release(UiTooltipHost host)
        {
            if (Current == host)
            {
                Current = null;
            }
        }

        internal void Show(UiTooltipTrigger source, string text, RectTransform anchor)
        {
            owner = source;
            label.text = text;
            bubble.gameObject.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(bubble);

            anchor.GetWorldCorners(corners);
            var screenTop = RectTransformUtility.WorldToScreenPoint(null, (corners[1] + corners[2]) * 0.5f);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenTop, null, out var local);

            float half = bubble.rect.width * 0.5f;
            float limit = canvasRect.rect.width * 0.5f - 12f - half;
            local.x = Mathf.Clamp(local.x, -limit, limit);
            local.y += 8f;
            bubble.anchoredPosition = local;
            bubble.SetAsLastSibling();
        }

        internal void Hide(UiTooltipTrigger source)
        {
            if (owner == source)
            {
                owner = null;
                bubble.gameObject.SetActive(false);
            }
        }
    }

    /// <summary>Shows <see cref="Text"/> in the shared tooltip bubble while hovered (also on disabled buttons).</summary>
    public sealed class UiTooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        /// <summary>Tooltip text; empty disables the tooltip.</summary>
        public string Text;

        public void OnPointerEnter(PointerEventData eventData)
        {
            var host = UiTooltipHost.Current;
            if (host != null && !string.IsNullOrEmpty(Text))
            {
                host.Show(this, Text, (RectTransform)transform);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            UiTooltipHost.Current?.Hide(this);
        }

        private void OnDisable()
        {
            UiTooltipHost.Current?.Hide(this);
        }
    }
}
