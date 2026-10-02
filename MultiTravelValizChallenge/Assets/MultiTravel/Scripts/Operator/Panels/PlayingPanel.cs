using MultiTravel.Core.Session;
using MultiTravel.Core.Utility;
using MultiTravel.Operator.UI;
using TMPro;
using UnityEngine;

namespace MultiTravel.Operator.Panels
{
    /// <summary>
    /// Live game view: huge tabular timer, rolling score tile, required-item progress ring, the spectator video, participant
    /// chip and correct / incorrect counters. "Zorla Bitir" → <see cref="SessionController.CompleteGame"/>(OperatorForced) and
    /// "İptal" → <see cref="SessionController.AbandonSession"/>, both behind a confirmation dialog. Refresh is allocation-free.
    /// </summary>
    public sealed class PlayingPanel : OperatorPanel
    {
        private readonly ElapsedTextBuffer timerText = new ElapsedTextBuffer(true);

        private TextMeshProUGUI timerValue;
        private TextMeshProUGUI timeLimitLabel;
        private TextMeshProUGUI participantLabel;
        private TextMeshProUGUI participantSet;
        private UiStatTile scoreTile;
        private UiProgressRing ring;
        private UiPill correctPill;
        private UiPill incorrectPill;

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
            var column = UiFactory.CreateRect("Column", root);
            UiFactory.Stretch(column);
            UiFactory.AddVerticalLayout(column.gameObject, 16f, new RectOffset(0, 0, 0, 0), TextAnchor.UpperLeft, true, true, true, false);

            // ----- top row: timer | score | progress -----
            var top = UiFactory.CreateRow(column, "TopRow", 16f, TextAnchor.UpperLeft);
            UiFactory.SetLayout(top, -1f, 256f, 1f, 0f, -1f, 256f);
            top.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>().childForceExpandHeight = true;

            var timerCard = UiFactory.CreateSurface(top, "TimerCard", OperatorUiStyle.Card, OperatorUiStyle.RadiusCard, true, true, out _);
            UiFactory.SetLayout(timerCard, 0f, -1f, 2.3f, 1f);
            UiFactory.AddVerticalLayout(timerCard.gameObject, 0f, new RectOffset(24, 24, 20, 12), TextAnchor.MiddleCenter, true, true, true, false);
            var timerCaption = UiFactory.CreateLabel(timerCard, "Caption", "SÜRE", OperatorUiStyle.FontLabel, OperatorUiStyle.TextSecondary, FontStyles.Bold,
                TextAlignmentOptions.Center);
            timerCaption.characterSpacing = 6f;
            timerValue = UiFactory.CreateLabel(timerCard, "Timer", "00:00.0", 128f, OperatorUiStyle.TextPrimary, FontStyles.Bold,
                TextAlignmentOptions.Center);
            timerValue.richText = true;
            UiFactory.MakeSingleLine(timerValue);
            timeLimitLabel = UiFactory.CreateLabel(timerCard, "TimeLimit", string.Empty, OperatorUiStyle.FontLabel, OperatorUiStyle.TextMuted,
                FontStyles.Normal, TextAlignmentOptions.Center);

            scoreTile = UiStatTile.Create(top, "ScoreTile", "PUAN", 104f, OperatorUiStyle.Accent, UiStatTile.TabularFormat, UiIcon.Trophy);
            UiFactory.SetLayout(scoreTile.Root, 0f, -1f, 1.1f, 1f);

            var ringCard = UiFactory.CreateSurface(top, "ProgressCard", OperatorUiStyle.Elevated, OperatorUiStyle.RadiusCard, false, true, out _);
            UiFactory.SetLayout(ringCard, 0f, -1f, 1f, 1f);
            UiFactory.AddVerticalLayout(ringCard.gameObject, 6f, new RectOffset(16, 16, 20, 16), TextAnchor.MiddleCenter, true, true, true, false);
            var ringCaption = UiFactory.CreateLabel(ringCard, "Caption", "GEREKLİ ÜRÜN", OperatorUiStyle.FontLabel, OperatorUiStyle.TextSecondary,
                FontStyles.Bold, TextAlignmentOptions.Center);
            ringCaption.characterSpacing = 4f;
            var ringRow = UiFactory.CreateRow(ringCard, "RingRow", 0f, TextAnchor.MiddleCenter, false);
            ring = UiProgressRing.Create(ringRow, "Ring", 152, OperatorUiStyle.Success);

            // ----- spectator video -----
            var frameHost = Context.Feed.CreateFrame(column, "SpectatorFrame", out var overlay);
            _ = frameHost;
            var live = UiPill.Create(overlay, "LivePill", 28f, OperatorUiStyle.FontCaption, true, true);
            live.Set("CANLI", OperatorUiStyle.Danger, false);
            var liveRect = live.Root;
            UiFactory.IgnoreLayout(liveRect);
            liveRect.anchorMin = new Vector2(0f, 1f);
            liveRect.anchorMax = new Vector2(0f, 1f);
            liveRect.pivot = new Vector2(0f, 1f);
            liveRect.anchoredPosition = new Vector2(16f, -16f);

            // ----- bottom bar -----
            var bar = UiFactory.CreateSurface(column, "Bar", OperatorUiStyle.Card, OperatorUiStyle.RadiusCard, true, true, out _);
            UiFactory.SetLayout(bar, -1f, 88f, 1f, 0f, -1f, 88f);
            UiFactory.AddHorizontalLayout(bar.gameObject, 16f, new RectOffset(24, 24, 0, 0), TextAnchor.MiddleLeft, true, true, false, false);

            UiFactory.CreateIcon(bar, "UserIcon", UiIcon.User, 28f, OperatorUiStyle.TextSecondary);
            var names = UiFactory.CreateColumn(bar, "Names", 0f, TextAnchor.MiddleLeft);
            UiFactory.SetLayout(names, 0f, -1f, 1f, -1f);
            participantLabel = UiFactory.CreateLabel(names, "Name", string.Empty, OperatorUiStyle.FontBody, OperatorUiStyle.TextPrimary, FontStyles.Bold);
            UiFactory.MakeSingleLine(participantLabel);
            participantSet = UiFactory.CreateLabel(names, "Set", string.Empty, OperatorUiStyle.FontLabel, OperatorUiStyle.TextMuted);
            UiFactory.MakeSingleLine(participantSet);

            correctPill = UiPill.Create(bar, "CorrectPill", 36f, OperatorUiStyle.FontLabel);
            correctPill.Set("Doğru 0", OperatorUiStyle.Success, false);
            incorrectPill = UiPill.Create(bar, "IncorrectPill", 36f, OperatorUiStyle.FontLabel);
            incorrectPill.Set("Yanlış 0", OperatorUiStyle.TextMuted, false);

            UiFactory.CreateButton(bar, "AbandonButton", "İptal", ButtonStyle.Ghost, OnAbandonRequested,
                OperatorUiStyle.ButtonHeight, OperatorUiStyle.FontBody, 160f, UiIcon.Close);
            UiFactory.CreateButton(bar, "ForceFinishButton", "Zorla Bitir", ButtonStyle.Warning, OnForceFinishRequested,
                OperatorUiStyle.ButtonHeight, OperatorUiStyle.FontBody, 220f);
        }

        protected override void OnShow(SessionState state)
        {
            CloseConfirm();
            var session = Context.Session.Current;
            participantLabel.text = ParticipantText.FullName(session?.Input);
            participantSet.text = session != null && session.GenderSelected ? ParticipantText.GenderLabel(session.Gender) + " ürün seti" : string.Empty;

            int limitSeconds = Context.Config.Gameplay.TimeLimitSeconds;
            timeLimitLabel.text = limitSeconds > 0 ? "Süre sınırı: " + TimeFormat.FormatSeconds(limitSeconds * 1000L) : string.Empty;
            UiFactory.SetActive(timeLimitLabel, limitSeconds > 0);

            lastScore = int.MinValue;
            lastPlaced = -1;
            lastRequired = -1;
            lastCorrect = -1;
            lastIncorrect = -1;
            timerText.Invalidate();
            scoreTile.Reset();
            ring.Invalidate();
        }

        protected override void OnHide()
        {
            CloseConfirm();
        }

        public override void Refresh()
        {
            var score = Context.Score;
            if (score.Score != lastScore)
            {
                bool first = lastScore == int.MinValue;
                lastScore = score.Score;
                scoreTile.SetValue(lastScore, !first);
            }

            timerText.Apply(timerValue, Context.Timer.ElapsedMs);

            var completion = Context.Completion;
            if (completion.RequiredPlacedCount != lastPlaced || completion.RequiredTotal != lastRequired)
            {
                bool first = lastPlaced < 0;
                lastPlaced = completion.RequiredPlacedCount;
                lastRequired = completion.RequiredTotal;
                ring.SetProgress(lastPlaced, lastRequired, !first);
            }

            if (score.PositiveCount != lastCorrect || score.NegativeCount != lastIncorrect)
            {
                lastCorrect = score.PositiveCount;
                lastIncorrect = score.NegativeCount;
                correctPill.Set("Doğru " + lastCorrect, OperatorUiStyle.Success);
                incorrectPill.Set("Yanlış " + lastIncorrect, lastIncorrect > 0 ? OperatorUiStyle.DangerText : OperatorUiStyle.TextMuted);
            }
        }

        public override void ClearParticipantData()
        {
            if (participantLabel != null)
            {
                participantLabel.text = string.Empty;
                participantSet.text = string.Empty;
            }
        }

        private void OnForceFinishRequested()
        {
            Confirm("Oyun şimdi bitirilsin mi?", "Mevcut puan ve süre sonuç olarak kaydedilecek.", "Evet, bitir", ButtonStyle.Warning, ForceFinish);
        }

        private void OnAbandonRequested()
        {
            Confirm("Oturum iptal edilsin mi?", "Sonuç liderlik tablosuna gönderilmeyecek.", "Evet, iptal et", ButtonStyle.Danger, Abandon);
        }

        private void ForceFinish()
        {
            Context.Run(SessionState.Playing, () => Context.Session.CompleteGame(CompletionReason.OperatorForced), "CompleteGame(OperatorForced)");
        }

        private void Abandon()
        {
            Context.Run(SessionState.Playing, Context.Session.AbandonSession, "AbandonSession");
        }
    }
}
