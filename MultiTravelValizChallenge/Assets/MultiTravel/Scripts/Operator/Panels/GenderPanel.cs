using MultiTravel.Core.Session;
using MultiTravel.Operator.UI;
using TMPro;
using UnityEngine;

namespace MultiTravel.Operator.Panels
{
    /// <summary>Gender variant of the product set: big "Kadın" / "Erkek" buttons (GenderSelection → Instructions).</summary>
    public sealed class GenderPanel : OperatorPanel
    {
        private TextMeshProUGUI participantLabel;
        private ConfirmPrompt cancelPrompt;

        public GenderPanel(OperatorContext context) : base(context)
        {
        }

        public override bool HandlesState(SessionState state)
        {
            return state == SessionState.GenderSelection;
        }

        protected override void OnBuild(RectTransform root)
        {
            var card = UiFactory.CreateCard(root, "Card", OperatorUiStyle.CardWidth, 48, 22f);
            CreateHeading(card, "Ürün Seti Seçimi");
            participantLabel = UiFactory.CreateLabel(card, "Participant", string.Empty, OperatorUiStyle.FontHeading,
                OperatorUiStyle.TextPrimary, FontStyles.Bold, TextAlignmentOptions.Center);
            UiFactory.MakeSingleLine(participantLabel);
            CreateBody(card, "Hint", "Katılımcının valizinde kullanılacak ürün setini seçin.");

            var row = UiFactory.CreateRow(card, "GenderButtons", 32f);
            var female = UiFactory.CreateButton(row, "FemaleButton", "Kadın", ButtonStyle.Accent, OnFemale,
                OperatorUiStyle.BigButtonHeight, 48f);
            UiFactory.SetLayout(female.Background, 0f, OperatorUiStyle.BigButtonHeight, 1f, -1f, -1f, OperatorUiStyle.BigButtonHeight);
            var male = UiFactory.CreateButton(row, "MaleButton", "Erkek", ButtonStyle.Primary, OnMale,
                OperatorUiStyle.BigButtonHeight, 48f);
            UiFactory.SetLayout(male.Background, 0f, OperatorUiStyle.BigButtonHeight, 1f, -1f, -1f, OperatorUiStyle.BigButtonHeight);

            UiFactory.CreateSpacer(card, 10f, 4f);
            cancelPrompt = new ConfirmPrompt(card, "CancelPrompt");
            var buttons = UiFactory.CreateRow(card, "Buttons", 20f, TextAnchor.MiddleRight, false);
            UiFactory.CreateButton(buttons, "CancelButton", "Oturumu İptal Et", ButtonStyle.Secondary, OnCancelRequested,
                OperatorUiStyle.ButtonHeight, OperatorUiStyle.FontButton, 300f);
        }

        protected override void OnShow(SessionState state)
        {
            cancelPrompt.Close();
            var session = Context.Session.Current;
            participantLabel.text = session != null ? ParticipantText.FullName(session.Input) : string.Empty;
        }

        protected override void OnHide()
        {
            cancelPrompt.Close();
        }

        public override void ClearParticipantData()
        {
            if (participantLabel != null)
            {
                participantLabel.text = string.Empty;
            }
        }

        private void OnFemale()
        {
            Context.Run(SessionState.GenderSelection, () => Context.Session.SelectGender(Gender.Female), "SelectGender(Female)");
        }

        private void OnMale()
        {
            Context.Run(SessionState.GenderSelection, () => Context.Session.SelectGender(Gender.Male), "SelectGender(Male)");
        }

        private void OnCancelRequested()
        {
            cancelPrompt.Ask("Oturum iptal edilsin mi? Katılımcı bilgileri silinecek.", "Evet, iptal et", ButtonStyle.Danger, CancelSession);
        }

        private void CancelSession()
        {
            if (Context.Run(SessionState.GenderSelection, Context.Session.AbandonSession, "AbandonSession"))
            {
                Context.Run(OperatorContext.CanReset, Context.Session.ResetForNextParticipant, "ResetForNextParticipant");
            }
        }
    }
}
