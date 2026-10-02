using MultiTravel.Core.Session;
using MultiTravel.Operator.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MultiTravel.Operator.Panels
{
    /// <summary>
    /// Product set choice: two large cards (Kadın / Erkek, keyboard 1 / 2) that select the gender variant
    /// (GenderSelection → Instructions).
    /// </summary>
    public sealed class GenderPanel : OperatorPanel
    {
        private TextMeshProUGUI participantLabel;

        public GenderPanel(OperatorContext context) : base(context)
        {
        }

        public override bool HandlesState(SessionState state)
        {
            return state == SessionState.GenderSelection;
        }

        protected override void OnBuild(RectTransform root)
        {
            var card = UiFactory.CreateCard(root, "Card", 1000f, 40, 20f);

            var header = UiFactory.CreateRow(card, "Header", 20f, TextAnchor.MiddleLeft, false);
            CreateBadge(header, UiIcon.User, OperatorUiStyle.Accent, 72f);
            var titles = UiFactory.CreateColumn(header, "Titles", 4f, TextAnchor.MiddleLeft);
            UiFactory.SetLayout(titles, 0f, -1f, 1f, -1f);
            var heading = CreateHeading(titles, "Ürün Seti Seçimi", TextAlignmentOptions.Left);
            UiFactory.MakeSingleLine(heading);
            participantLabel = UiFactory.CreateLabel(titles, "Participant", string.Empty, OperatorUiStyle.FontBody, OperatorUiStyle.TextSecondary,
                FontStyles.Normal, TextAlignmentOptions.Left);
            UiFactory.MakeSingleLine(participantLabel);

            CreateBody(card, "Hint", "Katılımcının valizinde kullanılacak ürün setini seçin. Klavyeden 1 veya 2 tuşunu da kullanabilirsiniz.",
                TextAlignmentOptions.Left);

            var row = UiFactory.CreateRow(card, "Choices", 24f, TextAnchor.UpperCenter);
            CreateChoice(row, "FemaleChoice", UiIcon.Female, "Kadın", "Kadın ürün seti", "1", OperatorUiStyle.Accent, OnFemale);
            CreateChoice(row, "MaleChoice", UiIcon.Male, "Erkek", "Erkek ürün seti", "2", OperatorUiStyle.PrimaryBright, OnMale);

            UiFactory.CreateSpacer(card, 10f, 4f);
            var buttons = UiFactory.CreateRow(card, "Buttons", 16f, TextAnchor.MiddleRight, false);
            UiFactory.CreateButton(buttons, "CancelButton", "Oturumu İptal Et", ButtonStyle.Ghost, OnCancelRequested,
                OperatorUiStyle.ButtonHeight, OperatorUiStyle.FontBody, 280f);
        }

        protected override void OnShow(SessionState state)
        {
            CloseConfirm();
            var session = Context.Session.Current;
            participantLabel.text = session != null ? ParticipantText.FullName(session.Input) : string.Empty;
        }

        protected override void OnHide()
        {
            CloseConfirm();
        }

        public override void ClearParticipantData()
        {
            if (participantLabel != null)
            {
                participantLabel.text = string.Empty;
            }
        }

        public override void Tick()
        {
            if (!CanUseShortcuts())
            {
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame)
            {
                OnFemale();
            }
            else if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame)
            {
                OnMale();
            }
        }

        private static void CreateChoice(Transform row, string name, UiIcon icon, string title, string subtitle, string key, Color accent,
            UnityEngine.Events.UnityAction onClick)
        {
            var button = UiFactory.CreateButton(row, name, string.Empty, ButtonStyle.Secondary, onClick, 320f, OperatorUiStyle.FontBody);
            button.Layout.flexibleWidth = 1f;
            button.Layout.preferredWidth = 0f;
            button.Label.gameObject.SetActive(false);
            button.Icon.gameObject.SetActive(false);

            var content = UiFactory.CreateColumn(button.GameObject.transform, "Content", 12f, TextAnchor.MiddleCenter);
            UiFactory.SetLayout(content, 0f, -1f, 1f, 1f);

            var circleRow = UiFactory.CreateRow(content, "IconRow", 0f, TextAnchor.MiddleCenter, false);
            var holder = UiFactory.CreateRect("IconHolder", circleRow);
            UiFactory.SetLayout(holder, 128f, 128f, 0f, 0f, 128f, 128f);
            var circle = UiFactory.CreateImage("Circle", holder, OperatorUiStyle.WithAlpha(accent, 0.2f), false);
            circle.sprite = UiSprites.Circle(128);
            UiFactory.Stretch(circle.rectTransform);
            var glyph = UiFactory.CreateIcon(holder, "Icon", icon, 72f, OperatorUiStyle.TextFor(accent));
            UiFactory.IgnoreLayout(glyph);
            UiFactory.AnchorCenter(glyph.rectTransform, 72f, 72f);

            UiFactory.CreateLabel(content, "Title", title, OperatorUiStyle.FontTitle, OperatorUiStyle.TextPrimary, FontStyles.Bold, TextAlignmentOptions.Center);
            UiFactory.CreateLabel(content, "Subtitle", subtitle, OperatorUiStyle.FontBody, OperatorUiStyle.TextSecondary, FontStyles.Normal, TextAlignmentOptions.Center);

            var keyRow = UiFactory.CreateRow(content, "KeyRow", 8f, TextAnchor.MiddleCenter, false);
            var chip = UiPill.Create(keyRow, "KeyChip", 28f, OperatorUiStyle.FontLabel, false);
            chip.Set("Tuş " + key, OperatorUiStyle.TextMuted, false);
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
            Confirm("Oturum iptal edilsin mi?", "Katılımcı bilgileri silinecek ve karşılama ekranına dönülecek.", "Evet, iptal et", ButtonStyle.Danger, CancelSession);
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
