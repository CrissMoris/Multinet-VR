using MultiTravel.Core.Session;
using MultiTravel.Operator.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MultiTravel.Operator.Panels
{
    /// <summary>
    /// Registration form (Ad, Soyad, Telefon, E-posta, optional KVKK consent). "Devam" (or Enter) calls
    /// <see cref="SessionController.SubmitRegistration"/>; field errors from the <see cref="ValidationResult"/> are shown
    /// inline in Turkish. Tab / Shift+Tab move the focus between fields. All fields are cleared for every new session.
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

        private readonly TMP_InputField[] fields = new TMP_InputField[FieldCount];
        private readonly TextMeshProUGUI[] fieldErrors = new TextMeshProUGUI[FieldCount];

        private RectTransform consentBlock;
        private Toggle consentToggle;
        private TextMeshProUGUI consentLabel;
        private TextMeshProUGUI consentError;
        private UiButton continueButton;
        private ConfirmPrompt cancelPrompt;
        private bool focusPending;
        private int shownFrame = -1;

        public RegistrationPanel(OperatorContext context) : base(context)
        {
        }

        public override bool HandlesState(SessionState state)
        {
            return state == SessionState.Registration;
        }

        protected override void OnBuild(RectTransform root)
        {
            var card = UiFactory.CreateCard(root, "Card", OperatorUiStyle.CardWidth, 44, 18f);
            CreateHeading(card, "Katılımcı Kaydı");
            CreateBody(card, "Hint", "Bilgileri katılımcıdan alarak doldurun. Tab ile sonraki alana geçebilir, Enter ile devam edebilirsiniz.");

            var rowNames = UiFactory.CreateRow(card, "NamesRow", 28f, TextAnchor.UpperLeft);
            fields[IndexFirstName] = CreateField(rowNames, IndexFirstName, "Ad", "Örn. Ayşe", TMP_InputField.ContentType.Standard, ParticipantValidator.MaxNameLength);
            fields[IndexLastName] = CreateField(rowNames, IndexLastName, "Soyad", "Örn. Yılmaz", TMP_InputField.ContentType.Standard, ParticipantValidator.MaxNameLength);

            var rowContact = UiFactory.CreateRow(card, "ContactRow", 28f, TextAnchor.UpperLeft);
            fields[IndexPhone] = CreateField(rowContact, IndexPhone, "Telefon", "05XX XXX XX XX", TMP_InputField.ContentType.Standard, PhoneCharacterLimit);
            fields[IndexEmail] = CreateField(rowContact, IndexEmail, "E-posta", "ornek@alanadi.com", TMP_InputField.ContentType.EmailAddress, ParticipantValidator.MaxEmailLength);

            var phone = fields[IndexPhone];
            phone.keyboardType = TouchScreenKeyboardType.PhonePad;
            phone.onValidateInput = ValidatePhoneCharacter;

            consentBlock = UiFactory.CreateColumn(card, "ConsentBlock", 6f);
            consentToggle = UiFactory.CreateToggle(consentBlock, "ConsentToggle", string.Empty, OperatorUiStyle.FontSmall, out consentLabel);
            consentToggle.onValueChanged.AddListener(OnConsentChanged);
            consentError = CreateErrorLabel(consentBlock, "ConsentError");

            UiFactory.CreateSpacer(card, 10f, 4f);
            cancelPrompt = new ConfirmPrompt(card, "CancelPrompt");

            var buttons = UiFactory.CreateRow(card, "Buttons", 20f, TextAnchor.MiddleRight, false);
            UiFactory.CreateButton(buttons, "CancelButton", "Vazgeç", ButtonStyle.Secondary, OnCancelRequested,
                OperatorUiStyle.ButtonHeight, OperatorUiStyle.FontButton, 220f);
            continueButton = UiFactory.CreateButton(buttons, "ContinueButton", "Devam", ButtonStyle.Primary, TrySubmit,
                OperatorUiStyle.ButtonHeight, OperatorUiStyle.FontButton, 320f);
        }

        protected override void OnShow(SessionState state)
        {
            ClearParticipantData();

            var privacy = Context.Config.Privacy;
            bool consentRequired = privacy.IsConsentRequired;
            consentLabel.text = consentRequired ? privacy.ConsentText : string.Empty;
            UiFactory.SetActive(consentBlock, consentRequired);
            continueButton.Interactable = true;
            focusPending = true;
            shownFrame = Time.frameCount;
        }

        protected override void OnHide()
        {
            cancelPrompt.Close();
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
                if (fields[i] != null)
                {
                    fields[i].SetTextWithoutNotify(string.Empty);
                }

                SetError(fieldErrors[i], null);
            }

            if (consentToggle != null)
            {
                consentToggle.SetIsOnWithoutNotify(false);
            }

            SetError(consentError, null);
            cancelPrompt?.Close();
        }

        /// <summary>Per-frame keyboard handling while visible (Tab / Shift+Tab focus, Enter submits). Allocation-free.</summary>
        public void Tick()
        {
            if (!IsVisible)
            {
                return;
            }

            if (focusPending)
            {
                focusPending = false;
                Focus(IndexFirstName);
            }

            var keyboard = Keyboard.current;
            if (keyboard == null || Time.frameCount <= shownFrame)
            {
                // Ignore the key press that opened the panel (e.g. Enter submitting "Başla" in the same frame).
                return;
            }

            if (keyboard.tabKey.wasPressedThisFrame)
            {
                bool backwards = keyboard.shiftKey.isPressed;
                MoveFocus(backwards);
                return;
            }

            if ((keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) && !cancelPrompt.IsOpen)
            {
                if (IsNonInputSelectableSelected())
                {
                    // A focused button / toggle receives Enter as a uGUI Submit event; do not submit twice.
                    return;
                }

                TrySubmit();
            }
        }

        private void TrySubmit()
        {
            if (!IsVisible || Context.State != SessionState.Registration)
            {
                return;
            }

            var privacy = Context.Config.Privacy;
            bool consentRequired = privacy.IsConsentRequired;
            var input = new ParticipantInput
            {
                FirstName = fields[IndexFirstName].text,
                LastName = fields[IndexLastName].text,
                Phone = fields[IndexPhone].text,
                Email = fields[IndexEmail].text,
                ConsentAccepted = consentRequired ? consentToggle.isOn : (bool?)null,
                ConsentVersion = consentRequired ? privacy.ConsentVersion : null
            };

            ValidationResult validation = null;
            Context.Run(SessionState.Registration, () => validation = Context.Session.SubmitRegistration(input), "SubmitRegistration");
            if (validation == null || validation.IsValid)
            {
                return;
            }

            int firstInvalid = -1;
            for (int i = 0; i < FieldCount; i++)
            {
                validation.TryGetError(FieldKeys[i], out var message);
                SetError(fieldErrors[i], message);
                if (message != null && firstInvalid < 0)
                {
                    firstInvalid = i;
                }
            }

            validation.TryGetError(ParticipantValidator.FieldConsent, out var consentMessage);
            SetError(consentError, consentRequired ? consentMessage : null);

            if (firstInvalid >= 0)
            {
                Focus(firstInvalid);
            }
        }

        private void OnCancelRequested()
        {
            cancelPrompt.Ask("Kayıt iptal edilsin mi? Girilen bilgiler silinecek.", "Evet, iptal et", ButtonStyle.Danger, CancelRegistration);
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
                SetError(consentError, null);
            }
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

            Focus(next);
        }

        private int FocusedIndex()
        {
            var eventSystem = EventSystem.current;
            var selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
            for (int i = 0; i < FieldCount; i++)
            {
                if (fields[i].isFocused || (selected != null && selected == fields[i].gameObject))
                {
                    return i;
                }
            }

            return -1;
        }

        private void Focus(int index)
        {
            var field = fields[index];
            var eventSystem = EventSystem.current;
            if (eventSystem != null)
            {
                eventSystem.SetSelectedGameObject(field.gameObject);
            }

            field.ActivateInputField();
        }

        private static bool IsNonInputSelectableSelected()
        {
            var eventSystem = EventSystem.current;
            var selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
            if (selected == null)
            {
                return false;
            }

            return selected.GetComponent<TMP_InputField>() == null && selected.GetComponent<Selectable>() != null;
        }

        private TMP_InputField CreateField(Transform row, int index, string label, string placeholder, TMP_InputField.ContentType contentType, int characterLimit)
        {
            var column = UiFactory.CreateColumn(row, label + "Field", 6f);
            UiFactory.SetLayout(column, 0f, -1f, 1f, -1f);
            UiFactory.CreateLabel(column, "Label", label, OperatorUiStyle.FontSmall, OperatorUiStyle.TextSecondary, FontStyles.Bold);
            var field = UiFactory.CreateInputField(column, "Input", placeholder, contentType, characterLimit);
            fieldErrors[index] = CreateErrorLabel(column, "Error");
            field.onValueChanged.AddListener(_ => SetError(fieldErrors[index], null));
            return field;
        }

        private static TextMeshProUGUI CreateErrorLabel(Transform parent, string name)
        {
            var label = UiFactory.CreateLabel(parent, name, string.Empty, OperatorUiStyle.FontSmall, OperatorUiStyle.Danger);
            label.gameObject.SetActive(false);
            return label;
        }

        private static void SetError(TextMeshProUGUI label, string message)
        {
            if (label == null)
            {
                return;
            }

            bool show = !string.IsNullOrEmpty(message);
            if (show)
            {
                label.text = message;
            }

            UiFactory.SetActive(label, show);
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
