using MultiTravel.Core.Session;
using MultiTravel.Operator.UI;
using TMPro;
using UnityEngine;

namespace MultiTravel.Operator.Panels
{
    /// <summary>
    /// Overlay for Loading (gameplay prepares the item set) and Countdown (VR countdown). Shows the remaining countdown
    /// seconds, warns when loading takes unusually long and lets the operator cancel the session.
    /// </summary>
    public sealed class LoadingPanel : OperatorPanel
    {
        private const float SlowLoadingWarningSeconds = 15f;

        private TextMeshProUGUI heading;
        private TextMeshProUGUI countdownLabel;
        private TextMeshProUGUI description;
        private TextMeshProUGUI slowWarning;
        private ConfirmPrompt cancelPrompt;

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
            var overlay = UiFactory.CreateImage("Overlay", root, OperatorUiStyle.Overlay, true);
            UiFactory.Stretch(overlay.rectTransform);

            var card = UiFactory.CreateCard(root, "Card", 900f, 48, 18f);
            heading = CreateHeading(card, string.Empty);
            countdownLabel = UiFactory.CreateLabel(card, "Countdown", string.Empty, OperatorUiStyle.FontHuge,
                OperatorUiStyle.Warning, FontStyles.Bold, TextAlignmentOptions.Center);
            description = CreateBody(card, "Description", string.Empty);
            slowWarning = UiFactory.CreateLabel(card, "SlowWarning",
                "Hazırlık beklenenden uzun sürüyor. VR uygulamasını kontrol edin; gerekirse oturumu iptal edin.",
                OperatorUiStyle.FontSmall, OperatorUiStyle.Danger, FontStyles.Bold, TextAlignmentOptions.Center);
            slowWarning.gameObject.SetActive(false);

            cancelPrompt = new ConfirmPrompt(card, "CancelPrompt");
            var buttons = UiFactory.CreateRow(card, "Buttons", 20f, TextAnchor.MiddleCenter, false);
            UiFactory.CreateButton(buttons, "CancelButton", "Oturumu İptal Et", ButtonStyle.Secondary, OnCancelRequested,
                OperatorUiStyle.ButtonHeight, OperatorUiStyle.FontButton, 300f);
        }

        protected override void OnShow(SessionState state)
        {
            if (state != shownState)
            {
                stateEnteredAt = Time.realtimeSinceStartup;
            }

            shownState = state;
            cancelPrompt.Close();
            lastRemaining = -1;
            slowWarningShown = false;
            UiFactory.SetActive(slowWarning, false);

            if (state == SessionState.Countdown)
            {
                heading.text = "Geri Sayım";
                description.text = "Katılımcı geri sayımı VR'da görüyor. Sayım bitince oyun ve süre başlar.";
                UiFactory.SetActive(countdownLabel, true);
            }
            else
            {
                heading.text = "Oyun Hazırlanıyor…";
                description.text = "Ürünler ve valiz hazırlanıyor, lütfen bekleyin.";
                UiFactory.SetActive(countdownLabel, false);
            }
        }

        protected override void OnHide()
        {
            cancelPrompt.Close();
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
                    countdownLabel.SetText("{0}", remaining);
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
            cancelPrompt.Ask("Oturum iptal edilsin mi? Bu katılımcı için sonuç kaydedilmeyecek.", "Evet, iptal et", ButtonStyle.Danger, CancelSession);
        }

        private void CancelSession()
        {
            Context.Run(IsLoadingState, Context.Session.AbandonSession, "AbandonSession");
        }
    }
}
