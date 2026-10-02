using MultiTravel.Core.Session;
using MultiTravel.Operator.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiTravel.Operator.Panels
{
    /// <summary>
    /// Pre-game briefing: a preparation checklist (headset, floor mark, practice item via <c>ITutorialStatus</c>), the configured
    /// instructions and scenario text, and "Oyunu Başlat" (Instructions → Loading). The checklist only informs; it never blocks
    /// the start (the headset warning is advisory, as before).
    /// </summary>
    public sealed class InstructionsPanel : OperatorPanel
    {
        private TextMeshProUGUI participantLabel;
        private TextMeshProUGUI scenarioLabel;
        private TextMeshProUGUI instructionsLabel;
        private RectTransform scenarioBox;
        private TextMeshProUGUI headsetWarning;
        private CheckRow headsetRow;
        private CheckRow tutorialRow;
        private UiToggle floorToggle;
        private int headsetState = -1;
        private int tutorialState = -1;

        public InstructionsPanel(OperatorContext context) : base(context)
        {
        }

        public override bool HandlesState(SessionState state)
        {
            return state == SessionState.Instructions;
        }

        protected override void OnBuild(RectTransform root)
        {
            var card = UiFactory.CreateCard(root, "Card", 1120f, 40, 20f);

            var header = UiFactory.CreateRow(card, "Header", 20f, TextAnchor.MiddleLeft, false);
            CreateBadge(header, UiIcon.Play, OperatorUiStyle.Accent, 72f);
            var titles = UiFactory.CreateColumn(header, "Titles", 4f, TextAnchor.MiddleLeft);
            UiFactory.SetLayout(titles, 0f, -1f, 1f, -1f);
            var heading = CreateHeading(titles, "Oyun Talimatları", TextAlignmentOptions.Left);
            UiFactory.MakeSingleLine(heading);
            participantLabel = UiFactory.CreateLabel(titles, "Participant", string.Empty, OperatorUiStyle.FontBody, OperatorUiStyle.TextSecondary,
                FontStyles.Normal, TextAlignmentOptions.Left);
            UiFactory.MakeSingleLine(participantLabel);

            UiFactory.CreateDivider(card);

            var columns = UiFactory.CreateRow(card, "Columns", 24f, TextAnchor.UpperLeft);

            // ----- left: checklist -----
            var left = UiFactory.CreateColumn(columns, "Checklist", 12f);
            UiFactory.SetLayout(left, 0f, -1f, 1f, 0f);
            UiFactory.CreateLabel(left, "Title", "HAZIRLIK KONTROL LİSTESİ", OperatorUiStyle.FontCaption, OperatorUiStyle.TextMuted, FontStyles.Bold)
                .characterSpacing = 4f;
            headsetRow = CheckRow.Create(left, "HeadsetRow", "VR başlığı takılı ve görüntü net");
            var floorRow = UiFactory.CreateSurface(left, "FloorRow", OperatorUiStyle.Elevated, OperatorUiStyle.RadiusButton, false, true, out _);
            UiFactory.AddHorizontalLayout(floorRow.gameObject, 12f, new RectOffset(20, 20, 0, 0), TextAnchor.MiddleLeft, true, true, true, false);
            UiFactory.SetLayout(floorRow, -1f, 64f, 1f, 0f, -1f, 64f);
            floorToggle = UiToggle.Create(floorRow, "FloorToggle", "Katılımcı zemin işaretinde (gerekirse Yeniden Ortala)", OperatorUiStyle.FontLabel);
            tutorialRow = CheckRow.Create(left, "TutorialRow", "Deneme ürünü valize yerleştirildi");

            // ----- right: briefing text -----
            var right = UiFactory.CreateColumn(columns, "Briefing", 12f);
            UiFactory.SetLayout(right, 0f, -1f, 1.2f, 0f);
            UiFactory.CreateLabel(right, "Title", "KATILIMCIYA ANLATIN", OperatorUiStyle.FontCaption, OperatorUiStyle.TextMuted, FontStyles.Bold)
                .characterSpacing = 4f;

            scenarioBox = UiFactory.CreateSurface(right, "ScenarioBox", OperatorUiStyle.WithAlpha(OperatorUiStyle.Accent, 0.12f), OperatorUiStyle.RadiusButton, false, false, out _);
            UiFactory.AddHorizontalLayout(scenarioBox.gameObject, 14f, new RectOffset(18, 18, 14, 14), TextAnchor.UpperLeft, true, true, false, false);
            UiFactory.CreateIcon(scenarioBox, "Icon", UiIcon.Suitcase, 24f, OperatorUiStyle.Accent);
            scenarioLabel = UiFactory.CreateLabel(scenarioBox, "Scenario", string.Empty, OperatorUiStyle.FontBody, OperatorUiStyle.TextPrimary,
                FontStyles.Bold, TextAlignmentOptions.TopLeft);
            UiFactory.SetLayout(scenarioLabel, 0f, -1f, 1f, -1f);

            var textBox = UiFactory.CreateSurface(right, "InstructionsBox", OperatorUiStyle.Elevated, OperatorUiStyle.RadiusButton, false, true, out _);
            UiFactory.AddVerticalLayout(textBox.gameObject, 0f, new RectOffset(20, 20, 16, 16), TextAnchor.UpperLeft, true, true, true, false);
            UiFactory.SetLayout(textBox, -1f, -1f, 1f, 0f, -1f, 200f);
            instructionsLabel = UiFactory.CreateLabel(textBox, "Instructions", string.Empty, OperatorUiStyle.FontBody, OperatorUiStyle.TextPrimary,
                FontStyles.Normal, TextAlignmentOptions.TopLeft);

            headsetWarning = UiFactory.CreateLabel(card, "HeadsetWarning",
                "Uyarı: VR başlığı şu anda algılanmıyor. Bağlantıyı kontrol edin (sağdaki VR kartı).",
                OperatorUiStyle.FontLabel, OperatorUiStyle.DangerText, FontStyles.Bold, TextAlignmentOptions.Left);
            headsetWarning.gameObject.SetActive(false);

            UiFactory.CreateSpacer(card, 10f, 4f);
            var buttons = UiFactory.CreateRow(card, "Buttons", 16f, TextAnchor.MiddleRight, false);
            UiFactory.CreateButton(buttons, "CancelButton", "Oturumu İptal Et", ButtonStyle.Ghost, OnCancelRequested,
                OperatorUiStyle.ButtonHeight, OperatorUiStyle.FontBody, 280f);
            UiFactory.CreateButton(buttons, "StartGameButton", "Oyunu Başlat", ButtonStyle.Accent, OnStartGame,
                OperatorUiStyle.ButtonHeightLarge, OperatorUiStyle.FontHeading, 380f, UiIcon.Play);
        }

        protected override void OnShow(SessionState state)
        {
            CloseConfirm();
            var texts = Context.Config.Texts;
            scenarioLabel.text = texts.ScenarioText;
            UiFactory.SetActive(scenarioBox, !string.IsNullOrWhiteSpace(texts.ScenarioText));
            instructionsLabel.text = texts.InstructionsText;

            var session = Context.Session.Current;
            participantLabel.text = session != null && session.GenderSelected
                ? ParticipantText.FullName(session.Input) + " · " + ParticipantText.GenderLabel(session.Gender) + " ürün seti"
                : ParticipantText.FullName(session?.Input);
            floorToggle.SetIsOn(false);
            headsetState = -1;
            tutorialState = -1;
        }

        protected override void OnHide()
        {
            CloseConfirm();
        }

        public override void Refresh()
        {
            var xr = Context.Xr;
            int state = xr == null ? 0 : (xr.HmdPresent ? 1 : 2);
            if (state != headsetState)
            {
                headsetState = state;
                UiFactory.SetActive(headsetWarning, state == 2);
                headsetRow.Set(state, state == 1 ? "Hazır" : state == 2 ? "Başlık yok" : "Bilinmiyor");
            }

            var tutorial = Context.Tutorial;
            int tState = tutorial == null ? 0 : (tutorial.Passed ? 1 : 2);
            if (tState != tutorialState)
            {
                tutorialState = tState;
                tutorialRow.Set(tState, tState == 1 ? "Geçti" : tState == 2 ? "Bekleniyor" : "Bilinmiyor");
            }
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
            Confirm("Oturum iptal edilsin mi?", "Bu katılımcı için sonuç kaydedilmeyecek.", "Evet, iptal et", ButtonStyle.Danger, CancelSession);
        }

        private void CancelSession()
        {
            if (Context.Run(SessionState.Instructions, Context.Session.AbandonSession, "AbandonSession"))
            {
                Context.Run(OperatorContext.CanReset, Context.Session.ResetForNextParticipant, "ResetForNextParticipant");
            }
        }

        /// <summary>Checklist row driven by a status (0 unknown, 1 ok, 2 pending).</summary>
        private sealed class CheckRow
        {
            private Image icon;
            private UiPill pill;

            public static CheckRow Create(Transform parent, string name, string text)
            {
                var row = new CheckRow();
                var surface = UiFactory.CreateSurface(parent, name, OperatorUiStyle.Elevated, OperatorUiStyle.RadiusButton, false, true, out _);
                UiFactory.AddHorizontalLayout(surface.gameObject, 12f, new RectOffset(20, 16, 0, 0), TextAnchor.MiddleLeft, true, true, false, false);
                UiFactory.SetLayout(surface, -1f, 64f, 1f, 0f, -1f, 64f);
                row.icon = UiFactory.CreateIcon(surface, "State", UiIcon.Check, 24f, OperatorUiStyle.TextMuted);
                var label = UiFactory.CreateLabel(surface, "Label", text, OperatorUiStyle.FontLabel, OperatorUiStyle.TextPrimary);
                UiFactory.SetLayout(label, 0f, -1f, 1f, -1f);
                row.pill = UiPill.Create(surface, "Pill", 28f, OperatorUiStyle.FontCaption);
                row.Set(0, "Bilinmiyor");
                return row;
            }

            public void Set(int state, string text)
            {
                if (state == 1)
                {
                    UiFactory.SetIcon(icon, UiIcon.Check, 24f);
                    icon.color = OperatorUiStyle.Success;
                    pill.Set(text, OperatorUiStyle.Success);
                }
                else if (state == 2)
                {
                    UiFactory.SetIcon(icon, UiIcon.Warning, 24f);
                    icon.color = OperatorUiStyle.Warning;
                    pill.Set(text, OperatorUiStyle.Warning);
                }
                else
                {
                    UiFactory.SetIcon(icon, UiIcon.Check, 24f);
                    icon.color = OperatorUiStyle.WithAlpha(OperatorUiStyle.TextMuted, 0.5f);
                    pill.Set(text, OperatorUiStyle.TextMuted);
                }
            }
        }
    }
}
