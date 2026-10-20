using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MultiTravel.Core.Backend;
using MultiTravel.Core.Session;
using MultiTravel.Operator.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MultiTravel.Operator.Panels
{
    /// <summary>
    /// Registration step since October 2026: participants register themselves on the registration web page, so the
    /// operator screen (mirrored to the stand TV) never shows a form or contact data. This panel lists the web queue
    /// ("Ayşe Y." + product set, oldest first, refreshed every few seconds); "Oyuna Al" claims the participant on the
    /// server and moves the session straight to the instructions (the product set comes from the registration).
    /// Enter takes the first participant in the queue.
    /// </summary>
    public sealed class QueuePanel : OperatorPanel
    {
        private const int MaxRows = 8;
        private const float RefreshSeconds = 3f;

        private readonly List<Row> rows = new List<Row>();
        private TextMeshProUGUI status;
        private WaitingParticipant[] waiting = Array.Empty<WaitingParticipant>();
        private float nextRefreshAt;
        private bool inFlight;
        private bool claiming;
        private int request;

        public QueuePanel(OperatorContext context) : base(context)
        {
        }

        public override bool HandlesState(SessionState state)
        {
            return state == SessionState.Registration || state == SessionState.GenderSelection;
        }

        protected override void OnBuild(RectTransform root)
        {
            var card = UiFactory.CreateCard(root, "Card", 960f, 40, 18f);

            var header = UiFactory.CreateRow(card, "Header", 20f, TextAnchor.MiddleLeft, false);
            CreateBadge(header, UiIcon.User, OperatorUiStyle.PrimaryBright, 72f);
            var titles = UiFactory.CreateColumn(header, "Titles", 4f, TextAnchor.MiddleLeft);
            UiFactory.SetLayout(titles, 0f, -1f, 1f, -1f);
            var heading = CreateHeading(titles, "Sıradaki Katılımcılar", TextAlignmentOptions.Left);
            UiFactory.MakeSingleLine(heading);
            CreateBody(titles, "Hint", "Katılımcılar kayıt sayfasından kaydolur. Oyuna alınacak kişiyi seçin (Enter: sıradaki ilk kişi).",
                TextAlignmentOptions.Left);

            UiFactory.CreateDivider(card);

            for (int i = 0; i < MaxRows; i++)
            {
                rows.Add(Row.Create(card, i, OnTake, OnRemoveRequested));
            }

            status = UiFactory.CreateLabel(card, "Status", string.Empty, OperatorUiStyle.FontBody, OperatorUiStyle.TextMuted,
                FontStyles.Normal, TextAlignmentOptions.Center);

            UiFactory.CreateSpacer(card, 10f, 4f);
            var buttons = UiFactory.CreateRow(card, "Buttons", 16f, TextAnchor.MiddleRight, false);
            UiFactory.CreateFlexibleSpace(buttons);
            UiFactory.CreateButton(buttons, "RefreshButton", "Yenile", ButtonStyle.Secondary, () => Refresh(true),
                OperatorUiStyle.ButtonHeight, OperatorUiStyle.FontBody, 180f, UiIcon.Refresh);
            UiFactory.CreateButton(buttons, "CancelButton", "Vazgeç", ButtonStyle.Ghost, OnCancel,
                OperatorUiStyle.ButtonHeight, OperatorUiStyle.FontBody, 180f);
        }

        protected override void OnShow(SessionState state)
        {
            claiming = false;
            waiting = Array.Empty<WaitingParticipant>();
            Render();
            Refresh(true);
        }

        public override void Refresh()
        {
            if (Time.realtimeSinceStartup >= nextRefreshAt)
            {
                Refresh(false);
            }
        }

        public override void Tick()
        {
            if (!CanUseShortcuts() || claiming)
            {
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) && waiting.Length > 0)
            {
                OnTake(0);
            }
        }

        public override void ClearParticipantData()
        {
            waiting = Array.Empty<WaitingParticipant>();
            if (rows.Count > 0)
            {
                Render();
            }
        }

        // ----- queue -----

        private void Refresh(bool force)
        {
            nextRefreshAt = Time.realtimeSinceStartup + RefreshSeconds;
            if (!Context.IsBound || Context.Backend == null)
            {
                ShowStatus("Servisler hazır değil.", OperatorUiStyle.DangerText);
                return;
            }

            if (!Context.Config.Backend.IsConfigured)
            {
                ShowStatus("Sunucu yapılandırılmamış: kayıt sırası alınamıyor.", OperatorUiStyle.Warning);
                return;
            }

            if (inFlight && !force)
            {
                return;
            }

            _ = FetchAsync(++request, Context.Lifetime);
        }

        private async Task FetchAsync(int current, CancellationToken token)
        {
            inFlight = true;
            BackendResult<WaitingParticipant[]> result;
            try
            {
                result = await Context.Backend.ListWaitingAsync(MaxRows, token);
            }
            catch (Exception ex)
            {
                result = BackendResult<WaitingParticipant[]>.Failure(BackendError.Transport(ex.GetType().Name + ": " + ex.Message));
            }

            if (current != request || token.IsCancellationRequested || !IsVisible)
            {
                return;
            }

            inFlight = false;
            if (!result.Ok)
            {
                ShowStatus("Kayıt sırası alınamadı: " + (result.Error != null ? result.Error.Message : BackendErrorMessages.ServerUnreachable),
                    OperatorUiStyle.DangerText);
                return;
            }

            waiting = result.Value ?? Array.Empty<WaitingParticipant>();
            Render();
        }

        private void Render()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].Set(i < waiting.Length ? waiting[i] : null, i, !claiming);
            }

            if (claiming)
            {
                ShowStatus("Katılımcı oyuna alınıyor…", OperatorUiStyle.TextMuted);
            }
            else if (waiting.Length == 0)
            {
                ShowStatus("Sırada bekleyen yok. Kayıt sayfasından kayıt bekleniyor…", OperatorUiStyle.TextMuted);
            }
            else
            {
                ShowStatus(waiting.Length + " kişi sırada", OperatorUiStyle.TextMuted);
            }
        }

        private void OnTake(int index)
        {
            if (claiming || index < 0 || index >= waiting.Length)
            {
                return;
            }

            _ = ClaimAsync(waiting[index], Context.Lifetime);
        }

        private async Task ClaimAsync(WaitingParticipant entry, CancellationToken token)
        {
            claiming = true;
            Render();
            BackendResult<ClaimedParticipant> result;
            try
            {
                result = await Context.Backend.ClaimParticipantAsync(entry.ParticipantId, token);
            }
            catch (Exception ex)
            {
                result = BackendResult<ClaimedParticipant>.Failure(BackendError.Transport(ex.GetType().Name + ": " + ex.Message));
            }

            claiming = false;
            if (token.IsCancellationRequested || Context.State != SessionState.Registration)
            {
                return;
            }

            if (!result.Ok || result.Value == null)
            {
                bool taken = result.Error != null && result.Error.Code == "QUEUE_NOT_WAITING";
                Context.Toast?.Show(taken ? "Bu katılımcı başka bir istasyonda oyuna alındı veya sıradan çıkarıldı." :
                    "Katılımcı alınamadı: " + (result.Error != null ? result.Error.Message : BackendErrorMessages.ServerUnreachable), ToastKind.Warning, 4f);
                Refresh(true);
                return;
            }

            var p = result.Value;
            var input = new ParticipantInput
            {
                FirstName = p.FirstName,
                LastName = p.LastName,
                Title = p.Title,
                Company = p.Company,
                Location = p.Location,
                Phone = p.Phone,
                Email = p.Email,
                ConsentAccepted = p.ConsentAccepted,
                ConsentVersion = p.ConsentVersion
            };
            var gender = string.Equals(p.Gender, "male", StringComparison.OrdinalIgnoreCase) ? Gender.Male : Gender.Female;
            ValidationResult validation = null;
            bool ok = Context.Run(SessionState.Registration,
                () => validation = Context.Session.AdmitQueuedParticipant(p.ClientSessionId, p.ParticipantId, input, gender), "AdmitQueuedParticipant");
            if (!ok || validation == null || !validation.IsValid)
            {
                Context.Toast?.Show("Kayıt bilgileri geçersiz: " + (validation != null ? validation.FirstError : "bilinmeyen hata"), ToastKind.Error, 5f);
                _ = Context.Backend.ReleaseParticipantAsync(p.ParticipantId, CancellationToken.None);
                Render();
            }
        }

        private void OnRemoveRequested(int index)
        {
            if (claiming || index < 0 || index >= waiting.Length)
            {
                return;
            }

            var entry = waiting[index];
            Confirm("Sıradan çıkarılsın mı?", entry.DisplayName + " sıradan çıkarılacak (kaydı silinmez).", "Evet, çıkar", ButtonStyle.Danger,
                () => _ = RemoveAsync(entry, Context.Lifetime));
        }

        private async Task RemoveAsync(WaitingParticipant entry, CancellationToken token)
        {
            BackendResult<QueueAck> result;
            try
            {
                result = await Context.Backend.RemoveFromQueueAsync(entry.ParticipantId, token);
            }
            catch (Exception ex)
            {
                result = BackendResult<QueueAck>.Failure(BackendError.Transport(ex.Message));
            }

            if (!result.Ok)
            {
                Context.Toast?.Show("Sıradan çıkarılamadı: " + (result.Error != null ? result.Error.Message : string.Empty), ToastKind.Warning, 4f);
            }

            Refresh(true);
        }

        private void OnCancel()
        {
            if (Context.Run(SessionState.Registration, Context.Session.AbandonSession, "AbandonSession"))
            {
                Context.Run(OperatorContext.CanReset, Context.Session.ResetForNextParticipant, "ResetForNextParticipant");
            }
        }

        private void ShowStatus(string text, Color color)
        {
            if (status != null)
            {
                status.text = text;
                status.color = color;
            }
        }

        /// <summary>One queue line: number, "Ayşe Y.", product set, "Oyuna Al" and "Çıkar".</summary>
        private sealed class Row
        {
            private RectTransform root;
            private TextMeshProUGUI number;
            private TextMeshProUGUI name;
            private TextMeshProUGUI set;
            private UiButton take;
            private UiButton remove;

            public static Row Create(Transform parent, int index, Action<int> onTake, Action<int> onRemove)
            {
                var row = new Row();
                row.root = UiFactory.CreateSurface(parent, "QueueRow" + index, OperatorUiStyle.Elevated, OperatorUiStyle.RadiusButton, false, true, out _);
                UiFactory.AddHorizontalLayout(row.root.gameObject, 14f, new RectOffset(20, 12, 0, 0), TextAnchor.MiddleLeft, true, true, false, false);
                UiFactory.SetLayout(row.root, -1f, 64f, 1f, 0f, -1f, 64f);
                row.number = UiFactory.CreateLabel(row.root, "Number", string.Empty, OperatorUiStyle.FontBody, OperatorUiStyle.TextMuted, FontStyles.Bold);
                UiFactory.SetLayout(row.number, 40f, -1f, 0f, 0f, 40f);
                row.name = UiFactory.CreateLabel(row.root, "Name", string.Empty, OperatorUiStyle.FontHeading, OperatorUiStyle.TextPrimary, FontStyles.Bold);
                UiFactory.MakeSingleLine(row.name);
                UiFactory.SetLayout(row.name, 0f, -1f, 1f, -1f);
                row.set = UiFactory.CreateLabel(row.root, "Set", string.Empty, OperatorUiStyle.FontLabel, OperatorUiStyle.TextSecondary);
                UiFactory.SetLayout(row.set, 150f, -1f, 0f, 0f, 150f);
                row.remove = UiFactory.CreateButton(row.root, "Remove", "Çıkar", ButtonStyle.Ghost, () => onRemove(index),
                    48f, OperatorUiStyle.FontLabel, 110f);
                row.take = UiFactory.CreateButton(row.root, "Take", "Oyuna Al", index == 0 ? ButtonStyle.Primary : ButtonStyle.Secondary,
                    () => onTake(index), 48f, OperatorUiStyle.FontBody, 190f, UiIcon.Play);
                return row;
            }

            public void Set(WaitingParticipant entry, int index, bool interactable)
            {
                UiFactory.SetActive(root, entry != null);
                if (entry == null)
                {
                    return;
                }

                number.SetText("{0}.", index + 1);
                name.text = string.IsNullOrWhiteSpace(entry.DisplayName) ? "—" : entry.DisplayName;
                set.text = string.Equals(entry.Gender, "male", StringComparison.OrdinalIgnoreCase) ? "Erkek ürün seti" : "Kadın ürün seti";
                take.Interactable = interactable;
                remove.Interactable = interactable;
            }
        }
    }
}
