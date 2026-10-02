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
    /// Top-10 table of the event leaderboard (<see cref="IBackendClient.GetLeaderboardAsync"/>): rank chip (gold / silver /
    /// bronze for the podium), name, score and time. Only the display name from the server is shown, never phone or e-mail.
    /// Rows are created once; a fetch only rewrites their texts. Stale responses are ignored (request version + lifetime token).
    /// Used by the drawer (<see cref="LeaderboardPanel"/>) and by the welcome screen (auto refresh every 30 s).
    /// </summary>
    public sealed class LeaderboardView
    {
        public const int RowCount = 10;

        private const float RowHeight = 48f;
        private const string LogPrefix = "[MultiTravel.Operator] ";

        private static readonly Color Gold = new Color32(0xF5, 0xC5, 0x42, 255);
        private static readonly Color Silver = new Color32(0xC3, 0xCC, 0xD9, 255);
        private static readonly Color Bronze = new Color32(0xD9, 0x8C, 0x5A, 255);

        private readonly OperatorContext context;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly Row[] rows = new Row[RowCount];

        private RectTransform root;
        private TextMeshProUGUI statusLabel;
        private int request;
        private bool inFlight;
        private bool disposed;
        private float nextAutoRefreshAt;

        public LeaderboardView(OperatorContext context)
        {
            this.context = context;
        }

        /// <summary>When above zero, <see cref="Tick"/> refreshes automatically at this interval (seconds).</summary>
        public float AutoRefreshSeconds { get; set; }

        /// <summary>True while a fetch is running.</summary>
        public bool IsLoading => inFlight;

        /// <summary>Raised when the loading state changes (the drawer disables its refresh button).</summary>
        public event Action<bool> LoadingChanged;

        /// <summary>Builds the table inside <paramref name="parent"/> (a vertical layout container).</summary>
        public void Build(Transform parent)
        {
            root = UiFactory.CreateColumn(parent, "Leaderboard", 4f);

            var header = UiFactory.CreateRect("HeaderRow", root);
            UiFactory.AddHorizontalLayout(header.gameObject, 12f, new RectOffset(14, 14, 0, 0), TextAnchor.MiddleLeft, true, true, false, false);
            UiFactory.SetLayout(header, -1f, 32f, 1f, 0f, -1f, 32f);
            CreateHeaderCell(header, "#", 32f, 0f, TextAlignmentOptions.Center);
            CreateHeaderCell(header, "Ad Soyad", 0f, 1f, TextAlignmentOptions.Left);
            CreateHeaderCell(header, "Puan", 64f, 0f, TextAlignmentOptions.Right);
            CreateHeaderCell(header, "Süre", 92f, 0f, TextAlignmentOptions.Right);

            for (int i = 0; i < RowCount; i++)
            {
                rows[i] = CreateRow(root, i);
                rows[i].Root.gameObject.SetActive(false);
            }

            statusLabel = UiFactory.CreateLabel(root, "Status", string.Empty, OperatorUiStyle.FontLabel, OperatorUiStyle.TextMuted,
                FontStyles.Normal, TextAlignmentOptions.Center);
        }

        /// <summary>Fetches the top 10 again (no-op while a fetch is running).</summary>
        public void Refresh()
        {
            if (disposed || root == null)
            {
                return;
            }

            if (AutoRefreshSeconds > 0f)
            {
                nextAutoRefreshAt = Time.realtimeSinceStartup + AutoRefreshSeconds;
            }

            if (!context.IsBound)
            {
                ShowStatus("Servisler hazır değil.", OperatorUiStyle.DangerText);
                return;
            }

            if (context.Backend == null)
            {
                ShowStatus("Sunucu istemcisi bulunamadı.", OperatorUiStyle.DangerText);
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

        /// <summary>Call at 10 Hz while the view is visible: runs the periodic refresh.</summary>
        public void Tick(float realtimeNow)
        {
            if (AutoRefreshSeconds > 0f && !inFlight && realtimeNow >= nextAutoRefreshAt)
            {
                Refresh();
            }
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
            SetLoading(true);
            ShowStatus("Yükleniyor…", OperatorUiStyle.TextMuted);

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

            SetLoading(false);

            if (result == null)
            {
                ShowStatus("İstek iptal edildi.", OperatorUiStyle.TextMuted);
                return;
            }

            if (!result.Ok)
            {
                if (result.Error != null)
                {
                    Debug.LogWarning(LogPrefix + "Leaderboard fetch failed: " + result.Error);
                }

                var message = result.Error != null ? result.Error.Message : BackendErrorMessages.ServerUnreachable;
                ShowStatus("Liderlik tablosu alınamadı: " + message, OperatorUiStyle.DangerText);
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
                SetRankColor(row, entry.Rank);
                row.Name.text = string.IsNullOrWhiteSpace(entry.DisplayName) ? "—" : entry.DisplayName;
                row.Score.SetText("{0}", entry.Score);
                row.Time.text = TimeFormat.FormatTenths(entry.CompletionMs);
                UiFactory.SetActive(row.Root, true);
                shown++;
            }

            if (shown == 0)
            {
                ShowStatus("Henüz tamamlanmış oyun yok.", OperatorUiStyle.TextMuted);
            }
            else
            {
                ShowStatus("Son güncelleme " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture), OperatorUiStyle.TextMuted);
            }
        }

        private void SetLoading(bool loading)
        {
            if (inFlight == loading)
            {
                return;
            }

            inFlight = loading;
            LoadingChanged?.Invoke(loading);
        }

        private void ShowStatus(string text, Color color)
        {
            statusLabel.text = text;
            statusLabel.color = color;
        }

        private static void SetRankColor(Row row, int rank)
        {
            Color chip = rank == 1 ? Gold : rank == 2 ? Silver : rank == 3 ? Bronze : OperatorUiStyle.Elevated;
            row.Chip.color = chip;
            row.Rank.color = rank >= 1 && rank <= 3 ? OperatorUiStyle.TextOnBright : OperatorUiStyle.TextPrimary;
        }

        private static Row CreateRow(Transform parent, int index)
        {
            var fill = index % 2 == 0 ? OperatorUiStyle.WithAlpha(OperatorUiStyle.Elevated, 0.55f) : OperatorUiStyle.WithAlpha(OperatorUiStyle.Elevated, 0.2f);
            var image = UiFactory.CreateRounded("Row" + (index + 1).ToString(CultureInfo.InvariantCulture), parent, fill, 10, false);
            UiFactory.AddHorizontalLayout(image.gameObject, 12f, new RectOffset(14, 14, 0, 0), TextAnchor.MiddleLeft, true, true, false, false);
            UiFactory.SetLayout(image, -1f, RowHeight, 1f, 0f, -1f, RowHeight);

            var row = new Row { Root = image.rectTransform };
            var chipHolder = UiFactory.CreateRect("RankChip", image.rectTransform);
            UiFactory.SetLayout(chipHolder, 32f, 32f, 0f, 0f, 32f, 32f);
            row.Chip = UiFactory.CreateImage("Chip", chipHolder, OperatorUiStyle.Elevated, false);
            row.Chip.sprite = UiSprites.Circle(32);
            UiFactory.Stretch(row.Chip.rectTransform);
            row.Rank = UiFactory.CreateLabel(chipHolder, "Rank", string.Empty, OperatorUiStyle.FontLabel, OperatorUiStyle.TextPrimary, FontStyles.Bold,
                TextAlignmentOptions.Center);
            UiFactory.Stretch(row.Rank.rectTransform);

            row.Name = CreateCell(image.rectTransform, "Name", 0f, 1f, FontStyles.Normal, TextAlignmentOptions.Left, OperatorUiStyle.TextPrimary);
            row.Score = CreateCell(image.rectTransform, "Score", 64f, 0f, FontStyles.Bold, TextAlignmentOptions.Right, OperatorUiStyle.TextPrimary);
            row.Time = CreateCell(image.rectTransform, "Time", 92f, 0f, FontStyles.Normal, TextAlignmentOptions.Right, OperatorUiStyle.TextSecondary);
            return row;
        }

        private static TextMeshProUGUI CreateCell(Transform parent, string name, float width, float flexible, FontStyles style,
            TextAlignmentOptions alignment, Color color)
        {
            var label = UiFactory.CreateLabel(parent, name, string.Empty, OperatorUiStyle.FontBody, color, style, alignment);
            UiFactory.MakeSingleLine(label);
            UiFactory.SetLayout(label, width, -1f, flexible, -1f, width);
            return label;
        }

        private static void CreateHeaderCell(Transform parent, string text, float width, float flexible, TextAlignmentOptions alignment)
        {
            var label = UiFactory.CreateLabel(parent, text, text, OperatorUiStyle.FontCaption, OperatorUiStyle.TextMuted, FontStyles.Bold, alignment);
            UiFactory.MakeSingleLine(label);
            UiFactory.SetLayout(label, width, -1f, flexible, -1f, width);
        }

        private sealed class Row
        {
            public RectTransform Root;
            public Image Chip;
            public TextMeshProUGUI Rank;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Score;
            public TextMeshProUGUI Time;
        }
    }
}
