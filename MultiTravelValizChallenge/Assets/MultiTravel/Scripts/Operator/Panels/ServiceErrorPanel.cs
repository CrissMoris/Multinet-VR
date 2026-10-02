using MultiTravel.Core.Session;
using MultiTravel.Operator.UI;
using TMPro;
using UnityEngine;

namespace MultiTravel.Operator.Panels
{
    /// <summary>
    /// Shown instead of the session panels while required services cannot be resolved from <c>AppServices</c>
    /// (e.g. the Main scene was opened without the Bootstrap scene). Never tied to a <see cref="SessionState"/>.
    /// </summary>
    public sealed class ServiceErrorPanel : OperatorPanel
    {
        private TextMeshProUGUI detailLabel;

        public ServiceErrorPanel(OperatorContext context) : base(context)
        {
        }

        public override bool HandlesState(SessionState state)
        {
            return false;
        }

        /// <summary>Sets the list of missing services (technical names, shown in small text).</summary>
        public void SetDetail(string detail)
        {
            if (detailLabel != null)
            {
                detailLabel.text = detail ?? string.Empty;
            }
        }

        protected override void OnBuild(RectTransform root)
        {
            var card = UiFactory.CreateCard(root, "Card", 920f, 48, 20f);
            var badgeRow = UiFactory.CreateRow(card, "BadgeRow", 0f, TextAnchor.MiddleCenter, false);
            CreateBadge(badgeRow, UiIcon.Server, OperatorUiStyle.Danger, 96f);
            var heading = CreateHeading(card, "Uygulama Servisleri Bulunamadı");
            heading.color = OperatorUiStyle.DangerText;
            CreateBody(card, "Message",
                "Operatör ekranı oyun servislerine bağlanamadı. Uygulamayı Bootstrap sahnesinden başlatın; " +
                "sorun devam ederse teknik ekibe haber verin. Servisler hazır olduğunda ekran kendiliğinden açılır.");
            detailLabel = UiFactory.CreateLabel(card, "Detail", string.Empty, OperatorUiStyle.FontLabel,
                OperatorUiStyle.TextMuted, FontStyles.Normal, TextAlignmentOptions.Center);
            var pillRow = UiFactory.CreateRow(card, "StatusRow", 0f, TextAnchor.MiddleCenter, false);
            var pill = UiPill.Create(pillRow, "Retrying", 32f, OperatorUiStyle.FontLabel);
            pill.Set("Yeniden bağlanılıyor…", OperatorUiStyle.Warning, false);
        }
    }
}
