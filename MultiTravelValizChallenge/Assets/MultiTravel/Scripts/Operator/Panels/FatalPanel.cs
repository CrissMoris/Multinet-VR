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
        private TextMeshProUGUI codeLabel;

        public FatalPanel(OperatorContext context) : base(context)
        {
        }

        public override bool HandlesState(SessionState state)
        {
            return state == SessionState.Fatal;
        }

        protected override void OnBuild(RectTransform root)
        {
            var card = UiFactory.CreateCard(root, "Card", 1000f, 48, 22f);
            var heading = CreateHeading(card, "Bir Hata Oluştu");
            heading.color = OperatorUiStyle.Danger;
            messageLabel = UiFactory.CreateLabel(card, "Message", string.Empty, OperatorUiStyle.FontHeading,
                OperatorUiStyle.TextPrimary, FontStyles.Bold, TextAlignmentOptions.Center);
            codeLabel = UiFactory.CreateLabel(card, "Code", string.Empty, OperatorUiStyle.FontSmall,
                OperatorUiStyle.TextMuted, FontStyles.Normal, TextAlignmentOptions.Center);
            CreateBody(card, "Hint", "Sorun devam ederse uygulamayı yeniden başlatın ve teknik ekibe haber verin.");

            var buttons = UiFactory.CreateRow(card, "Buttons", 20f, TextAnchor.MiddleCenter, false);
            UiFactory.CreateButton(buttons, "RetryButton", "Yeniden Dene", ButtonStyle.Warning, OnRetry, 72f, 30f, 340f);
        }

        protected override void OnShow(SessionState state)
        {
            var fatal = Context.Session.Fatal;
            messageLabel.text = fatal != null ? fatal.Message : FatalInfo.MessageFor(FatalReason.Unexpected);
            codeLabel.text = fatal != null ? "Hata kodu: " + fatal.Reason : string.Empty;
        }

        private void OnRetry()
        {
            Context.Run(SessionState.Fatal, Context.Session.ResetForNextParticipant, "ResetForNextParticipant");
        }
    }
}
