using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MultiTravel.Core.Backend;
using MultiTravel.Core.Leaderboard;
using MultiTravel.Core.Utility;
using MultiTravel.Operator.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiTravel.Operator
{
    /// <summary>
    /// Toggleable drawer with the top 10 of the event leaderboard (<see cref="IBackendClient.GetLeaderboardAsync"/>).
    /// Columns: Sıra / Ad Soyad / Puan / Süre. Only the server's <c>display_name</c> is shown — never phone or e-mail.
    /// Rows are created once; a fetch only rewrites their texts. Stale responses are ignored (request version + lifetime token).
    /// </summary>
    public sealed class LeaderboardPanel
    {
        public const int RowCount = 10;

        private const float Width = 700f;
        private const float RankWidth = 80f;
        private const float ScoreWidth = 110f;
        private const float TimeWidth = 140f;
        private const string LogPrefix = "[MultiTravel.Operator] ";

        private readonly OperatorContext context;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly Row[] rows = new Row[RowCount];

        private RectTransform root;
        private TextMeshProUGUI statusLabel;
        private UiButton refreshButton;
        private int request;
        private bool inFlight;
        private bool disposed;

        public LeaderboardPanel(OperatorContext context)
        {
            this.context = context;
        }

        public bool IsOpen => root != null && root.gameObject.activeSelf;

        /// <summary>Builds the drawer at the right edge of <paramref name="contentArea"/> (hidden).</summary>
        public void Build(RectTransform contentArea)
        {
            var panel = UiFactory.CreateImage("LeaderboardPanel", contentArea, OperatorUiStyle.Card, true);
            root = panel.rectTransform;
            root.anchorMin = new Vector2(1f, 0f);
            root.anchorMax = new Vector2(1f, 1f);
            root.pivot = new Vector2(1f, 0.5f);
            root.offsetMin = new Vector2(-Width - 20f, 20f);
            root.offsetMax = new Vector2(-20f, -20f);
            var shadow = panel.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.18f);
            shadow.effectDistance = new Vector2(-4f, -4f);
            UiFactory.AddVerticalLayout(panel.gameObject, 8f, new RectOffset(28, 28, 24, 24), TextAnchor.UpperLeft, true, true, true, false);

            var titleRow = UiFactory.CreateRow(root, "TitleRow", 16f, TextAnchor.MiddleLeft, false);
            var title = UiFactory.CreateLabel(titleRow, "Title", "Liderlik Tablosu", OperatorUiStyle.FontHeading,
                OperatorUiStyle.Primary, FontStyles.Bold);
            UiFactory.MakeSingleLine(title);
            UiFactory.SetLayout(title, 0f, -1f, 1f, -1f);
            refreshButton = UiFactory.CreateButton(titleRow, "RefreshButton", "Yenile", ButtonStyle.Secondary, Refresh,
                44f, OperatorUiStyle.FontSmall, 140f);

            UiFactory.CreateLabel(root, "Subtitle", "İlk 10 · puan, ardından süreye göre", OperatorUiStyle.FontSmall,
                OperatorUiStyle.TextSecondary);

            CreateRowVisual(root, "HeaderRow", OperatorUiStyle.Lighten(OperatorUiStyle.Primary, 0.88f), out var headerRow);
            headerRow.Rank.text = "Sıra";
            headerRow.Name.text = "Ad Soyad";
            headerRow.Score.text = "Puan";
            headerRow.Time.text = "Süre";
            SetHeaderStyle(headerRow);

            for (int i = 0; i < RowCount; i++)
            {
                var color = i % 2 == 0 ? OperatorUiStyle.Card : OperatorUiStyle.RowAlternate;
                var visual = CreateRowVisual(root, "Row" + (i + 1).ToString(CultureInfo.InvariantCulture), color, out var row);
                rows[i] = row;
                visual.gameObject.SetActive(false);
            }

            statusLabel = UiFactory.CreateLabel(root, "Status", string.Empty, OperatorUiStyle.FontSmall,
                OperatorUiStyle.TextSecondary, FontStyles.Normal, TextAlignmentOptions.Center);

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
            Refresh();
        }

        public void Close()
        {
            if (root != null && root.gameObject.activeSelf)
            {
                root.gameObject.SetActive(false);
            }
        }

        /// <summary>Fetches the top 10 again (no-op while a fetch is running).</summary>
        public void Refresh()
        {
            if (disposed || root == null)
            {
                return;
            }

            if (!context.IsBound)
            {
                ShowStatus("Servisler hazır değil.", OperatorUiStyle.Danger);
                return;
            }

            if (context.Backend == null)
            {
                ShowStatus("Sunucu istemcisi bulunamadı.", OperatorUiStyle.Danger);
                return;
            }

            if (!context.Config.Backend.HasEndpoint)
            {
                ShowStatus("Sunucu yapılandırılmamış (adres, anahtar veya etkinlik kodu eksik).", OperatorUiStyle.Warning);
                return;
            }

            if (inFlight)
            {
                return;
            }

            int current = ++request;
            _ = FetchAsync(current, lifetime.Token);
        }

        /// <summary>Cancels in-flight requests; later continuations are ignored.</summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            lifetime.Cancel();
            lifetime.Dispose();
        }

        private async Task FetchAsync(int current, CancellationToken token)
        {
            inFlight = true;
            refreshButton.Interactable = false;
            ShowStatus("Yükleniyor…", OperatorUiStyle.TextSecondary);

            BackendResult<LeaderboardEntry[]> result;
            try
            {
                result = await context.Backend.GetLeaderboardAsync(RowCount, token);
            }
            catch (OperationCanceledException)
            {
                result = null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(LogPrefix + "Leaderboard fetch failed: " + ex.Message);
                result = BackendResult<LeaderboardEntry[]>.Failure(BackendError.Transport(ex.GetType().Name + ": " + ex.Message));
            }

            // Continuation guard: ignore results after destroy or when a newer request was issued.
            if (disposed || token.IsCancellationRequested || current != request)
            {
                return;
            }

            inFlight = false;
            refreshButton.Interactable = true;

            if (result == null)
            {
                ShowStatus("İstek iptal edildi.", OperatorUiStyle.TextSecondary);
                return;
            }

            if (!result.Ok)
            {
                if (result.Error != null)
                {
                    Debug.LogWarning(LogPrefix + "Leaderboard fetch failed: " + result.Error);
                }

                var message = result.Error != null ? result.Error.Message : BackendErrorMessages.ServerUnreachable;
                ShowStatus("Liderlik tablosu alınamadı: " + message, OperatorUiStyle.Danger);
                return;
            }

            var entries = result.Value ?? Array.Empty<LeaderboardEntry>();
            int shown = 0;
            for (int i = 0; i < RowCount; i++)
            {
                var row = rows[i];
                var entry = i < entries.Length ? entries[i] : null;
                if (entry == null)
                {
                    UiFactory.SetActive(row.Root, false);
                    continue;
                }

                row.Rank.SetText("{0}", entry.Rank);
                row.Name.text = string.IsNullOrWhiteSpace(entry.DisplayName) ? "—" : entry.DisplayName;
                row.Score.SetText("{0}", entry.Score);
                row.Time.text = TimeFormat.FormatTenths(entry.CompletionMs);
                UiFactory.SetActive(row.Root, true);
                shown++;
            }

            if (shown == 0)
            {
                ShowStatus("Henüz tamamlanmış oyun yok.", OperatorUiStyle.TextSecondary);
            }
            else
            {
                ShowStatus("Son güncelleme " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture), OperatorUiStyle.TextSecondary);
            }
        }

        private void ShowStatus(string text, Color color)
        {
            statusLabel.text = text;
            statusLabel.color = color;
        }

        private static Image CreateRowVisual(Transform parent, string name, Color background, out Row row)
        {
            var image = UiFactory.CreateImage(name, parent, background);
            UiFactory.AddHorizontalLayout(image.gameObject, 12f, new RectOffset(14, 14, 0, 0), TextAnchor.MiddleLeft, true, true, false, false);
            UiFactory.SetLayout(image, -1f, 50f, 1f, -1f, -1f, 50f);

            row = new Row
            {
                Root = image,
                Rank = CreateCell(image.rectTransform, "Rank", RankWidth, 0f, FontStyles.Bold, TextAlignmentOptions.Left),
                Name = CreateCell(image.rectTransform, "Name", 0f, 1f, FontStyles.Normal, TextAlignmentOptions.Left),
                Score = CreateCell(image.rectTransform, "Score", ScoreWidth, 0f, FontStyles.Bold, TextAlignmentOptions.Right),
                Time = CreateCell(image.rectTransform, "Time", TimeWidth, 0f, FontStyles.Normal, TextAlignmentOptions.Right)
            };
            return image;
        }

        private static TextMeshProUGUI CreateCell(Transform parent, string name, float width, float flexible, FontStyles style, TextAlignmentOptions alignment)
        {
            var label = UiFactory.CreateLabel(parent, name, string.Empty, OperatorUiStyle.FontBody - 2f, OperatorUiStyle.TextPrimary, style, alignment);
            UiFactory.MakeSingleLine(label);
            UiFactory.SetLayout(label, width, -1f, flexible, -1f, width);
            return label;
        }

        private static void SetHeaderStyle(Row row)
        {
            SetHeaderCell(row.Rank);
            SetHeaderCell(row.Name);
            SetHeaderCell(row.Score);
            SetHeaderCell(row.Time);
        }

        private static void SetHeaderCell(TextMeshProUGUI cell)
        {
            cell.fontSize = OperatorUiStyle.FontSmall;
            cell.fontStyle = FontStyles.Bold;
            cell.color = OperatorUiStyle.Primary;
        }

        private sealed class Row
        {
            public Image Root;
            public TextMeshProUGUI Rank;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Score;
            public TextMeshProUGUI Time;
        }
    }
}
