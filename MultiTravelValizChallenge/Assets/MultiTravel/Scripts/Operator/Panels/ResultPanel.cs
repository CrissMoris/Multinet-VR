using System;
using System.Globalization;
using System.Threading.Tasks;
using MultiTravel.Core.Backend;
using MultiTravel.Core.Session;
using MultiTravel.Core.Utility;
using MultiTravel.Operator.UI;
using TMPro;
using UnityEngine;

namespace MultiTravel.Operator.Panels
{
    /// <summary>
    /// Result view for Completed / Submitting / SubmissionFailed / Finished over the spectator backdrop: rolling score, time,
    /// rank ("#7 / 112" once the participant count is known), correct / incorrect counts and a submission status pill
    /// (Gönderiliyor / Gönderildi / Gönderilemedi + error). "Tekrar Gönder" only in SubmissionFailed; "Yeni Katılımcı" in
    /// Finished and SubmissionFailed. Content is rebuilt on every state change.
    /// </summary>
    public sealed class ResultPanel : OperatorPanel
    {
        /// <summary>Entries requested to count the participants; a full page means the real total is unknown.</summary>
        private const int TotalFetchLimit = 500;

        private TextMeshProUGUI heading;
        private TextMeshProUGUI participantLabel;
        private RectTransform tilesRow;
        private UiStatTile scoreTile;
        private UiStatTile timeTile;
        private UiStatTile rankTile;
        private RectTransform chipsRow;
        private UiPill correctPill;
        private UiPill incorrectPill;
        private UiPill requiredPill;
        private TextMeshProUGUI reasonLabel;
        private UiPill statusPill;
        private TextMeshProUGUI detailLabel;
        private UiButton retryButton;
        private UiButton newParticipantButton;

        private ParticipantSession rolledFor;
        private ParticipantSession totalRequestedFor;
        private ParticipantSession totalKnownFor;
        private int knownTotal;

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
            var card = UiFactory.CreateCard(root, "Card", 1040f, 40, 20f);

            var header = UiFactory.CreateRow(card, "Header", 16f, TextAnchor.MiddleLeft, false);
            heading = CreateHeading(header, string.Empty, TextAlignmentOptions.Left);
            UiFactory.MakeSingleLine(heading);
            UiFactory.SetLayout(heading, 0f, -1f, 1f, -1f);
            statusPill = UiPill.Create(header, "StatusPill", 40f, OperatorUiStyle.FontBody);

            participantLabel = UiFactory.CreateLabel(card, "Participant", string.Empty, OperatorUiStyle.FontHeading, OperatorUiStyle.TextSecondary,
                FontStyles.Normal, TextAlignmentOptions.Left);
            UiFactory.MakeSingleLine(participantLabel);

            tilesRow = UiFactory.CreateRow(card, "Tiles", 16f, TextAnchor.UpperCenter);
            tilesRow.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>().childForceExpandHeight = true;
            scoreTile = UiStatTile.Create(tilesRow, "ScoreTile", "PUAN", 104f, OperatorUiStyle.Accent, UiStatTile.TabularFormat, UiIcon.Trophy);
            UiFactory.SetLayout(scoreTile.Root, 0f, -1f, 1.2f, 1f);
            timeTile = UiStatTile.Create(tilesRow, "TimeTile", "SÜRE", 56f, OperatorUiStyle.TextPrimary);
            UiFactory.SetLayout(timeTile.Root, 0f, -1f, 1f, 1f);
            rankTile = UiStatTile.Create(tilesRow, "RankTile", "SIRA", 56f, OperatorUiStyle.Warning);
            UiFactory.SetLayout(rankTile.Root, 0f, -1f, 1f, 1f);

            chipsRow = UiFactory.CreateRow(card, "Chips", 12f, TextAnchor.MiddleLeft, false);
            correctPill = UiPill.Create(chipsRow, "Correct", 36f, OperatorUiStyle.FontLabel);
            incorrectPill = UiPill.Create(chipsRow, "Incorrect", 36f, OperatorUiStyle.FontLabel);
            requiredPill = UiPill.Create(chipsRow, "Required", 36f, OperatorUiStyle.FontLabel);
            reasonLabel = UiFactory.CreateLabel(chipsRow, "Reason", string.Empty, OperatorUiStyle.FontLabel, OperatorUiStyle.TextMuted,
                FontStyles.Normal, TextAlignmentOptions.Right);
            UiFactory.SetLayout(reasonLabel, 0f, -1f, 1f, -1f);

            UiFactory.CreateDivider(card);
            detailLabel = UiFactory.CreateLabel(card, "Detail", string.Empty, OperatorUiStyle.FontBody, OperatorUiStyle.TextSecondary,
                FontStyles.Normal, TextAlignmentOptions.Left);

            var buttons = UiFactory.CreateRow(card, "Buttons", 16f, TextAnchor.MiddleRight, false);
            retryButton = UiFactory.CreateButton(buttons, "RetryButton", "Tekrar Gönder", ButtonStyle.Warning, OnRetry,
                OperatorUiStyle.ButtonHeight, OperatorUiStyle.FontBody, 260f, UiIcon.Refresh);
            newParticipantButton = UiFactory.CreateButton(buttons, "NewParticipantButton", "Yeni Katılımcı", ButtonStyle.Primary,
                OnNewParticipant, OperatorUiStyle.ButtonHeightLarge, OperatorUiStyle.FontHeading, 360f, UiIcon.Play);
        }

        protected override void OnShow(SessionState state)
        {
            var session = Context.Session.Current;
            var result = session?.Result;
            participantLabel.text = ParticipantText.FullName(session?.Input);
            UiFactory.SetActive(participantLabel, session != null);

            UiFactory.SetActive(tilesRow, result != null);
            UiFactory.SetActive(chipsRow, result != null);
            if (result != null)
            {
                if (rolledFor != session)
                {
                    rolledFor = session;
                    scoreTile.RollUpFromZero(result.Score);
                }
                else
                {
                    scoreTile.SetValue(result.Score, false);
                }

                timeTile.SetText(TimeFormat.FormatTenths(result.CompletionMs));
                correctPill.Set("Doğru " + result.CorrectCount.ToString(CultureInfo.InvariantCulture), OperatorUiStyle.Success, false);
                incorrectPill.Set("Yanlış " + result.IncorrectCount.ToString(CultureInfo.InvariantCulture),
                    result.IncorrectCount > 0 ? OperatorUiStyle.DangerText : OperatorUiStyle.TextMuted, false);
                requiredPill.Set("Gerekli ürün " + result.RequiredTotal.ToString(CultureInfo.InvariantCulture), OperatorUiStyle.TextMuted, false);
                reasonLabel.text = ParticipantText.CompletionReasonLabel(result.Reason);
            }

            int? rank = session?.Receipt?.Rank;
            UiFactory.SetActive(rankTile.Root, rank.HasValue);
            if (rank.HasValue)
            {
                ShowRank(rank.Value, totalKnownFor == session ? knownTotal : 0);
                if (totalRequestedFor != session)
                {
                    totalRequestedFor = session;
                    RequestTotal(session, rank.Value);
                }
            }

            bool abandoned = session == null || session.Outcome == SessionOutcome.Abandoned;
            switch (state)
            {
                case SessionState.Completed:
                case SessionState.Submitting:
                    heading.text = "Oyun Tamamlandı";
                    statusPill.Set("Gönderiliyor…", OperatorUiStyle.Warning);
                    detailLabel.text = "Sonuç sunucuya gönderiliyor. Bağlantı yoksa otomatik olarak yeniden denenecek.";
                    break;

                case SessionState.SubmissionFailed:
                    heading.text = "Oyun Tamamlandı";
                    statusPill.Set("Gönderilemedi", OperatorUiStyle.Danger);
                    detailLabel.text = ErrorMessage(session) + " Sonuç bu bilgisayarda güvenle saklandı ve arka planda otomatik olarak yeniden gönderilecek. " +
                                       "'Tekrar Gönder' ile hemen deneyebilir ya da 'Yeni Katılımcı' ile devam edebilirsiniz.";
                    break;

                default:
                    if (abandoned)
                    {
                        heading.text = "Oturum İptal Edildi";
                        statusPill.Set("Liderlik tablosuna gönderilmedi", OperatorUiStyle.TextMuted);
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
                        statusPill.Set("Gönderildi", OperatorUiStyle.Success);
                        detailLabel.text = rank.HasValue
                            ? "Sonuç liderlik tablosuna kaydedildi."
                            : "Sonuç sunucuya kaydedildi.";
                    }
                    else
                    {
                        heading.text = "Oyun Tamamlandı";
                        statusPill.Set("Gönderilmedi", OperatorUiStyle.TextMuted);
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
            rolledFor = null;
            totalRequestedFor = null;
            totalKnownFor = null;
            if (participantLabel == null)
            {
                return;
            }

            participantLabel.text = string.Empty;
            scoreTile.Reset();
            scoreTile.SetText(string.Empty);
            timeTile.SetText(string.Empty);
            rankTile.SetText(string.Empty);
            detailLabel.text = string.Empty;
        }

        private void ShowRank(int rank, int total)
        {
            string text = "#" + rank.ToString(CultureInfo.InvariantCulture);
            if (total > 0)
            {
                text += "<size=60%><color=#AEB9CC> / " + total.ToString(CultureInfo.InvariantCulture) + "</color></size>";
            }

            rankTile.SetText(text);
        }

        /// <summary>Counts the leaderboard once so the rank can read "#7 / 112" (best effort, ignored when stale).</summary>
        private void RequestTotal(ParticipantSession session, int rank)
        {
            if (Context.Backend == null || !Context.Config.Backend.HasEndpoint)
            {
                return;
            }

            _ = FetchTotalAsync(session, Context.Epoch, rank);
        }

        private async Task FetchTotalAsync(ParticipantSession session, int epoch, int rank)
        {
            BackendResult<MultiTravel.Core.Leaderboard.LeaderboardEntry[]> result;
            try
            {
                result = await Context.Backend.GetLeaderboardAsync(TotalFetchLimit, Context.Lifetime);
            }
            catch (Exception)
            {
                return;
            }

            // Continuation guard: another participant cycle started or the screen was destroyed meanwhile.
            if (Context.Lifetime.IsCancellationRequested || epoch != Context.Epoch || Context.Session.Current != session
                || session.Receipt == null || session.Receipt.Rank != rank)
            {
                return;
            }

            var entries = result != null && result.Ok ? result.Value : null;
            if (entries != null && entries.Length < TotalFetchLimit && entries.Length >= rank)
            {
                totalKnownFor = session;
                knownTotal = entries.Length;
                ShowRank(rank, entries.Length);
            }
        }

        private static string ErrorMessage(ParticipantSession session)
        {
            var error = session?.LastSubmissionError;
            return error != null && !string.IsNullOrWhiteSpace(error.Message) ? error.Message : BackendErrorMessages.ServerUnreachable;
        }

        private void OnRetry()
        {
            retryButton.Interactable = false;
            if (Context.Run(SessionState.SubmissionFailed, Context.Session.RetrySubmission, "RetrySubmission"))
            {
                Context.Toast?.Show("Sonuç yeniden gönderiliyor", ToastKind.Info);
            }
            else
            {
                retryButton.Interactable = true;
            }
        }

        private void OnNewParticipant()
        {
            Context.Run(OperatorContext.CanReset, Context.Session.ResetForNextParticipant, "ResetForNextParticipant");
        }
    }
}
