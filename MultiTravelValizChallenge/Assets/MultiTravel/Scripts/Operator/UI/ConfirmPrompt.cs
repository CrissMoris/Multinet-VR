using System;
using TMPro;
using UnityEngine;

namespace MultiTravel.Operator.UI
{
    /// <summary>
    /// Inline confirmation strip ("Emin misiniz?") placed inside a panel layout. Hidden until <see cref="Ask"/> is called;
    /// the stored action runs only when the operator presses the confirm button. Closing (or hiding the panel) drops it.
    /// </summary>
    public sealed class ConfirmPrompt
    {
        private const string CancelLabel = "Vazgeç";

        private readonly RectTransform root;
        private readonly TextMeshProUGUI message;
        private readonly UiButton confirmButton;
        private Action pendingAction;

        public ConfirmPrompt(Transform parent, string name)
        {
            var background = UiFactory.CreateImage(name, parent, OperatorUiStyle.Lighten(OperatorUiStyle.Warning, 0.85f), true);
            root = background.rectTransform;
            UiFactory.AddHorizontalLayout(root.gameObject, 16f, new RectOffset(20, 20, 14, 14), TextAnchor.MiddleLeft, true, true, false, false);

            message = UiFactory.CreateLabel(root, "Message", string.Empty, OperatorUiStyle.FontBody, OperatorUiStyle.TextPrimary, FontStyles.Bold);
            UiFactory.SetLayout(message, -1f, -1f, 1f, -1f);

            confirmButton = UiFactory.CreateButton(root, "Confirm", string.Empty, ButtonStyle.Danger, OnConfirm,
                OperatorUiStyle.ButtonHeight, OperatorUiStyle.FontButton, 240f);
            UiFactory.CreateButton(root, "Cancel", CancelLabel, ButtonStyle.Secondary, Close,
                OperatorUiStyle.ButtonHeight, OperatorUiStyle.FontButton, 180f);

            root.gameObject.SetActive(false);
        }

        /// <summary>True while the strip is visible and waiting for an answer.</summary>
        public bool IsOpen => root.gameObject.activeSelf;

        /// <summary>Shows the strip with a question; <paramref name="onConfirm"/> runs when the operator confirms.</summary>
        public void Ask(string question, string confirmLabel, ButtonStyle confirmStyle, Action onConfirm)
        {
            pendingAction = onConfirm;
            message.text = question ?? string.Empty;
            confirmButton.SetLabel(confirmLabel);
            confirmButton.SetStyle(confirmStyle);
            root.gameObject.SetActive(true);
        }

        /// <summary>Hides the strip and forgets the pending action.</summary>
        public void Close()
        {
            pendingAction = null;
            if (root.gameObject.activeSelf)
            {
                root.gameObject.SetActive(false);
            }
        }

        private void OnConfirm()
        {
            var action = pendingAction;
            Close();
            action?.Invoke();
        }
    }
}
