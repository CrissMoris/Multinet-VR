using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MultiTravel.Core.Backend;
using MultiTravel.Operator.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiTravel.Operator
{
    /// <summary>
    /// Always-visible header + status strip: product title, "Liderlik Tablosu" toggle, app version, VR status (dot, text,
    /// "Yeniden Dene"), server status via <see cref="IBackendClient.PingAsync"/> every 30 s and on demand (dot, text, last
    /// check time, "Kontrol Et"), station, event and pending submission count. Refreshed at 10 Hz by
    /// <see cref="OperatorScreen"/>; the periodic refresh is allocation-free unless a value changed.
    /// </summary>
    public sealed class StatusBar
    {
        public const float HeaderHeight = 72f;
        public const float StripHeight = 64f;
        public const float TotalHeight = HeaderHeight + StripHeight;

        /// <summary>Interval of the automatic server check.</summary>
        public const float PingIntervalSeconds = 30f;

        private const string LogPrefix = "[MultiTravel.Operator] ";

        private enum XrIndicator
        {
            None = -1,
            Unknown,
            Ready,
            NoHeadset,
            NotRunning
        }

        private enum ServerIndicator
        {
            Unknown,
            Checking,
            Connected,
            Disconnected,
            NotConfigured
        }

        private readonly OperatorContext context;
        private readonly Action onLeaderboardToggle;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();

        private TextMeshProUGUI titleLabel;
        private UiButton leaderboardButton;
        private Image xrDot;
        private TextMeshProUGUI xrLabel;
        private UiButton xrRetryButton;
        private Image serverDot;
        private TextMeshProUGUI serverLabel;
        private TextMeshProUGUI serverDetailLabel;
        private UiButton serverCheckButton;
        private TextMeshProUGUI stationLabel;
        private TextMeshProUGUI eventLabel;
        private TextMeshProUGUI pendingLabel;

        private XrIndicator shownXr = XrIndicator.None;
        private int shownPending = int.MinValue;
        private int pingRequest;
        private bool pingInFlight;
        private float nextPingAt;
        private bool disposed;

        public StatusBar(OperatorContext context, Action onLeaderboardToggle)
        {
            this.context = context;
            this.onLeaderboardToggle = onLeaderboardToggle;
        }

        /// <summary>Builds the header and the status strip at the top of <paramref name="canvasRoot"/>.</summary>
        public void Build(RectTransform canvasRoot)
        {
            BuildHeader(canvasRoot);
            BuildStrip(canvasRoot);
            SetXr(XrIndicator.Unknown);
            SetServer(ServerIndicator.Unknown, string.Empty);
            stationLabel.text = "İstasyon: —";
            eventLabel.text = "Etkinlik: —";
            pendingLabel.text = "Bekleyen gönderim: —";
        }

        /// <summary>Applies config-derived texts once services are bound and runs the first server check.</summary>
        public void OnBound()
        {
            var config = context.Config;
            var title = config.Branding.ProductTitle;
            titleLabel.text = string.IsNullOrWhiteSpace(title) ? "Operatör Paneli" : title + "  ·  Operatör Paneli";
            stationLabel.text = "İstasyon: " + Dash(config.Backend.StationId);
            eventLabel.text = "Etkinlik: " + Dash(config.Backend.EventSlug);
            shownXr = XrIndicator.None;
            shownPending = int.MinValue;
            xrRetryButton.Interactable = context.Xr != null;
            serverCheckButton.Interactable = context.Backend != null;
            RequestPing();
        }

        /// <summary>10 Hz refresh: VR status, pending count and the 30 s server check schedule.</summary>
        public void Refresh(float realtimeNow)
        {
            if (disposed || !context.IsBound)
            {
                return;
            }

            UpdateXr();
            UpdatePending();
            if (!pingInFlight && realtimeNow >= nextPingAt)
            {
                RequestPing();
            }
        }

        /// <summary>Reflects the leaderboard drawer state on the toggle button.</summary>
        public void SetLeaderboardOpen(bool open)
        {
            leaderboardButton.SetLabel(open ? "Liderlik Tablosunu Kapat" : "Liderlik Tablosu");
            leaderboardButton.SetStyle(open ? ButtonStyle.Warning : ButtonStyle.Accent);
        }

        /// <summary>Starts a server check now (no-op while one is running).</summary>
        public void RequestPing()
        {
            if (disposed || !context.IsBound)
            {
                return;
            }

            nextPingAt = Time.realtimeSinceStartup + PingIntervalSeconds;
            if (context.Backend == null)
            {
                SetServer(ServerIndicator.Unknown, "Sunucu istemcisi yok");
                return;
            }

            if (!context.Config.Backend.IsConfigured)
            {
                SetServer(ServerIndicator.NotConfigured, LastCheckText(null));
                return;
            }

            if (pingInFlight)
            {
                return;
            }

            int request = ++pingRequest;
            _ = PingAsync(request, lifetime.Token);
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

        private async Task PingAsync(int request, CancellationToken token)
        {
            pingInFlight = true;
            SetServer(ServerIndicator.Checking, serverDetailLabel.text);
            BackendResult<PingReceipt> result;
            try
            {
                result = await context.Backend.PingAsync(token);
            }
            catch (OperationCanceledException)
            {
                result = null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning(LogPrefix + "Ping failed: " + ex.Message);
                result = BackendResult<PingReceipt>.Failure(BackendError.Transport(ex.GetType().Name + ": " + ex.Message));
            }

            // Continuation guard: the screen may have been destroyed or a newer check started meanwhile.
            if (disposed || token.IsCancellationRequested || request != pingRequest)
            {
                return;
            }

            pingInFlight = false;
            nextPingAt = Time.realtimeSinceStartup + PingIntervalSeconds;
            if (result == null)
            {
                SetServer(ServerIndicator.Unknown, LastCheckText(null));
                return;
            }

            if (result.Ok)
            {
                SetServer(ServerIndicator.Connected, LastCheckText(null));
                var eventName = result.Value?.EventName;
                var slug = context.Config.Backend.EventSlug;
                eventLabel.text = string.IsNullOrWhiteSpace(eventName)
                    ? "Etkinlik: " + Dash(slug)
                    : "Etkinlik: " + eventName + " (" + Dash(slug) + ")";
                return;
            }

            var error = result.Error;
            if (error != null && error.Code == BackendError.CodeConfigurationMissing)
            {
                SetServer(ServerIndicator.NotConfigured, LastCheckText(null));
                return;
            }

            if (error != null)
            {
                Debug.LogWarning(LogPrefix + "Server check failed: " + error);
            }

            SetServer(ServerIndicator.Disconnected, LastCheckText(error?.Message));
        }

        private void UpdateXr()
        {
            var xr = context.Xr;
            XrIndicator state;
            if (xr == null)
            {
                state = XrIndicator.Unknown;
            }
            else if (!xr.IsXrRunning)
            {
                state = XrIndicator.NotRunning;
            }
            else
            {
                state = xr.HmdPresent ? XrIndicator.Ready : XrIndicator.NoHeadset;
            }

            if (state != shownXr)
            {
                SetXr(state);
            }
        }

        private void UpdatePending()
        {
            var outbox = context.Outbox;
            int pending = outbox != null ? outbox.PendingCount : -1;
            if (pending == shownPending)
            {
                return;
            }

            shownPending = pending;
            if (pending < 0)
            {
                pendingLabel.text = "Bekleyen gönderim: —";
                pendingLabel.color = OperatorUiStyle.TextSecondary;
            }
            else
            {
                pendingLabel.SetText("Bekleyen gönderim: {0}", pending);
                pendingLabel.color = pending > 0 ? OperatorUiStyle.Darken(OperatorUiStyle.Warning, 0.1f) : OperatorUiStyle.TextPrimary;
            }
        }

        private void SetXr(XrIndicator state)
        {
            shownXr = state;
            switch (state)
            {
                case XrIndicator.Ready:
                    xrDot.color = OperatorUiStyle.Accent;
                    xrLabel.text = "VR: Hazır";
                    break;
                case XrIndicator.NoHeadset:
                    xrDot.color = OperatorUiStyle.Warning;
                    xrLabel.text = "VR: Başlık algılanmadı";
                    break;
                case XrIndicator.NotRunning:
                    xrDot.color = OperatorUiStyle.Danger;
                    xrLabel.text = "VR: Çalışmıyor";
                    break;
                default:
                    xrDot.color = OperatorUiStyle.TextMuted;
                    xrLabel.text = "VR: Bilinmiyor";
                    break;
            }
        }

        private void SetServer(ServerIndicator state, string detail)
        {
            switch (state)
            {
                case ServerIndicator.Checking:
                    serverDot.color = OperatorUiStyle.TextMuted;
                    serverLabel.text = "Sunucu: Kontrol ediliyor…";
                    break;
                case ServerIndicator.Connected:
                    serverDot.color = OperatorUiStyle.Accent;
                    serverLabel.text = "Sunucu: Bağlı";
                    break;
                case ServerIndicator.Disconnected:
                    serverDot.color = OperatorUiStyle.Danger;
                    serverLabel.text = "Sunucu: Bağlantı yok";
                    break;
                case ServerIndicator.NotConfigured:
                    serverDot.color = OperatorUiStyle.Warning;
                    serverLabel.text = "Sunucu: Yapılandırılmamış";
                    break;
                default:
                    serverDot.color = OperatorUiStyle.TextMuted;
                    serverLabel.text = "Sunucu: Bilinmiyor";
                    break;
            }

            serverDetailLabel.text = detail ?? string.Empty;
        }

        private void OnXrRetry()
        {
            var xr = context.Xr;
            if (xr == null)
            {
                return;
            }

            try
            {
                xr.Retry();
            }
            catch (Exception ex)
            {
                Debug.LogError(LogPrefix + "XR retry failed: " + ex);
            }

            shownXr = XrIndicator.None;
            UpdateXr();
        }

        private void OnServerCheck()
        {
            RequestPing();
        }

        private void OnLeaderboardClicked()
        {
            onLeaderboardToggle?.Invoke();
        }

        private void BuildHeader(RectTransform canvasRoot)
        {
            var header = UiFactory.CreateImage("Header", canvasRoot, OperatorUiStyle.Primary, true);
            UiFactory.AnchorTop(header.rectTransform, HeaderHeight);
            UiFactory.AddHorizontalLayout(header.gameObject, 24f, new RectOffset(28, 28, 0, 0), TextAnchor.MiddleLeft, true, true, false, false);

            titleLabel = UiFactory.CreateLabel(header.rectTransform, "Title", "Operatör Paneli", OperatorUiStyle.FontHeading,
                OperatorUiStyle.TextOnDark, FontStyles.Bold);
            UiFactory.MakeSingleLine(titleLabel);
            UiFactory.SetLayout(titleLabel, 0f, -1f, 1f, -1f);

            leaderboardButton = UiFactory.CreateButton(header.rectTransform, "LeaderboardToggle", "Liderlik Tablosu", ButtonStyle.Accent,
                OnLeaderboardClicked, 48f, OperatorUiStyle.FontSmall + 2f, 320f);

            var versionLabel = UiFactory.CreateLabel(header.rectTransform, "Version", "Sürüm " + Application.version, OperatorUiStyle.FontSmall,
                OperatorUiStyle.Lighten(OperatorUiStyle.Primary, 0.7f), FontStyles.Normal, TextAlignmentOptions.Right);
            UiFactory.MakeSingleLine(versionLabel);
            UiFactory.SetLayout(versionLabel, 170f, -1f, 0f, -1f, 170f);
        }

        private void BuildStrip(RectTransform canvasRoot)
        {
            var strip = UiFactory.CreateImage("StatusStrip", canvasRoot, OperatorUiStyle.StatusStrip, true);
            UiFactory.AnchorTop(strip.rectTransform, StripHeight, HeaderHeight);
            UiFactory.AddHorizontalLayout(strip.gameObject, 30f, new RectOffset(28, 28, 6, 6), TextAnchor.MiddleLeft, true, true, false, false);

            var border = UiFactory.CreateImage("BottomBorder", strip.rectTransform, OperatorUiStyle.Border);
            border.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            UiFactory.AnchorBottom(border.rectTransform, 2f);

            // VR status
            var xrChip = UiFactory.CreateRow(strip.rectTransform, "XrStatus", 10f, TextAnchor.MiddleLeft, false);
            xrDot = UiFactory.CreateDot(xrChip, "Dot", 16f, OperatorUiStyle.TextMuted);
            xrLabel = CreateStripLabel(xrChip, "Text", 250f, OperatorUiStyle.FontSmall + 1f, FontStyles.Bold);
            xrRetryButton = UiFactory.CreateButton(xrChip, "RetryButton", "Yeniden Dene", ButtonStyle.Secondary, OnXrRetry,
                OperatorUiStyle.SmallButtonHeight, OperatorUiStyle.FontTiny, 150f);

            // Server status
            var serverChip = UiFactory.CreateRow(strip.rectTransform, "ServerStatus", 10f, TextAnchor.MiddleLeft, false);
            serverDot = UiFactory.CreateDot(serverChip, "Dot", 16f, OperatorUiStyle.TextMuted);
            var serverTexts = UiFactory.CreateColumn(serverChip, "Texts", 0f);
            UiFactory.SetLayout(serverTexts, 320f, -1f, 0f, -1f, 320f);
            serverLabel = CreateStripLabel(serverTexts, "Text", 320f, OperatorUiStyle.FontSmall + 1f, FontStyles.Bold);
            serverDetailLabel = CreateStripLabel(serverTexts, "Detail", 320f, OperatorUiStyle.FontTiny - 1f, FontStyles.Normal);
            serverDetailLabel.color = OperatorUiStyle.TextSecondary;
            serverCheckButton = UiFactory.CreateButton(serverChip, "CheckButton", "Kontrol Et", ButtonStyle.Secondary, OnServerCheck,
                OperatorUiStyle.SmallButtonHeight, OperatorUiStyle.FontTiny, 130f);

            stationLabel = CreateStripLabel(strip.rectTransform, "Station", 200f, OperatorUiStyle.FontSmall, FontStyles.Normal);
            eventLabel = CreateStripLabel(strip.rectTransform, "Event", 260f, OperatorUiStyle.FontSmall, FontStyles.Normal);
            UiFactory.SetLayout(eventLabel, 260f, -1f, 1f, -1f, 120f);
            pendingLabel = CreateStripLabel(strip.rectTransform, "Pending", 250f, OperatorUiStyle.FontSmall, FontStyles.Bold);
        }

        private static TextMeshProUGUI CreateStripLabel(Transform parent, string name, float width, float fontSize, FontStyles style)
        {
            var label = UiFactory.CreateLabel(parent, name, string.Empty, fontSize, OperatorUiStyle.TextPrimary, style);
            UiFactory.MakeSingleLine(label);
            UiFactory.SetLayout(label, width, -1f, 0f, -1f, width);
            return label;
        }

        private static string LastCheckText(string errorMessage)
        {
            var time = "Son kontrol " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(errorMessage) ? time : time + " · " + errorMessage;
        }

        private static string Dash(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "—" : value;
        }
    }
}
