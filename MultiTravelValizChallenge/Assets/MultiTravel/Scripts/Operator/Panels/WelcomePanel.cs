using MultiTravel.Core.Session;
using MultiTravel.Operator.UI;
using TMPro;
using UnityEngine;

namespace MultiTravel.Operator.Panels
{
    /// <summary>Idle screen: configured welcome title / subtitle and "Başla" (Welcome → Registration).</summary>
    public sealed class WelcomePanel : OperatorPanel
    {
        private TextMeshProUGUI title;
        private TextMeshProUGUI subtitle;
        private UiButton startButton;

        public WelcomePanel(OperatorContext context) : base(context)
        {
        }

        public override bool HandlesState(SessionState state)
        {
            return state == SessionState.Welcome;
        }

        protected override void OnBuild(RectTransform root)
        {
            var card = UiFactory.CreateCard(root, "Card", 960f, 56, 24f);
            title = CreateHeading(card, string.Empty);
            subtitle = CreateBody(card, "Subtitle", string.Empty);
            CreateBody(card, "Hint", "Yeni bir katılımcıyı kaydetmek için Başla'ya basın.");
            UiFactory.CreateSpacer(card, 10f, 8f);
            startButton = UiFactory.CreateButton(card, "StartButton", "Başla", ButtonStyle.Primary, OnStart, 88f, 34f);
        }

        protected override void OnShow(SessionState state)
        {
            var texts = Context.Config.Texts;
            var welcome = string.IsNullOrWhiteSpace(texts.WelcomeTitle) ? Context.Config.Branding.ProductTitle : texts.WelcomeTitle;
            title.text = welcome;
            subtitle.text = texts.WelcomeSubtitle;
            UiFactory.SetActive(subtitle, !string.IsNullOrWhiteSpace(texts.WelcomeSubtitle));
            startButton.Interactable = true;
        }

        private void OnStart()
        {
            Context.Run(SessionState.Welcome, Context.Session.BeginRegistration, "BeginRegistration");
        }
    }
}
