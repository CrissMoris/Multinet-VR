using MultiTravel.Core.Session;
using MultiTravel.Operator.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace MultiTravel.Operator.Panels
{
    /// <summary>
    /// Registration form in a two-column 960 px card: Ad, Soyad, Telefon, E-posta (floating labels, live validity check,
    /// inline errors) and the KVKK consent block when configured. The phone number formats itself as 0XXX XXX XX XX. Enter moves
    /// to the next field and submits from the last one; Tab / Shift+Tab move the focus. "Devam" calls
    /// <see cref="SessionController.SubmitRegistration"/>; field errors from the <see cref="ValidationResult"/> are shown
    /// inline in Turkish. All fields are cleared for every new session.
    /// </summary>
    public sealed class RegistrationPanel : OperatorPanel
    {
        private const int FieldCount = 4;
        private const int IndexFirstName = 0;
        private const int IndexLastName = 1;
        private const int IndexPhone = 2;
        private const int IndexEmail = 3;
        private const int PhoneCharacterLimit = 24;

        private static readonly string[] FieldKeys =
        {
            ParticipantValidator.FieldFirstName,
            ParticipantValidator.FieldLastName,
            ParticipantValidator.FieldPhone,
            ParticipantValidator.FieldEmail
        };

        private readonly UiField[] fields = new UiField[FieldCount];

        private RectTransform consentBlock;
        private UiToggle consentToggle;
        private TextMeshProUGUI consentError;
        private UiButton continueButton;
        private bool focusPending;
        private int pendingFocus = -1;

        public RegistrationPanel(OperatorContext context) : base(context)
        {
        }

        public override bool HandlesState(SessionState state)
        {
            return state == SessionState.Registration;
        }

        protected override void OnBuild(RectTransform root)
        {
            var card = UiFactory.CreateCard(root, "Card", 960f, 40, 20f);

            var header = UiFactory.CreateRow(card, "Header", 20f, TextAnchor.MiddleLeft, false);
            CreateBadge(header, UiIcon.User, OperatorUiStyle.PrimaryBright, 72f);
            var titles = UiFactory.CreateColumn(header, "Titles", 4f, TextAnchor.MiddleLeft);
            UiFactory.SetLayout(titles, 0f, -1f, 1f, -1f);
            var heading = CreateHeading(titles, "Katılımcı Kaydı", TextAlignmentOptions.Left);
            UiFactory.MakeSingleLine(heading);
            CreateBody(titles, "Hint", "Bilgileri katılımcıdan alarak doldurun. Enter ile sonraki alana geçin.", TextAlignmentOptions.Left);

            UiFactory.CreateDivider(card);

            var rowNames = UiFactory.CreateRow(card, "NamesRow", 24f, TextAnchor.UpperLeft);
            fields[IndexFirstName] = CreateField(rowNames, IndexFirstName, "Ad", "Örn. Ayşe", TMP_InputField.ContentType.Standard, ParticipantValidator.MaxNameLength);
            fields[IndexLastName] = CreateField(rowNames, IndexLastName, "Soyad", "Örn. Yılmaz", TMP_InputField.ContentType.Standard, ParticipantValidator.MaxNameLength);

            var rowContact = UiFactory.CreateRow(card, "ContactRow", 24f, TextAnchor.UpperLeft);
            fields[IndexPhone] = CreateField(rowContact, IndexPhone, "Telefon", "0XXX XXX XX XX", TMP_InputField.ContentType.Standard, PhoneCharacterLimit);
            fields[IndexEmail] = CreateField(rowContact, IndexEmail, "E-posta", "ornek@alanadi.com", TMP_InputField.ContentType.EmailAddress, ParticipantValidator.MaxEmailLength);

            var phone = fields[IndexPhone].Input;
            phone.keyboardType = TouchScreenKeyboardType.PhonePad;
            phone.onValidateInput = ValidatePhoneCharacter;

            consentBlock = UiFactory.CreateColumn(card, "ConsentBlock", 6f);
            consentToggle = UiToggle.Create(consentBlock, "ConsentToggle", string.Empty, OperatorUiStyle.FontLabel);
            consentToggle.Changed += OnConsentChanged;
            consentError = UiFactory.CreateLabel(consentBlock, "ConsentError", string.Empty, OperatorUiStyle.FontCaption, OperatorUiStyle.DangerText);
            consentError.gameObject.SetActive(false);

            UiFactory.CreateSpacer(card, 10f, 4f);
            var buttons = UiFactory.CreateRow(card, "Buttons", 16f, TextAnchor.MiddleRight, false);
            UiFactory.CreateButton(buttons, "CancelButton", "Vazgeç", ButtonStyle.Ghost, OnCancelRequested,
                OperatorUiStyle.ButtonHeight, OperatorUiStyle.FontBody, 180f);
            continueButton = UiFactory.CreateButton(buttons, "ContinueButton", "Devam", ButtonStyle.Primary, TrySubmit,
                OperatorUiStyle.ButtonHeight, OperatorUiStyle.FontBody, 280f, UiIcon.Play);
        }

        protected override void OnShow(SessionState state)
        {
            ClearParticipantData();

            var privacy = Context.Config.Privacy;
            bool consentRequired = privacy.IsConsentRequired;
            consentToggle.Label.text = consentRequired ? privacy.ConsentText : string.Empty;
            UiFactory.SetActive(consentBlock, consentRequired);
            continueButton.Interactable = true;
            focusPending = true;
            pendingFocus = -1;
        }

        protected override void OnHide()
        {
            CloseConfirm();
            focusPending = false;
            var eventSystem = EventSystem.current;
            if (eventSystem != null && !eventSystem.alreadySelecting)
            {
                eventSystem.SetSelectedGameObject(null);
            }
        }

        /// <summary>Empties every field, the consent checkbox and all error labels.</summary>
        public override void ClearParticipantData()
        {
            for (int i = 0; i < FieldCount; i++)
            {
                fields[i]?.Clear();
            }

            consentToggle?.SetIsOn(false);
            SetConsentError(null);
            CloseConfirm();
        }

        /// <summary>Per-frame keyboard handling while visible (initial focus, Tab / Shift+Tab). Allocation-free.</summary>
        public override void Tick()
        {
            if (!IsVisible)
            {
                return;
            }

            if (focusPending && CanUseShortcuts())
            {
                focusPending = false;
                fields[IndexFirstName].Focus();
            }

            if (pendingFocus >= 0)
            {
                // Focus moves requested from onSubmit run one frame later: the input field still finishes its own
                // Enter handling (and deactivates itself) after the submit callback returns.
                int index = pendingFocus;
                pendingFocus = -1;
                fields[index].Focus();
            }

            var keyboard = Keyboard.current;
            if (keyboard == null || !CanUseShortcuts())
            {
                return;
            }

            if (keyboard.tabKey.wasPressedThisFrame)
            {
                MoveFocus(keyboard.shiftKey.isPressed);
            }
        }

        private UiField CreateField(Transform row, int index, string label, string hint, TMP_InputField.ContentType contentType, int characterLimit)
        {
            var field = UiField.Create(row, label + "Field", label, hint, contentType, characterLimit);
            field.Changed += _ => OnFieldChanged(index);
            field.Blurred += _ => OnFieldBlurred(index);
            field.Submitted += _ => OnFieldSubmitted(index);
            return field;
        }

        private void OnFieldChanged(int index)
        {
            var field = fields[index];
            if (index == IndexPhone)
            {
                string formatted = PhoneFormatter.Format(field.Text);
                if (formatted != field.Text)
                {
                    field.Input.SetTextWithoutNotify(formatted);
                    field.Input.caretPosition = formatted.Length;
                }
            }

            field.SetState(FieldState.None, null);
            if (field.Text.Length > 0)
            {
                var validation = ParticipantValidator.Validate(BuildInput(false), false);
                if (!validation.TryGetError(FieldKeys[index], out _))
                {
                    field.SetState(FieldState.Valid, null);
                }
            }
        }

        private void OnFieldBlurred(int index)
        {
            var field = fields[index];
            if (!IsVisible || field.Text.Length == 0)
            {
                return;
            }

            var validation = ParticipantValidator.Validate(BuildInput(false), false);
            if (validation.TryGetError(FieldKeys[index], out var message))
            {
                field.SetState(FieldState.Error, message);
            }
        }

        private void OnFieldSubmitted(int index)
        {
            if (index < FieldCount - 1)
            {
                pendingFocus = index + 1;
            }
            else
            {
                TrySubmit();
            }
        }

        private ParticipantInput BuildInput(bool includeConsent)
        {
            var privacy = Context.Config.Privacy;
            bool consentRequired = includeConsent && privacy.IsConsentRequired;
            return new ParticipantInput
            {
                FirstName = fields[IndexFirstName].Text,
                LastName = fields[IndexLastName].Text,
                Phone = fields[IndexPhone].Text,
                Email = fields[IndexEmail].Text,
                ConsentAccepted = consentRequired ? consentToggle.IsOn : (bool?)null,
                ConsentVersion = consentRequired ? privacy.ConsentVersion : null
            };
        }

        private void TrySubmit()
        {
            if (!IsVisible || Context.State != SessionState.Registration)
            {
                return;
            }

            var privacy = Context.Config.Privacy;
            bool consentRequired = privacy.IsConsentRequired;
            var input = BuildInput(true);

            ValidationResult validation = null;
            Context.Run(SessionState.Registration, () => validation = Context.Session.SubmitRegistration(input), "SubmitRegistration");
            if (validation == null || validation.IsValid)
            {
                return;
            }

            int firstInvalid = -1;
            for (int i = 0; i < FieldCount; i++)
            {
                if (validation.TryGetError(FieldKeys[i], out var message))
                {
                    fields[i].SetState(FieldState.Error, message);
                    if (firstInvalid < 0)
                    {
                        firstInvalid = i;
                    }
                }
                else
                {
                    fields[i].SetState(fields[i].Text.Length > 0 ? FieldState.Valid : FieldState.None, null);
                }
            }

            validation.TryGetError(ParticipantValidator.FieldConsent, out var consentMessage);
            SetConsentError(consentRequired ? consentMessage : null);

            if (firstInvalid >= 0)
            {
                pendingFocus = firstInvalid;
            }

            Context.Toast?.Show("Lütfen işaretli alanları düzeltin", ToastKind.Warning, 2.5f);
        }

        private void OnCancelRequested()
        {
            Confirm("Kayıt iptal edilsin mi?", "Girilen bilgiler silinecek ve karşılama ekranına dönülecek.", "Evet, iptal et", ButtonStyle.Danger, CancelRegistration);
        }

        private void CancelRegistration()
        {
            if (Context.Run(SessionState.Registration, Context.Session.AbandonSession, "AbandonSession"))
            {
                Context.Run(OperatorContext.CanReset, Context.Session.ResetForNextParticipant, "ResetForNextParticipant");
            }
        }

        private void OnConsentChanged(bool isOn)
        {
            if (isOn)
            {
                SetConsentError(null);
            }
        }

        private void SetConsentError(string message)
        {
            if (consentError == null)
            {
                return;
            }

            bool show = !string.IsNullOrEmpty(message);
            if (show)
            {
                consentError.text = message;
            }

            UiFactory.SetActive(consentError, show);
        }

        private void MoveFocus(bool backwards)
        {
            int current = FocusedIndex();
            int next;
            if (current < 0)
            {
                next = backwards ? FieldCount - 1 : 0;
            }
            else
            {
                next = backwards ? (current + FieldCount - 1) % FieldCount : (current + 1) % FieldCount;
            }

            fields[next].Focus();
        }

        private int FocusedIndex()
        {
            var eventSystem = EventSystem.current;
            var selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
            for (int i = 0; i < FieldCount; i++)
            {
                if (fields[i].IsFocused || fields[i].Input.isFocused || (selected != null && selected == fields[i].Input.gameObject))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Phone input filter: digits, a single leading '+', spaces, dashes and parentheses.</summary>
        private static char ValidatePhoneCharacter(string text, int charIndex, char addedChar)
        {
            if (addedChar >= '0' && addedChar <= '9')
            {
                return addedChar;
            }

            if (addedChar == '+')
            {
                return charIndex == 0 && (string.IsNullOrEmpty(text) || text[0] != '+') ? addedChar : '\0';
            }

            if (addedChar == ' ' || addedChar == '-' || addedChar == '(' || addedChar == ')')
            {
                return addedChar;
            }

            return '\0';
        }
    }
}
