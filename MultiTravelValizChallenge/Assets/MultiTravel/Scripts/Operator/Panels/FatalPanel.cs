using MultiTravel.Core.Session;
using MultiTravel.Operator.UI;
using TMPro;
using UnityEngine;

namespace MultiTravel.Operator.Panels
{
    /// <summary>Fatal error view: Turkish message from <see cref="FatalInfo"/> and "Yeniden Dene" (Fatal → Welcome).</summary>
    public sealed class FatalPanel : OperatorPanel
    {
        private TextMeshProUGUI messageLabel;
        private UiPill codePill;

        public FatalPanel(OperatorContext context) : base(context)
        {
        }

        public override bool HandlesState(SessionState state)
        {
            return state == SessionState.Fatal;
        }

        protected override void OnBuild(RectTransform root)
        {
            var card = UiFactory.CreateCard(root, "Card", 920f, 48, 20f);
            var badgeRow = UiFactory.CreateRow(card, "BadgeRow", 0f, TextAnchor.MiddleCenter, false);
            CreateBadge(badgeRow, UiIcon.Warning, OperatorUiStyle.Danger, 96f);
            var heading = CreateHeading(card, "Bir Hata Oluştu");
            heading.color = OperatorUiStyle.DangerText;
            messageLabel = UiFactory.CreateLabel(card, "Message", string.Empty, OperatorUiStyle.FontHeading, OperatorUiStyle.TextPrimary,
                FontStyles.Bold, TextAlignmentOptions.Center);
            var pillRow = UiFactory.CreateRow(card, "CodeRow", 0f, TextAnchor.MiddleCenter, false);
            codePill = UiPill.Create(pillRow, "Code", 32f, OperatorUiStyle.FontLabel);
            codePill.Set(string.Empty, OperatorUiStyle.TextMuted, false);
            CreateBody(card, "Hint", "Sorun devam ederse uygulamayı yeniden başlatın ve teknik ekibe haber verin.");

            UiFactory.CreateSpacer(card, 10f, 4f);
            var buttons = UiFactory.CreateRow(card, "Buttons", 16f, TextAnchor.MiddleCenter, false);
            UiFactory.CreateButton(buttons, "RetryButton", "Yeniden Dene", ButtonStyle.Warning, OnRetry,
                OperatorUiStyle.ButtonHeightLarge, OperatorUiStyle.FontHeading, 360f, UiIcon.Refresh);
        }

        protected override void OnShow(SessionState state)
        {
            var fatal = Context.Session.Fatal;
            messageLabel.text = fatal != null ? fatal.Message : FatalInfo.MessageFor(FatalReason.Unexpected);
            codePill.Set(fatal != null ? "Hata kodu: " + fatal.Reason : string.Empty, OperatorUiStyle.Danger, false);
            UiFactory.SetActive(codePill.Root, fatal != null);
        }

        private void OnRetry()
        {
            Context.Run(SessionState.Fatal, Context.Session.ResetForNextParticipant, "ResetForNextParticipant");
        }
    }
}
