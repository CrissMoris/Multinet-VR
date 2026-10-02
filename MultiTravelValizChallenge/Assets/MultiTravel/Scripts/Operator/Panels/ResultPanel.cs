using MultiTravel.Core.Backend;
using MultiTravel.Core.Session;
using MultiTravel.Core.Utility;
using MultiTravel.Operator.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiTravel.Operator.Panels
{
    /// <summary>
    /// Result view for Completed / Submitting / SubmissionFailed / Finished: score, time, rank (when the receipt has it)
    /// and a coloured submission status (Gönderiliyor / Gönderildi / Gönderilemedi + error). "Tekrar Gönder" only in
    /// SubmissionFailed; "Yeni Katılımcı" in Finished and SubmissionFailed. Content is rebuilt on every state change.
    /// </summary>
    public sealed class ResultPanel : OperatorPanel
    {
        private TextMeshProUGUI heading;
        private TextMeshProUGUI participantLabel;
        private RectTransform tilesRow;
        private TextMeshProUGUI scoreValue;
        private TextMeshProUGUI timeValue;
        private Image rankTile;
        private TextMeshProUGUI rankValue;
        private TextMeshProUGUI reasonLabel;
        private TextMeshProUGUI countsLabel;
        private Image statusStrip;
        private Image statusDot;
        private TextMeshProUGUI statusLabel;
        private TextMeshProUGUI detailLabel;
        private UiButton retryButton;
        private UiButton newParticipantButton;

        public ResultPanel(OperatorContext context) : base(context)
        {
        }

        public override bool HandlesState(SessionState state)
        {
            return state == SessionState.Completed
                   || state == SessionState.Submitting
                   || state == SessionState.SubmissionFailed
                   || state == SessionState.Finished;
        }

        protected override void OnBuild(RectTransform root)
        {
            var card = UiFactory.CreateCard(root, "Card", 1200f, 44, 18f);
            heading = CreateHeading(card, string.Empty);
            participantLabel = UiFactory.CreateLabel(card, "Participant", string.Empty, OperatorUiStyle.FontHeading,
                OperatorUiStyle.TextPrimary, FontStyles.Bold, TextAlignmentOptions.Center);
            UiFactory.MakeSingleLine(participantLabel);

            tilesRow = UiFactory.CreateRow(card, "Tiles", 24f);
            scoreValue = CreateTile(tilesRow, "ScoreTile", "Puan", OperatorUiStyle.Primary, out _);
            timeValue = CreateTile(tilesRow, "TimeTile", "Süre", OperatorUiStyle.Primary, out _);
            rankValue = CreateTile(tilesRow, "RankTile", "Sıra", OperatorUiStyle.Accent, out rankTile);

            reasonLabel = CreateBody(card, "Reason", string.Empty);
            countsLabel = UiFactory.CreateLabel(card, "Counts", string.Empty, OperatorUiStyle.FontSmall,
                OperatorUiStyle.TextSecondary, FontStyles.Normal, TextAlignmentOptions.Center);

            statusStrip = UiFactory.CreateImage("Status", card, OperatorUiStyle.Neutral);
            UiFactory.AddHorizontalLayout(statusStrip.gameObject, 14f, new RectOffset(22, 22, 16, 16), TextAnchor.MiddleLeft, true, true, false, false);
            statusDot = UiFactory.CreateDot(statusStrip.rectTransform, "Dot", 22f, OperatorUiStyle.TextMuted);
            statusLabel = UiFactory.CreateLabel(statusStrip.rectTransform, "Text", string.Empty, OperatorUiStyle.FontHeading,
                OperatorUiStyle.TextPrimary, FontStyles.Bold);
            UiFactory.SetLayout(statusLabel, -1f, -1f, 1f, -1f);

            detailLabel = UiFactory.CreateLabel(card, "Detail", string.Empty, OperatorUiStyle.FontBody,
                OperatorUiStyle.TextSecondary, FontStyles.Normal, TextAlignmentOptions.Left);

            var buttons = UiFactory.CreateRow(card, "Buttons", 20f, TextAnchor.MiddleRight, false);
            retryButton = UiFactory.CreateButton(buttons, "RetryButton", "Tekrar Gönder", ButtonStyle.Warning, OnRetry,
                OperatorUiStyle.ButtonHeight, OperatorUiStyle.FontButton, 300f);
            newParticipantButton = UiFactory.CreateButton(buttons, "NewParticipantButton", "Yeni Katılımcı", ButtonStyle.Primary,
                OnNewParticipant, 72f, 30f, 340f);
        }

        protected override void OnShow(SessionState state)
        {
            var session = Context.Session.Current;
            var result = session?.Result;
            participantLabel.text = ParticipantText.FullName(session?.Input);
            UiFactory.SetActive(participantLabel, session != null);

            UiFactory.SetActive(tilesRow, result != null);
            if (result != null)
            {
                scoreValue.SetText("{0}", result.Score);
                timeValue.text = TimeFormat.FormatTenths(result.CompletionMs);
                reasonLabel.text = ParticipantText.CompletionReasonLabel(result.Reason);
                countsLabel.SetText("Doğru ürün: {0}     Yanlış ürün: {1}     Gerekli ürün sayısı: {2}",
                    result.CorrectCount, result.IncorrectCount, result.RequiredTotal);
            }

            UiFactory.SetActive(reasonLabel, result != null);
            UiFactory.SetActive(countsLabel, result != null);

            int? rank = session?.Receipt?.Rank;
            UiFactory.SetActive(rankTile, rank.HasValue);
            if (rank.HasValue)
            {
                rankValue.SetText("{0}", rank.Value);
            }

            bool abandoned = session == null || session.Outcome == SessionOutcome.Abandoned;
            switch (state)
            {
                case SessionState.Completed:
                case SessionState.Submitting:
                    heading.text = "Oyun Tamamlandı";
                    SetStatus("Gönderiliyor…", OperatorUiStyle.Warning);
                    detailLabel.text = "Sonuç sunucuya gönderiliyor. Bağlantı yoksa otomatik olarak yeniden denenecek.";
                    break;

                case SessionState.SubmissionFailed:
                    heading.text = "Oyun Tamamlandı";
                    SetStatus("Gönderilemedi: " + ErrorMessage(session), OperatorUiStyle.Danger);
                    detailLabel.text =
                        "Sonuç bu bilgisayarda güvenle saklandı ve arka planda otomatik olarak yeniden gönderilecek. " +
                        "'Tekrar Gönder' ile hemen deneyebilir ya da 'Yeni Katılımcı' ile devam edebilirsiniz.";
                    break;

                default:
                    if (abandoned)
                    {
                        heading.text = "Oturum İptal Edildi";
                        SetStatus("Liderlik tablosuna gönderilmedi", OperatorUiStyle.TextMuted);
                        bool queued = session != null && session.Result != null && !session.IsSubmitted
                                      && session.SubmissionId.HasValue && Context.Outbox != null
                                      && Context.Outbox.IsPending(session.SubmissionId.Value);
                        detailLabel.text = queued
                            ? "Bu oturum iptal edildi. Daha önce kaydedilen sonuç gönderim kuyruğunda kaldı ve arka planda gönderilmeye devam edecek."
                            : "Bu oturum iptal edildi. Yeni katılımcıya geçebilirsiniz.";
                    }
                    else if (session.IsSubmitted)
                    {
                        heading.text = "Oyun Tamamlandı";
                        SetStatus("Gönderildi", OperatorUiStyle.Accent);
                        detailLabel.text = rank.HasValue
                            ? "Sonuç liderlik tablosuna kaydedildi."
                            : "Sonuç sunucuya kaydedildi.";
                    }
                    else
                    {
                        heading.text = "Oyun Tamamlandı";
                        SetStatus("Gönderilmedi", OperatorUiStyle.TextMuted);
                        detailLabel.text = "Sonuç sunucuya gönderilmedi.";
                    }

                    break;
            }

            retryButton.SetActive(state == SessionState.SubmissionFailed);
            retryButton.Interactable = true;
            bool canReset = state == SessionState.Finished || state == SessionState.SubmissionFailed;
            newParticipantButton.SetActive(canReset);
            newParticipantButton.Interactable = true;
        }

        public override void ClearParticipantData()
        {
            if (participantLabel == null)
            {
                return;
            }

            participantLabel.text = string.Empty;
            scoreValue.text = string.Empty;
            timeValue.text = string.Empty;
            rankValue.text = string.Empty;
            detailLabel.text = string.Empty;
            statusLabel.text = string.Empty;
        }

        private void SetStatus(string text, Color color)
        {
            statusLabel.text = text;
            statusLabel.color = OperatorUiStyle.Darken(color, 0.15f);
            statusDot.color = color;
            statusStrip.color = OperatorUiStyle.Lighten(color, 0.86f);
        }

        private static string ErrorMessage(ParticipantSession session)
        {
            var error = session?.LastSubmissionError;
            return error != null && !string.IsNullOrWhiteSpace(error.Message) ? error.Message : BackendErrorMessages.ServerUnreachable;
        }

        private void OnRetry()
        {
            retryButton.Interactable = false;
            if (!Context.Run(SessionState.SubmissionFailed, Context.Session.RetrySubmission, "RetrySubmission"))
            {
                retryButton.Interactable = true;
            }
        }

        private void OnNewParticipant()
        {
            Context.Run(OperatorContext.CanReset, Context.Session.ResetForNextParticipant, "ResetForNextParticipant");
        }

        private static TextMeshProUGUI CreateTile(Transform row, string name, string caption, Color valueColor, out Image tile)
        {
            tile = UiFactory.CreateImage(name, row, OperatorUiStyle.RowAlternate);
            UiFactory.AddVerticalLayout(tile.gameObject, 4f, new RectOffset(20, 20, 18, 18), TextAnchor.MiddleCenter, true, true, true, false);
            UiFactory.SetLayout(tile, 0f, -1f, 1f, -1f);
            UiFactory.CreateLabel(tile.rectTransform, "Caption", caption, OperatorUiStyle.FontSmall, OperatorUiStyle.TextSecondary,
                FontStyles.Bold, TextAlignmentOptions.Center);
            var value = UiFactory.CreateLabel(tile.rectTransform, "Value", string.Empty, OperatorUiStyle.FontStat, valueColor,
                FontStyles.Bold, TextAlignmentOptions.Center);
            UiFactory.MakeSingleLine(value);
            return value;
        }
    }
}
