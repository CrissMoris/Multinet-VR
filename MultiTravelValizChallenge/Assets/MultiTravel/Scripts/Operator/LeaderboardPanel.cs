using MultiTravel.Operator.UI;
using TMPro;
using UnityEngine;

namespace MultiTravel.Operator
{
    /// <summary>
    /// Toggleable drawer with the top 10 of the event leaderboard, slid in over the right side of the centre area. Opening
    /// triggers a refresh; "Yenile" fetches again. The table itself is a <see cref="LeaderboardView"/>.
    /// </summary>
    public sealed class LeaderboardPanel
    {
        private const float Width = 580f;

        private readonly LeaderboardView view;
        private RectTransform root;
        private CanvasGroup group;
        private UiButton refreshButton;

        public LeaderboardPanel(OperatorContext context)
        {
            view = new LeaderboardView(context);
        }

        public bool IsOpen => root != null && root.gameObject.activeSelf;

        /// <summary>Builds the drawer at the right edge of <paramref name="contentArea"/> (hidden).</summary>
        public void Build(RectTransform contentArea)
        {
            root = UiFactory.CreateSurface(contentArea, "LeaderboardDrawer", OperatorUiStyle.Card, OperatorUiStyle.RadiusCard, true, true, out _);
            root.anchorMin = new Vector2(1f, 0f);
            root.anchorMax = new Vector2(1f, 1f);
            root.pivot = new Vector2(1f, 0.5f);
            root.offsetMin = new Vector2(-Width, 0f);
            root.offsetMax = Vector2.zero;
            group = root.gameObject.AddComponent<CanvasGroup>();
            UiFactory.AddVerticalLayout(root.gameObject, 12f, new RectOffset(24, 24, 24, 24), TextAnchor.UpperLeft, true, true, true, false);

            var titleRow = UiFactory.CreateRow(root, "TitleRow", 12f, TextAnchor.MiddleLeft, false);
            UiFactory.CreateIcon(titleRow, "Icon", UiIcon.Trophy, 32f, OperatorUiStyle.Warning);
            var title = UiFactory.CreateLabel(titleRow, "Title", "Liderlik Tablosu", OperatorUiStyle.FontHeading, OperatorUiStyle.TextPrimary, FontStyles.Bold);
            UiFactory.MakeSingleLine(title);
            UiFactory.SetLayout(title, 0f, -1f, 1f, -1f);
            refreshButton = UiFactory.CreateButton(titleRow, "RefreshButton", "Yenile", ButtonStyle.Secondary, Refresh,
                OperatorUiStyle.ButtonHeightSmall, OperatorUiStyle.FontLabel, -1f, UiIcon.Refresh);

            UiFactory.CreateLabel(root, "Subtitle", "İlk 10 · önce puan, sonra süreye göre", OperatorUiStyle.FontLabel, OperatorUiStyle.TextMuted);
            UiFactory.CreateDivider(root);
            view.Build(root);
            view.LoadingChanged += loading => refreshButton.Interactable = !loading;

            root.gameObject.SetActive(false);
        }

        /// <summary>Opens or closes the drawer; opening triggers a refresh. Returns the new state.</summary>
        public bool Toggle()
        {
            if (IsOpen)
            {
                Close();
                return false;
            }

            Open();
            return true;
        }

        public void Open()
        {
            if (root == null)
            {
                return;
            }

            root.gameObject.SetActive(true);
            root.SetAsLastSibling();
            UiTween.PanelIn(group, root, new Vector2(32f, 0f), UiTween.Slow);
            Refresh();
        }

        public void Close()
        {
            if (root != null && root.gameObject.activeSelf)
            {
                UiTween.Cancel(group);
                group.alpha = 1f;
                root.anchoredPosition = Vector2.zero;
                root.gameObject.SetActive(false);
            }
        }

        /// <summary>Fetches the top 10 again (no-op while a fetch is running).</summary>
        public void Refresh()
        {
            view.Refresh();
        }

        /// <summary>Refreshes the table when the drawer is open (used after a result was submitted).</summary>
        public void RefreshIfOpen()
        {
            if (IsOpen)
            {
                view.Refresh();
            }
        }

        /// <summary>Cancels in-flight requests; later continuations are ignored.</summary>
        public void Dispose()
        {
            view.Dispose();
        }
    }
}
