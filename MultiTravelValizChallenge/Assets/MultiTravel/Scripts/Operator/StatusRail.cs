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
    /// Right rail (400 px): live status cards for VR, server (ping every 30 s and on demand), pending submissions, tutorial
    /// ("Deneme durumu") and the rig controls (Yeniden Ortala, Zemin -/+ via <c>IXrRigControl</c>, disabled with a tooltip when
    /// the service is absent). Refreshed at 10 Hz by <see cref="OperatorScreen"/>; allocation-free unless a value changed.
    /// </summary>
    public sealed class StatusRail
    {
        /// <summary>Interval of the automatic server check.</summary>
        public const float PingIntervalSeconds = 30f;

        /// <summary>Floor offset step per button press (metres), matching the rig's persisted 0.02 m granularity.</summary>
        public const float FloorStepMetres = 0.02f;

        private const string LogPrefix = "[MultiTravel.Operator] ";
        private const string RigMissingTooltip = "VR rig kontrolü kullanılamıyor (servis kayıtlı değil)";

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
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();

        private UiPill xrPill;
        private TextMeshProUGUI xrDetail;
        private UiButton xrRetryButton;
        private UiPill serverPill;
        private TextMeshProUGUI serverDetail;
        private TextMeshProUGUI eventLabel;
        private UiButton serverCheckButton;
        private TextMeshProUGUI pendingValue;
        private UiPill tutorialPill;
        private UiButton recenterButton;
        private UiButton floorDownButton;
        private UiButton floorUpButton;
        private TextMeshProUGUI floorValue;

        private XrIndicator shownXr = XrIndicator.None;
        private int shownPending = int.MinValue;
        private int shownTutorial = -1;
        private float shownFloor = float.NaN;
        private bool rigEnabled = true;
        private bool rigStateKnown;
        private int pingRequest;
        private bool pingInFlight;
        private float nextPingAt;
        private bool disposed;

        public StatusRail(OperatorContext context)
        {
            this.context = context;
        }

        /// <summary>Builds the rail inside <paramref name="host"/> (the 400 px column).</summary>
        public void Build(RectTransform host)
        {
            var column = UiFactory.CreateRect("StatusRail", host);
            UiFactory.Stretch(column, 12f, 24f, 24f, 24f);
            UiFactory.AddVerticalLayout(column.gameObject, 16f, new RectOffset(0, 0, 0, 0), TextAnchor.UpperLeft, true, true, true, false);

            BuildVr(column);
            BuildServer(column);
            BuildPending(column);
            BuildTutorial(column);
            BuildControls(column);

            SetXr(XrIndicator.Unknown);
            SetServer(ServerIndicator.Unknown, string.Empty);
            eventLabel.text = "Etkinlik —";
            pendingValue.text = "—";
            tutorialPill.Set("Bilinmiyor", OperatorUiStyle.TextMuted, false);
            floorValue.text = "—";
            SetRigEnabled(false);
        }

        /// <summary>Applies config-derived values once services are bound and runs the first server check.</summary>
        public void OnBound()
        {
            eventLabel.text = "Etkinlik " + Dash(context.Config.Backend.EventSlug);
            shownXr = XrIndicator.None;
            shownPending = int.MinValue;
            shownTutorial = -1;
            xrRetryButton.Interactable = context.Xr != null;
            serverCheckButton.Interactable = context.Backend != null;
            RequestPing();
        }

        /// <summary>10 Hz refresh: VR, pending, tutorial, rig controls and the 30 s server check schedule.</summary>
        public void Refresh(float realtimeNow)
        {
            if (disposed || !context.IsBound)
            {
                return;
            }

            UpdateXr();
            UpdatePending();
            UpdateTutorial();
            UpdateRig();
            if (!pingInFlight && realtimeNow >= nextPingAt)
            {
                RequestPing();
            }
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

        // ----- server check -----

        private async Task PingAsync(int request, CancellationToken token)
        {
            pingInFlight = true;
            SetServer(ServerIndicator.Checking, serverDetail.text);
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
                    ? "Etkinlik " + Dash(slug)
                    : "Etkinlik " + eventName + " (" + Dash(slug) + ")";
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

        // ----- live values -----

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
                pendingValue.text = "—";
                UiTween.ColorTo(pendingValue, OperatorUiStyle.TextMuted);
            }
            else
            {
                pendingValue.SetText("{0}", pending);
                UiTween.ColorTo(pendingValue, pending > 0 ? OperatorUiStyle.Warning : OperatorUiStyle.TextPrimary);
            }
        }

        private void UpdateTutorial()
        {
            var tutorial = context.Tutorial;
            int state = tutorial == null ? 0 : (tutorial.Passed ? 1 : 2);
            if (state == shownTutorial)
            {
                return;
            }

            shownTutorial = state;
            switch (state)
            {
                case 1:
                    tutorialPill.Set("Geçti", OperatorUiStyle.Success);
                    break;
                case 2:
                    tutorialPill.Set("Bekleniyor", OperatorUiStyle.Warning);
                    break;
                default:
                    tutorialPill.Set("Bilinmiyor", OperatorUiStyle.TextMuted);
                    break;
            }
        }

        private void UpdateRig()
        {
            var rig = context.Rig;
            SetRigEnabled(rig != null);
            if (rig == null)
            {
                return;
            }

            float offset = rig.FloorOffset;
            if (!Mathf.Approximately(offset, shownFloor))
            {
                shownFloor = offset;
                floorValue.SetText("{0:2} m", offset);
            }
        }

        private void SetRigEnabled(bool enabled)
        {
            if (rigStateKnown && enabled == rigEnabled)
            {
                return;
            }

            rigStateKnown = true;
            rigEnabled = enabled;
            recenterButton.Interactable = enabled;
            floorDownButton.Interactable = enabled;
            floorUpButton.Interactable = enabled;
            string tooltip = enabled ? string.Empty : RigMissingTooltip;
            recenterButton.SetTooltip(tooltip);
            floorDownButton.SetTooltip(tooltip);
            floorUpButton.SetTooltip(tooltip);
            if (!enabled)
            {
                floorValue.text = "—";
                shownFloor = float.NaN;
            }
        }

        private void SetXr(XrIndicator state)
        {
            shownXr = state;
            switch (state)
            {
                case XrIndicator.Ready:
                    xrPill.Set("Hazır", OperatorUiStyle.Success);
                    xrDetail.text = "Başlık algılandı ve izleniyor";
                    break;
                case XrIndicator.NoHeadset:
                    xrPill.Set("Başlık yok", OperatorUiStyle.Warning);
                    xrDetail.text = "Başlık algılanmadı; Link / kabloyu kontrol edin";
                    break;
                case XrIndicator.NotRunning:
                    xrPill.Set("Çalışmıyor", OperatorUiStyle.Danger);
                    xrDetail.text = "XR çalışma zamanı başlatılamadı";
                    break;
                default:
                    xrPill.Set("Bilinmiyor", OperatorUiStyle.TextMuted);
                    xrDetail.text = "VR durum servisi kayıtlı değil";
                    break;
            }
        }

        private void SetServer(ServerIndicator state, string detail)
        {
            switch (state)
            {
                case ServerIndicator.Checking:
                    serverPill.Set("Kontrol ediliyor", OperatorUiStyle.PrimaryBright);
                    break;
                case ServerIndicator.Connected:
                    serverPill.Set("Bağlı", OperatorUiStyle.Success);
                    break;
                case ServerIndicator.Disconnected:
                    serverPill.Set("Bağlantı yok", OperatorUiStyle.Danger);
                    break;
                case ServerIndicator.NotConfigured:
                    serverPill.Set("Yapılandırılmamış", OperatorUiStyle.Warning);
                    break;
                default:
                    serverPill.Set("Bilinmiyor", OperatorUiStyle.TextMuted);
                    break;
            }

            serverDetail.text = detail ?? string.Empty;
        }

        // ----- commands -----

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
            context.Toast?.Show("VR yeniden başlatma denendi", ToastKind.Info);
        }

        private void OnRecenter()
        {
            var rig = context.Rig;
            if (rig == null)
            {
                return;
            }

            try
            {
                rig.Recenter();
                context.Toast?.Show("Oyuncu yeniden ortalandı", ToastKind.Success);
            }
            catch (Exception ex)
            {
                Debug.LogError(LogPrefix + "Recenter failed: " + ex);
                context.Toast?.Show("Yeniden ortalama başarısız", ToastKind.Error);
            }
        }

        private void OnFloor(float delta)
        {
            var rig = context.Rig;
            if (rig == null)
            {
                return;
            }

            try
            {
                rig.AdjustFloorOffset(delta);
                float offset = rig.FloorOffset;
                context.Toast?.Show("Zemin ofseti " + offset.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + " m", ToastKind.Info, 1.8f);
            }
            catch (Exception ex)
            {
                Debug.LogError(LogPrefix + "Floor adjustment failed: " + ex);
            }
        }

        // ----- construction -----

        private static RectTransform CreateRailCard(Transform parent, string name, UiIcon icon, string title, out RectTransform header)
        {
            var card = UiFactory.CreateSurface(parent, name, OperatorUiStyle.Card, OperatorUiStyle.RadiusCard, true, true, out _);
            UiFactory.AddVerticalLayout(card.gameObject, 12f, new RectOffset(20, 20, 18, 18), TextAnchor.UpperLeft, true, true, true, false);
            UiFactory.SetLayout(card, -1f, -1f, 1f, 0f);
            header = UiFactory.CreateRow(card, "Header", 10f, TextAnchor.MiddleLeft, false);
            UiFactory.CreateIcon(header, "Icon", icon, 24f, OperatorUiStyle.TextSecondary);
            var label = UiFactory.CreateLabel(header, "Title", title, OperatorUiStyle.FontLabel, OperatorUiStyle.TextSecondary, FontStyles.Bold);
            UiFactory.MakeSingleLine(label);
            UiFactory.SetLayout(label, 0f, -1f, 1f, -1f);
            return card;
        }

        private void BuildVr(RectTransform column)
        {
            var card = CreateRailCard(column, "VrCard", UiIcon.Headset, "VR Başlık", out var header);
            xrPill = UiPill.Create(header, "Pill");
            xrDetail = UiFactory.CreateLabel(card, "Detail", string.Empty, OperatorUiStyle.FontLabel, OperatorUiStyle.TextMuted);
            xrRetryButton = UiFactory.CreateButton(card, "RetryButton", "Yeniden Dene", ButtonStyle.Secondary, OnXrRetry,
                OperatorUiStyle.ButtonHeightSmall, OperatorUiStyle.FontLabel, -1f, UiIcon.Refresh);
        }

        private void BuildServer(RectTransform column)
        {
            var card = CreateRailCard(column, "ServerCard", UiIcon.Server, "Sunucu", out var header);
            serverPill = UiPill.Create(header, "Pill");
            serverDetail = UiFactory.CreateLabel(card, "Detail", string.Empty, OperatorUiStyle.FontLabel, OperatorUiStyle.TextMuted);
            UiFactory.MakeSingleLine(serverDetail);
            eventLabel = UiFactory.CreateLabel(card, "Event", string.Empty, OperatorUiStyle.FontLabel, OperatorUiStyle.TextSecondary);
            UiFactory.MakeSingleLine(eventLabel);
            serverCheckButton = UiFactory.CreateButton(card, "CheckButton", "Kontrol Et", ButtonStyle.Secondary, RequestPing,
                OperatorUiStyle.ButtonHeightSmall, OperatorUiStyle.FontLabel, -1f, UiIcon.Refresh);
        }

        private void BuildPending(RectTransform column)
        {
            var card = CreateRailCard(column, "PendingCard", UiIcon.ArrowUp, "Bekleyen gönderim", out var header);
            pendingValue = UiFactory.CreateLabel(header, "Value", "—", OperatorUiStyle.FontHeading, OperatorUiStyle.TextPrimary, FontStyles.Bold,
                TextAlignmentOptions.Right);
            UiFactory.MakeSingleLine(pendingValue);
            UiFactory.SetLayout(pendingValue, 60f, -1f, 0f, -1f, 40f);
            UiFactory.CreateLabel(card, "Hint", "Sunucuya henüz ulaşmamış sonuçlar; arka planda gönderilir.", OperatorUiStyle.FontCaption,
                OperatorUiStyle.TextMuted);
        }

        private void BuildTutorial(RectTransform column)
        {
            var card = CreateRailCard(column, "TutorialCard", UiIcon.Play, "Deneme durumu", out var header);
            tutorialPill = UiPill.Create(header, "Pill");
            UiFactory.CreateLabel(card, "Hint", "Katılımcı deneme ürününü valize yerleştirdi mi?", OperatorUiStyle.FontCaption, OperatorUiStyle.TextMuted);
        }

        private void BuildControls(RectTransform column)
        {
            var card = CreateRailCard(column, "RigCard", UiIcon.Recenter, "Oyuncu konumu", out _);
            recenterButton = UiFactory.CreateButton(card, "RecenterButton", "Yeniden Ortala", ButtonStyle.Secondary, OnRecenter,
                OperatorUiStyle.ButtonHeight, OperatorUiStyle.FontBody, -1f, UiIcon.Recenter);

            var row = UiFactory.CreateRow(card, "FloorRow", 12f, TextAnchor.MiddleLeft, false);
            var texts = UiFactory.CreateColumn(row, "Texts", 0f);
            UiFactory.SetLayout(texts, 0f, -1f, 1f, -1f);
            UiFactory.CreateLabel(texts, "Caption", "Zemin ofseti", OperatorUiStyle.FontLabel, OperatorUiStyle.TextSecondary, FontStyles.Bold);
            floorValue = UiFactory.CreateLabel(texts, "Value", "—", OperatorUiStyle.FontHeading, OperatorUiStyle.TextPrimary, FontStyles.Bold);
            UiFactory.MakeSingleLine(floorValue);
            floorDownButton = UiFactory.CreateButton(row, "FloorDown", "Zemin −", ButtonStyle.Secondary, () => OnFloor(-FloorStepMetres),
                OperatorUiStyle.ButtonHeight, OperatorUiStyle.FontLabel, -1f, UiIcon.ArrowDown);
            floorUpButton = UiFactory.CreateButton(row, "FloorUp", "Zemin +", ButtonStyle.Secondary, () => OnFloor(FloorStepMetres),
                OperatorUiStyle.ButtonHeight, OperatorUiStyle.FontLabel, -1f, UiIcon.ArrowUp);
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
