using MultiTravel.Core.Session;
using MultiTravel.Operator.UI;
using TMPro;
using UnityEngine;

namespace MultiTravel.Operator.Panels
{
    /// <summary>
    /// Loading (gameplay prepares the item set) and Countdown (VR countdown): spectator view with the remaining countdown
    /// seconds on top, a warning when loading takes unusually long and the cancel action.
    /// </summary>
    public sealed class LoadingPanel : OperatorPanel
    {
        private const float SlowLoadingWarningSeconds = 15f;

        private TextMeshProUGUI heading;
        private TextMeshProUGUI countdownLabel;
        private TextMeshProUGUI description;
        private TextMeshProUGUI slowWarning;
        private RectTransform countdownOverlay;

        private SessionState shownState;
        private float stateEnteredAt;
        private int lastRemaining = -1;
        private bool slowWarningShown;

        public LoadingPanel(OperatorContext context) : base(context)
        {
        }

        public override bool HandlesState(SessionState state)
        {
            return state == SessionState.Loading || state == SessionState.Countdown;
        }

        protected override void OnBuild(RectTransform root)
        {
            var column = UiFactory.CreateRect("Column", root);
            UiFactory.Stretch(column, 40f, 0f, 40f, 0f);
            UiFactory.AddVerticalLayout(column.gameObject, 16f, new RectOffset(0, 0, 0, 0), TextAnchor.UpperCenter, true, true, true, false);

            heading = CreateHeading(column, string.Empty);

            var frame = Context.Feed.CreateFrame(column, "SpectatorFrame", out var overlay);
            countdownOverlay = UiFactory.CreateRect("CountdownOverlay", overlay);
            UiFactory.Stretch(countdownOverlay);
            var dim = UiFactory.CreateImage("Dim", countdownOverlay, OperatorUiStyle.WithAlpha(OperatorUiStyle.Background, 0.55f), false);
            UiFactory.Stretch(dim.rectTransform);
            countdownLabel = UiFactory.CreateLabel(countdownOverlay, "Countdown", string.Empty, OperatorUiStyle.FontHero, Color.white,
                FontStyles.Bold, TextAlignmentOptions.Center);
            countdownLabel.richText = true;
            UiFactory.Stretch(countdownLabel.rectTransform);
            _ = frame;

            description = CreateBody(column, "Description", string.Empty);
            slowWarning = UiFactory.CreateLabel(column, "SlowWarning",
                "Hazırlık beklenenden uzun sürüyor. VR uygulamasını kontrol edin; gerekirse oturumu iptal edin.",
                OperatorUiStyle.FontLabel, OperatorUiStyle.DangerText, FontStyles.Bold, TextAlignmentOptions.Center);
            slowWarning.gameObject.SetActive(false);

            var buttons = UiFactory.CreateRow(column, "Buttons", 16f, TextAnchor.MiddleCenter, false);
            UiFactory.CreateButton(buttons, "CancelButton", "Oturumu İptal Et", ButtonStyle.Ghost, OnCancelRequested,
                OperatorUiStyle.ButtonHeight, OperatorUiStyle.FontBody, 280f);
        }

        protected override void OnShow(SessionState state)
        {
            if (state != shownState)
            {
                stateEnteredAt = Time.realtimeSinceStartup;
            }

            shownState = state;
            CloseConfirm();
            lastRemaining = -1;
            slowWarningShown = false;
            UiFactory.SetActive(slowWarning, false);

            if (state == SessionState.Countdown)
            {
                heading.text = "Geri Sayım";
                description.text = "Katılımcı geri sayımı VR'da görüyor. Sayım bitince oyun ve süre başlar.";
                UiFactory.SetActive(countdownOverlay, true);
            }
            else
            {
                heading.text = "Oyun Hazırlanıyor…";
                description.text = "Ürünler ve valiz hazırlanıyor, lütfen bekleyin.";
                UiFactory.SetActive(countdownOverlay, false);
            }
        }

        protected override void OnHide()
        {
            CloseConfirm();
            shownState = SessionState.Welcome;
        }

        public override void Refresh()
        {
            float elapsed = Time.realtimeSinceStartup - stateEnteredAt;
            if (shownState == SessionState.Countdown)
            {
                int remaining = Mathf.Max(0, Mathf.CeilToInt(Context.Session.CountdownSeconds - elapsed));
                if (remaining != lastRemaining)
                {
                    lastRemaining = remaining;
                    countdownLabel.SetText(OperatorUiStyle.TabularOpen + "{0}" + OperatorUiStyle.TabularClose, remaining);
                    var rect = countdownLabel.rectTransform;
                    rect.localScale = new Vector3(1.25f, 1.25f, 1f);
                    UiTween.ScaleTo(rect, 1f, UiTween.Normal);
                }
            }
            else if (shownState == SessionState.Loading && !slowWarningShown && elapsed >= SlowLoadingWarningSeconds)
            {
                slowWarningShown = true;
                UiFactory.SetActive(slowWarning, true);
            }
        }

        private static bool IsLoadingState(SessionState state)
        {
            return state == SessionState.Loading || state == SessionState.Countdown;
        }

        private void OnCancelRequested()
        {
            Confirm("Oturum iptal edilsin mi?", "Bu katılımcı için sonuç kaydedilmeyecek.", "Evet, iptal et", ButtonStyle.Danger, CancelSession);
        }

        private void CancelSession()
        {
            Context.Run(IsLoadingState, Context.Session.AbandonSession, "AbandonSession");
        }
    }
}
