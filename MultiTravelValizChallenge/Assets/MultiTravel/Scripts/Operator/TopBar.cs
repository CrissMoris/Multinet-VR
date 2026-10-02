using System;
using MultiTravel.Operator.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiTravel.Operator
{
    /// <summary>
    /// 72 px top bar: brand mark + wordmark, station chip, "Liderlik Tablosu" toggle and the app version.
    /// </summary>
    public sealed class TopBar
    {
        public const float Height = OperatorUiStyle.TopBarHeight;

        private const string DefaultTitle = "MultiTravel Valiz Challenge";

        private readonly OperatorContext context;
        private readonly Action onLeaderboardToggle;
        private TextMeshProUGUI titleLabel;
        private UnityEngine.UI.Image logoImage;
        private GameObject brandMark;
        private UiPill stationChip;
        private UiButton leaderboardButton;

        public TopBar(OperatorContext context, Action onLeaderboardToggle)
        {
            this.context = context;
            this.onLeaderboardToggle = onLeaderboardToggle;
        }

        public void Build(RectTransform canvasRoot)
        {
            var bar = UiFactory.CreateImage("TopBar", canvasRoot, OperatorUiStyle.Card, true);
            UiFactory.AnchorTop(bar.rectTransform, Height);
            UiFactory.AddHorizontalLayout(bar.gameObject, 16f, new RectOffset(24, 24, 0, 0), TextAnchor.MiddleLeft, true, true, false, false);

            var line = UiFactory.CreateImage("BottomLine", bar.rectTransform, OperatorUiStyle.WithAlpha(OperatorUiStyle.Border, 0.6f), false);
            UiFactory.IgnoreLayout(line);
            UiFactory.AnchorBottom(line.rectTransform, 1f);

            // corporate logo (shown once the config provides a sprite) or the generic brand mark
            logoImage = UiFactory.CreateImage("Logo", bar.rectTransform, Color.white, false);
            logoImage.preserveAspect = true;
            UiFactory.SetLayout(logoImage, 168f, 46f, 0f, 0f, 168f, 46f);
            logoImage.gameObject.SetActive(false);

            var mark = UiFactory.CreateRounded("BrandMark", bar.rectTransform, OperatorUiStyle.Primary, 10, false);
            brandMark = mark.gameObject;
            UiFactory.SetLayout(mark, 40f, 40f, 0f, 0f, 40f, 40f);
            var markIcon = UiFactory.CreateIcon(mark.rectTransform, "Icon", UiIcon.Suitcase, 24f, Color.white);
            UiFactory.IgnoreLayout(markIcon);
            UiFactory.AnchorCenter(markIcon.rectTransform, 24f, 24f);

            titleLabel = UiFactory.CreateLabel(bar.rectTransform, "Wordmark", DefaultTitle, OperatorUiStyle.FontHeading,
                OperatorUiStyle.TextPrimary, FontStyles.Bold, TextAlignmentOptions.Left);
            UiFactory.MakeSingleLine(titleLabel);
            UiFactory.SetLayout(titleLabel, -1f, -1f, 0f, -1f);

            var role = UiFactory.CreateLabel(bar.rectTransform, "Role", "Operatör Paneli", OperatorUiStyle.FontLabel,
                OperatorUiStyle.TextMuted, FontStyles.Normal, TextAlignmentOptions.Left);
            UiFactory.MakeSingleLine(role);
            UiFactory.SetLayout(role, -1f, -1f, 0f, -1f);

            stationChip = UiPill.Create(bar.rectTransform, "StationChip", 32f, OperatorUiStyle.FontLabel);
            stationChip.Set("İstasyon —", OperatorUiStyle.TextMuted, false);

            UiFactory.CreateFlexibleSpace(bar.rectTransform);

            leaderboardButton = UiFactory.CreateButton(bar.rectTransform, "LeaderboardToggle", "Liderlik Tablosu", ButtonStyle.Secondary,
                () => onLeaderboardToggle?.Invoke(), OperatorUiStyle.ButtonHeightSmall + 8f, OperatorUiStyle.FontLabel, -1f, UiIcon.Trophy);

            var version = UiFactory.CreateLabel(bar.rectTransform, "Version", "Sürüm " + Application.version, OperatorUiStyle.FontLabel,
                OperatorUiStyle.TextMuted, FontStyles.Normal, TextAlignmentOptions.Right);
            UiFactory.MakeSingleLine(version);
            UiFactory.SetLayout(version, -1f, -1f, 0f, -1f);
        }

        /// <summary>Applies config-derived texts once services are bound.</summary>
        public void OnBound()
        {
            var config = context.Config;
            var title = config.Branding.ProductTitle;
            titleLabel.text = string.IsNullOrWhiteSpace(title) ? DefaultTitle : title;
            var logo = config.Branding.LogoSprite;
            if (logo != null)
            {
                logoImage.sprite = logo;
                logoImage.gameObject.SetActive(true);
                brandMark.SetActive(false);
                titleLabel.text = "Valiz Challenge";
                titleLabel.color = OperatorUiStyle.TextSecondary;
            }
            var station = config.Backend.StationId;
            stationChip.Set("İstasyon " + (string.IsNullOrWhiteSpace(station) ? "—" : station), OperatorUiStyle.Accent, true);
        }

        /// <summary>Reflects the leaderboard drawer state on the toggle button.</summary>
        public void SetLeaderboardOpen(bool open)
        {
            leaderboardButton.SetLabel(open ? "Tabloyu Kapat" : "Liderlik Tablosu");
            leaderboardButton.SetIcon(open ? UiIcon.Close : UiIcon.Trophy);
            leaderboardButton.SetStyle(open ? ButtonStyle.Primary : ButtonStyle.Secondary);
        }
    }
}
