using MultiTravel.Core.Session;
using MultiTravel.Operator.UI;
using TMPro;
using UnityEngine;

namespace MultiTravel.Operator.Panels
{
    /// <summary>
    /// Configured scenario + instructions text, headset reminder and "Oyunu Başlat" (Instructions → Loading).
    /// Warns (without blocking) when the XR status service reports no headset.
    /// </summary>
    public sealed class InstructionsPanel : OperatorPanel
    {
        private TextMeshProUGUI participantLabel;
        private TextMeshProUGUI scenarioLabel;
        private TextMeshProUGUI instructionsLabel;
        private TextMeshProUGUI headsetWarning;
        private ConfirmPrompt cancelPrompt;
        private int headsetState = -1;

        public InstructionsPanel(OperatorContext context) : base(context)
        {
        }

        public override bool HandlesState(SessionState state)
        {
            return state == SessionState.Instructions;
        }

        protected override void OnBuild(RectTransform root)
        {
            var card = UiFactory.CreateCard(root, "Card", OperatorUiStyle.CardWidth, 44, 16f);
            CreateHeading(card, "Oyun Talimatları");
            participantLabel = UiFactory.CreateLabel(card, "Participant", string.Empty, OperatorUiStyle.FontBody,
                OperatorUiStyle.TextSecondary, FontStyles.Bold, TextAlignmentOptions.Center);
            UiFactory.MakeSingleLine(participantLabel);

            scenarioLabel = UiFactory.CreateLabel(card, "Scenario", string.Empty, OperatorUiStyle.FontHeading,
                OperatorUiStyle.Primary, FontStyles.Bold, TextAlignmentOptions.Center);

            instructionsLabel = UiFactory.CreateLabel(card, "Instructions", string.Empty, OperatorUiStyle.FontBody,
                OperatorUiStyle.TextPrimary, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            instructionsLabel.enableAutoSizing = true;
            instructionsLabel.fontSizeMax = OperatorUiStyle.FontBody + 2f;
            instructionsLabel.fontSizeMin = OperatorUiStyle.FontTiny;
            instructionsLabel.overflowMode = TextOverflowModes.Ellipsis;
            UiFactory.SetLayout(instructionsLabel, -1f, 300f, 1f, -1f, -1f, 300f);

            var reminder = UiFactory.CreateImage("HeadsetReminder", card, OperatorUiStyle.Lighten(OperatorUiStyle.Accent, 0.85f));
            UiFactory.AddHorizontalLayout(reminder.gameObject, 12f, new RectOffset(20, 20, 14, 14), TextAnchor.MiddleLeft, true, true, true, false);
            UiFactory.CreateLabel(reminder.rectTransform, "Text",
                "Oyunu başlatmadan önce katılımcının VR başlığını taktığından ve görüntünün net olduğundan emin olun.",
                OperatorUiStyle.FontBody, OperatorUiStyle.TextPrimary, FontStyles.Bold);

            headsetWarning = UiFactory.CreateLabel(card, "HeadsetWarning",
                "Uyarı: VR başlığı şu anda algılanmıyor. Bağlantıyı kontrol edin (üst çubuktaki VR durumu).",
                OperatorUiStyle.FontSmall, OperatorUiStyle.Danger, FontStyles.Bold, TextAlignmentOptions.Center);
            headsetWarning.gameObject.SetActive(false);

            cancelPrompt = new ConfirmPrompt(card, "CancelPrompt");
            var buttons = UiFactory.CreateRow(card, "Buttons", 20f, TextAnchor.MiddleRight, false);
            UiFactory.CreateButton(buttons, "CancelButton", "Oturumu İptal Et", ButtonStyle.Secondary, OnCancelRequested,
                OperatorUiStyle.ButtonHeight, OperatorUiStyle.FontButton, 300f);
            UiFactory.CreateButton(buttons, "StartGameButton", "Oyunu Başlat", ButtonStyle.Accent, OnStartGame,
                80f, 32f, 380f);
        }

        protected override void OnShow(SessionState state)
        {
            cancelPrompt.Close();
            var texts = Context.Config.Texts;
            scenarioLabel.text = texts.ScenarioText;
            UiFactory.SetActive(scenarioLabel, !string.IsNullOrWhiteSpace(texts.ScenarioText));
            instructionsLabel.text = texts.InstructionsText;
            UiFactory.SetActive(instructionsLabel, !string.IsNullOrWhiteSpace(texts.InstructionsText));

            var session = Context.Session.Current;
            participantLabel.text = session != null && session.GenderSelected
                ? ParticipantText.FullName(session.Input) + " · " + ParticipantText.GenderLabel(session.Gender) + " ürün seti"
                : ParticipantText.FullName(session?.Input);
            headsetState = -1;
        }

        protected override void OnHide()
        {
            cancelPrompt.Close();
        }

        public override void Refresh()
        {
            var xr = Context.Xr;
            int state = xr == null ? 0 : (xr.HmdPresent ? 1 : 2);
            if (state == headsetState)
            {
                return;
            }

            headsetState = state;
            UiFactory.SetActive(headsetWarning, state == 2);
        }

        public override void ClearParticipantData()
        {
            if (participantLabel != null)
            {
                participantLabel.text = string.Empty;
            }
        }

        private void OnStartGame()
        {
            Context.Run(SessionState.Instructions, Context.Session.StartGame, "StartGame");
        }

        private void OnCancelRequested()
        {
            cancelPrompt.Ask("Oturum iptal edilsin mi? Bu katılımcı için sonuç kaydedilmeyecek.", "Evet, iptal et", ButtonStyle.Danger, CancelSession);
        }

        private void CancelSession()
        {
            if (Context.Run(SessionState.Instructions, Context.Session.AbandonSession, "AbandonSession"))
            {
                Context.Run(OperatorContext.CanReset, Context.Session.ResetForNextParticipant, "ResetForNextParticipant");
            }
        }
    }
}
