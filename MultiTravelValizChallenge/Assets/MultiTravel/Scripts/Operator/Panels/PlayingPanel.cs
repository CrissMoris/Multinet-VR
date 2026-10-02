using MultiTravel.Core.Session;
using MultiTravel.Core.Utility;
using MultiTravel.Operator.UI;
using TMPro;
using UnityEngine;

namespace MultiTravel.Operator.Panels
{
    /// <summary>
    /// Live game view: score, timer (mm:ss.f), required-item progress, correct / incorrect counts.
    /// "Zorla Bitir" → <see cref="SessionController.CompleteGame"/>(OperatorForced) and "Oturumu İptal Et" →
    /// <see cref="SessionController.AbandonSession"/>, both behind an inline confirmation. Refresh is allocation-free.
    /// </summary>
    public sealed class PlayingPanel : OperatorPanel
    {
        private readonly ElapsedTextBuffer timerText = new ElapsedTextBuffer();

        private TextMeshProUGUI participantLabel;
        private TextMeshProUGUI scoreValue;
        private TextMeshProUGUI timerValue;
        private TextMeshProUGUI progressValue;
        private TextMeshProUGUI countsLabel;
        private TextMeshProUGUI timeLimitLabel;
        private ConfirmPrompt confirmPrompt;

        private int lastScore;
        private int lastPlaced;
        private int lastRequired;
        private int lastCorrect;
        private int lastIncorrect;

        public PlayingPanel(OperatorContext context) : base(context)
        {
        }

        public override bool HandlesState(SessionState state)
        {
            return state == SessionState.Playing;
        }

        protected override void OnBuild(RectTransform root)
        {
            var card = UiFactory.CreateCard(root, "Card", 1200f, 44, 20f);
            CreateHeading(card, "Oyun Devam Ediyor");
            participantLabel = UiFactory.CreateLabel(card, "Participant", string.Empty, OperatorUiStyle.FontBody,
                OperatorUiStyle.TextSecondary, FontStyles.Bold, TextAlignmentOptions.Center);
            UiFactory.MakeSingleLine(participantLabel);

            var tiles = UiFactory.CreateRow(card, "Tiles", 24f);
            scoreValue = CreateTile(tiles, "ScoreTile", "Puan", OperatorUiStyle.Primary);
            timerValue = CreateTile(tiles, "TimerTile", "Süre", OperatorUiStyle.Primary);
            progressValue = CreateTile(tiles, "ProgressTile", "Gerekli ürün", OperatorUiStyle.Accent);

            countsLabel = CreateBody(card, "Counts", string.Empty);
            timeLimitLabel = UiFactory.CreateLabel(card, "TimeLimit", string.Empty, OperatorUiStyle.FontSmall,
                OperatorUiStyle.TextSecondary, FontStyles.Normal, TextAlignmentOptions.Center);

            confirmPrompt = new ConfirmPrompt(card, "ConfirmPrompt");
            var buttons = UiFactory.CreateRow(card, "Buttons", 20f, TextAnchor.MiddleRight, false);
            UiFactory.CreateButton(buttons, "AbandonButton", "Oturumu İptal Et", ButtonStyle.Secondary, OnAbandonRequested,
                OperatorUiStyle.ButtonHeight, OperatorUiStyle.FontButton, 300f);
            UiFactory.CreateButton(buttons, "ForceFinishButton", "Zorla Bitir", ButtonStyle.Warning, OnForceFinishRequested,
                OperatorUiStyle.ButtonHeight, OperatorUiStyle.FontButton, 300f);
        }

        protected override void OnShow(SessionState state)
        {
            confirmPrompt.Close();
            var session = Context.Session.Current;
            participantLabel.text = session != null && session.GenderSelected
                ? ParticipantText.FullName(session.Input) + " · " + ParticipantText.GenderLabel(session.Gender) + " ürün seti"
                : ParticipantText.FullName(session?.Input);

            int limitSeconds = Context.Config.Gameplay.TimeLimitSeconds;
            timeLimitLabel.text = limitSeconds > 0 ? "Süre sınırı: " + TimeFormat.FormatSeconds(limitSeconds * 1000L) : string.Empty;
            UiFactory.SetActive(timeLimitLabel, limitSeconds > 0);

            lastScore = int.MinValue;
            lastPlaced = -1;
            lastRequired = -1;
            lastCorrect = -1;
            lastIncorrect = -1;
            timerText.Invalidate();
        }

        protected override void OnHide()
        {
            confirmPrompt.Close();
        }

        public override void Refresh()
        {
            var score = Context.Score;
            if (score.Score != lastScore)
            {
                lastScore = score.Score;
                scoreValue.SetText("{0}", lastScore);
            }

            timerText.Apply(timerValue, Context.Timer.ElapsedMs);

            var completion = Context.Completion;
            if (completion.RequiredPlacedCount != lastPlaced || completion.RequiredTotal != lastRequired)
            {
                lastPlaced = completion.RequiredPlacedCount;
                lastRequired = completion.RequiredTotal;
                progressValue.SetText("{0} / {1}", lastPlaced, lastRequired);
            }

            if (score.PositiveCount != lastCorrect || score.NegativeCount != lastIncorrect)
            {
                lastCorrect = score.PositiveCount;
                lastIncorrect = score.NegativeCount;
                countsLabel.SetText("Doğru ürün: {0}     Yanlış ürün: {1}", lastCorrect, lastIncorrect);
            }
        }

        public override void ClearParticipantData()
        {
            if (participantLabel != null)
            {
                participantLabel.text = string.Empty;
            }
        }

        private void OnForceFinishRequested()
        {
            confirmPrompt.Ask("Oyun şimdi bitirilsin mi? Mevcut puan ve süre sonuç olarak kaydedilecek.", "Evet, bitir",
                ButtonStyle.Warning, ForceFinish);
        }

        private void OnAbandonRequested()
        {
            confirmPrompt.Ask("Oturum iptal edilsin mi? Sonuç liderlik tablosuna gönderilmeyecek.", "Evet, iptal et",
                ButtonStyle.Danger, Abandon);
        }

        private void ForceFinish()
        {
            Context.Run(SessionState.Playing, () => Context.Session.CompleteGame(CompletionReason.OperatorForced), "CompleteGame(OperatorForced)");
        }

        private void Abandon()
        {
            Context.Run(SessionState.Playing, Context.Session.AbandonSession, "AbandonSession");
        }

        private static TextMeshProUGUI CreateTile(Transform row, string name, string caption, Color valueColor)
        {
            var tile = UiFactory.CreateImage(name, row, OperatorUiStyle.RowAlternate);
            UiFactory.AddVerticalLayout(tile.gameObject, 4f, new RectOffset(20, 20, 18, 18), TextAnchor.MiddleCenter, true, true, true, false);
            UiFactory.SetLayout(tile, 0f, -1f, 1f, -1f);
            UiFactory.CreateLabel(tile.rectTransform, "Caption", caption, OperatorUiStyle.FontSmall, OperatorUiStyle.TextSecondary,
                FontStyles.Bold, TextAlignmentOptions.Center);
            var value = UiFactory.CreateLabel(tile.rectTransform, "Value", "-", OperatorUiStyle.FontStat, valueColor,
                FontStyles.Bold, TextAlignmentOptions.Center);
            UiFactory.MakeSingleLine(value);
            return value;
        }
    }
}
