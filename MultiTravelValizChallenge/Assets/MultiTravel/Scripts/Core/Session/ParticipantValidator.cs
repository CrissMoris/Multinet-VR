using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MultiTravel.Core.Session
{
    /// <summary>
    /// Pure registration-form validation with Turkish messages (ARCHITECTURE.md §2.3).
    /// Rules mirror the backend: names 1..60 characters after trimming; phone normalised by stripping spaces,
    /// dashes and parentheses, optional leading '+', 10..15 digits; e-mail checked with an RFC-lite pattern.
    /// </summary>
    public static class ParticipantValidator
    {
        public const string FieldFirstName = "firstName";
        public const string FieldLastName = "lastName";
        public const string FieldPhone = "phone";
        public const string FieldEmail = "email";
        public const string FieldConsent = "consent";

        public const int MaxNameLength = 60;
        public const int MinPhoneDigits = 10;
        public const int MaxPhoneDigits = 15;
        public const int MaxEmailLength = 254;

        /// <summary>Field keys in form order (used for "first error" selection).</summary>
        public static readonly IReadOnlyList<string> FieldOrder = new[]
        {
            FieldFirstName, FieldLastName, FieldPhone, FieldEmail, FieldConsent
        };

        private static readonly Regex PhonePattern = new Regex("^\\+?[0-9]{10,15}$", RegexOptions.Compiled);
        private static readonly Regex EmailPattern = new Regex("^[^\\s@]+@[^\\s@]+\\.[^\\s@]{2,}$", RegexOptions.Compiled);

        /// <summary>
        /// Validates and normalises the input. <paramref name="consentRequired"/> is true when the event collects KVKK consent
        /// (non-empty consent text); the checkbox must then be ticked.
        /// </summary>
        public static ValidationResult Validate(ParticipantInput input, bool consentRequired)
        {
            var errors = new Dictionary<string, string>();
            var normalized = new ParticipantInput();

            if (input == null)
            {
                input = new ParticipantInput();
            }

            normalized.FirstName = ValidateName(input.FirstName, FieldFirstName, "Ad", errors);
            normalized.LastName = ValidateName(input.LastName, FieldLastName, "Soyad", errors);

            normalized.Phone = NormalizePhone(input.Phone);
            if (string.IsNullOrEmpty(normalized.Phone))
            {
                errors[FieldPhone] = "Telefon alanı zorunludur.";
            }
            else if (!PhonePattern.IsMatch(normalized.Phone))
            {
                errors[FieldPhone] = string.Format(CultureInfo.InvariantCulture,
                    "Telefon numarası geçersiz. Başında isteğe bağlı + ile {0}-{1} rakam girin.", MinPhoneDigits, MaxPhoneDigits);
            }

            normalized.Email = (input.Email ?? string.Empty).Trim();
            if (normalized.Email.Length == 0)
            {
                errors[FieldEmail] = "E-posta alanı zorunludur.";
            }
            else if (normalized.Email.Length > MaxEmailLength || !IsValidEmail(normalized.Email))
            {
                errors[FieldEmail] = "E-posta adresi geçersiz.";
            }

            if (consentRequired)
            {
                normalized.ConsentAccepted = input.ConsentAccepted == true;
                normalized.ConsentVersion = string.IsNullOrWhiteSpace(input.ConsentVersion) ? null : input.ConsentVersion.Trim();
                if (input.ConsentAccepted != true)
                {
                    errors[FieldConsent] = "Devam etmek için aydınlatma metnini onaylamanız gerekir.";
                }
            }
            else
            {
                normalized.ConsentAccepted = null;
                normalized.ConsentVersion = null;
            }

            return new ValidationResult(errors, normalized);
        }

        /// <summary>Strips whitespace, dashes and parentheses; keeps a single leading '+'. Null-safe.</summary>
        public static string NormalizePhone(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (char.IsWhiteSpace(c) || c == '-' || c == '(' || c == ')')
                {
                    continue;
                }

                if (c == '+' && builder.Length == 0)
                {
                    builder.Append(c);
                    continue;
                }

                builder.Append(c);
            }

            return builder.ToString();
        }

        /// <summary>RFC-lite e-mail check: one '@', no whitespace, a dot in the domain, 2+ character TLD.</summary>
        public static bool IsValidEmail(string email)
        {
            return !string.IsNullOrEmpty(email) && EmailPattern.IsMatch(email);
        }

        private static string ValidateName(string raw, string field, string label, Dictionary<string, string> errors)
        {
            var value = (raw ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                errors[field] = label + " alanı zorunludur.";
            }
            else if (value.Length > MaxNameLength)
            {
                errors[field] = string.Format(CultureInfo.InvariantCulture, "{0} en fazla {1} karakter olabilir.", label, MaxNameLength);
            }

            return value;
        }
    }
}
