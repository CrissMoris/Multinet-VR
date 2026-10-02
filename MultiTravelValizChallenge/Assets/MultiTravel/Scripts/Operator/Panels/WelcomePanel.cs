using MultiTravel.Core.Session;
using MultiTravel.Operator.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MultiTravel.Operator.Panels
{
    /// <summary>
    /// Attract / idle screen over the spectator view: big brand lockup, configured subtitle and scenario, the primary
    /// "Yeni Katılımcı" action (Welcome → Registration; Enter works too) and the top-10 leaderboard that refreshes every 30 s.
    /// </summary>
    public sealed class WelcomePanel : OperatorPanel
    {
        private const float LeaderboardRefreshSeconds = 30f;

        private readonly LeaderboardView board;
        private TextMeshProUGUI title;
        private TextMeshProUGUI subtitle;
        private TextMeshProUGUI scenario;
        private RectTransform scenarioCard;
        private UiButton startButton;
        private UnityEngine.UI.Image logo;
        private GameObject brandMark;

        public WelcomePanel(OperatorContext context) : base(context)
        {
            board = new LeaderboardView(context);
        }

        public override bool HandlesState(SessionState state)
        {
            return state == SessionState.Welcome;
        }

        protected override void OnBuild(RectTransform root)
        {
            var row = UiFactory.CreateRect("Row", root);
            UiFactory.Stretch(row);
            UiFactory.AddHorizontalLayout(row.gameObject, 32f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleCenter, true, true, false, false);

            // ----- brand lockup -----
            var left = UiFactory.CreateColumn(row, "Lockup", 20f, TextAnchor.UpperLeft);
            UiFactory.SetLayout(left, 560f, -1f, 0f, 0f, 560f);

            var markRow = UiFactory.CreateRow(left, "MarkRow", 0f, TextAnchor.MiddleLeft, false);
            logo = UiFactory.CreateImage("Logo", markRow, Color.white, false);
            logo.preserveAspect = true;
            UiFactory.SetLayout(logo, 360f, 100f, 0f, 0f, 360f, 100f);
            logo.gameObject.SetActive(false);
            var mark = UiFactory.CreateRounded("BrandMark", markRow, OperatorUiStyle.Primary, 24, false);
            brandMark = mark.gameObject;
            UiFactory.SetLayout(mark, 96f, 96f, 0f, 0f, 96f, 96f);
            var markIcon = UiFactory.CreateIcon(mark.rectTransform, "Icon", UiIcon.Suitcase, 56f, Color.white);
            UiFactory.IgnoreLayout(markIcon);
            UiFactory.AnchorCenter(markIcon.rectTransform, 56f, 56f);

            title = UiFactory.CreateLabel(left, "Title", string.Empty, OperatorUiStyle.FontDisplay, OperatorUiStyle.TextPrimary, FontStyles.Bold,
                TextAlignmentOptions.TopLeft);
            subtitle = UiFactory.CreateLabel(left, "Subtitle", string.Empty, OperatorUiStyle.FontBody, OperatorUiStyle.TextSecondary,
                FontStyles.Normal, TextAlignmentOptions.TopLeft);

            scenarioCard = UiFactory.CreateSurface(left, "ScenarioCard", OperatorUiStyle.Elevated, OperatorUiStyle.RadiusCard, false, true, out _);
            UiFactory.AddHorizontalLayout(scenarioCard.gameObject, 16f, new RectOffset(20, 20, 18, 18), TextAnchor.UpperLeft, true, true, false, false);
            UiFactory.CreateIcon(scenarioCard, "Icon", UiIcon.Suitcase, 28f, OperatorUiStyle.Accent);
            scenario = UiFactory.CreateLabel(scenarioCard, "Scenario", string.Empty, OperatorUiStyle.FontBody, OperatorUiStyle.TextPrimary,
                FontStyles.Normal, TextAlignmentOptions.TopLeft);
            UiFactory.SetLayout(scenario, 0f, -1f, 1f, -1f);

            UiFactory.CreateSpacer(left, 10f, 8f);
            startButton = UiFactory.CreateButton(left, "StartButton", "Yeni Katılımcı", ButtonStyle.Primary, OnStart,
                OperatorUiStyle.ButtonHeightHero, 28f, -1f, UiIcon.Play);
            var hint = UiFactory.CreateLabel(left, "Hint", "Enter tuşu ile de başlatabilirsiniz.", OperatorUiStyle.FontLabel, OperatorUiStyle.TextMuted,
                FontStyles.Normal, TextAlignmentOptions.Left);
            UiFactory.MakeSingleLine(hint);

            // ----- leaderboard -----
            var card = UiFactory.CreateSurface(row, "BoardCard", OperatorUiStyle.Card, OperatorUiStyle.RadiusCard, true, true, out _);
            UiFactory.SetLayout(card, 540f, -1f, 0f, 0f, 540f);
            UiFactory.AddVerticalLayout(card.gameObject, 12f, new RectOffset(24, 24, 24, 24), TextAnchor.UpperLeft, true, true, true, false);
            var header = UiFactory.CreateRow(card, "Header", 12f, TextAnchor.MiddleLeft, false);
            UiFactory.CreateIcon(header, "Icon", UiIcon.Trophy, 32f, OperatorUiStyle.Warning);
            var headerTitle = UiFactory.CreateLabel(header, "Title", "Liderlik Tablosu", OperatorUiStyle.FontHeading, OperatorUiStyle.TextPrimary, FontStyles.Bold);
            UiFactory.MakeSingleLine(headerTitle);
            UiFactory.CreateLabel(card, "Subtitle", "İlk 10 · her 30 saniyede güncellenir", OperatorUiStyle.FontLabel, OperatorUiStyle.TextMuted);
            UiFactory.CreateDivider(card);
            board.AutoRefreshSeconds = LeaderboardRefreshSeconds;
            board.Build(card);
        }

        protected override void OnShow(SessionState state)
        {
            var config = Context.Config;
            var texts = config.Texts;
            var sprite = config.Branding.LogoSprite;
            if (sprite != null)
            {
                logo.sprite = sprite;
                logo.gameObject.SetActive(true);
                brandMark.SetActive(false);
            }

            var welcome = string.IsNullOrWhiteSpace(texts.WelcomeTitle) ? config.Branding.ProductTitle : texts.WelcomeTitle;
            title.text = welcome;
            subtitle.text = texts.WelcomeSubtitle;
            UiFactory.SetActive(subtitle, !string.IsNullOrWhiteSpace(texts.WelcomeSubtitle));
            scenario.text = texts.ScenarioText;
            UiFactory.SetActive(scenarioCard, !string.IsNullOrWhiteSpace(texts.ScenarioText));
            startButton.Interactable = true;
            board.Refresh();
        }

        public override void Refresh()
        {
            board.Tick(Time.realtimeSinceStartup);
        }

        public override void Tick()
        {
            if (!CanUseShortcuts())
            {
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
            {
                OnStart();
            }
        }

        public override void Dispose()
        {
            board.Dispose();
        }

        private void OnStart()
        {
            Context.Run(SessionState.Welcome, Context.Session.BeginRegistration, "BeginRegistration");
        }
    }
}
