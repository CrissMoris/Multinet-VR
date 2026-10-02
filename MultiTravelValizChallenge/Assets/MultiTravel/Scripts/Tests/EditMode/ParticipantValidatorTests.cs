using MultiTravel.Core.Session;
using MultiTravel.Tests.EditMode.Fakes;
using NUnit.Framework;

namespace MultiTravel.Tests.EditMode
{
    public sealed class ParticipantValidatorTests
    {
        [Test]
        public void ValidInput_IsAccepted_AndNormalized()
        {
            var result = ParticipantValidator.Validate(TestData.ValidInput(), consentRequired: false);

            Assert.IsTrue(result.IsValid, result.FirstError);
            Assert.AreEqual(0, result.Errors.Count);
            Assert.AreEqual("Ayşe", result.Normalized.FirstName);
            Assert.AreEqual("Yılmaz", result.Normalized.LastName);
            Assert.AreEqual("+905321234567", result.Normalized.Phone);
            Assert.AreEqual("ayse@example.com", result.Normalized.Email);
            Assert.IsNull(result.Normalized.ConsentAccepted);
            Assert.IsNull(result.Normalized.ConsentVersion);
            Assert.IsNull(result.FirstError);
        }

        [Test]
        public void EmptyFirstName_ReportsTurkishMessage()
        {
            var input = TestData.ValidInput();
            input.FirstName = "   ";

            var result = ParticipantValidator.Validate(input, false);

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.TryGetError(ParticipantValidator.FieldFirstName, out var message));
            Assert.AreEqual("Ad alanı zorunludur.", message);
            Assert.AreEqual(message, result.FirstError);
        }

        [Test]
        public void TooLongLastName_IsRejected()
        {
            var input = TestData.ValidInput();
            input.LastName = new string('a', ParticipantValidator.MaxNameLength + 1);

            var result = ParticipantValidator.Validate(input, false);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual("Soyad en fazla 60 karakter olabilir.", result.Errors[ParticipantValidator.FieldLastName]);

            input.LastName = new string('a', ParticipantValidator.MaxNameLength);
            Assert.IsTrue(ParticipantValidator.Validate(input, false).IsValid);
        }

        [TestCase("+90 532 123 45 67", true)]
        [TestCase("05321234567", true)]
        [TestCase("+1 (555) 010-0199", true)]
        [TestCase("123456789012345", true)]
        [TestCase("+123456789012345", true)]
        [TestCase("1234567890123456", false)]
        [TestCase("123456789", false)]
        [TestCase("abc1234567", false)]
        [TestCase("+90 532 123 45 6a", false)]
        [TestCase("5321234567+", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void Phone_IsValidatedAfterNormalisation(string phone, bool expectedValid)
        {
            var input = TestData.ValidInput();
            input.Phone = phone;

            var result = ParticipantValidator.Validate(input, false);

            Assert.AreEqual(expectedValid, !result.Errors.ContainsKey(ParticipantValidator.FieldPhone), result.FirstError);
            if (!expectedValid)
            {
                Assert.AreEqual(
                    string.IsNullOrWhiteSpace(phone) ? "Telefon alanı zorunludur." : "Telefon numarası geçersiz. Başında isteğe bağlı + ile 10-15 rakam girin.",
                    result.Errors[ParticipantValidator.FieldPhone]);
            }
        }

        [TestCase("+90 (532) 123-45-67", "+905321234567")]
        [TestCase("  0532 123 45 67 ", "05321234567")]
        [TestCase("532-123-4567", "5321234567")]
        [TestCase("+1\t555", "+1555")]
        [TestCase(null, "")]
        public void NormalizePhone_StripsSeparators(string raw, string expected)
        {
            Assert.AreEqual(expected, ParticipantValidator.NormalizePhone(raw));
        }

        [TestCase("ayse@example.com", true)]
        [TestCase("a.b+tag@sub.example.co", true)]
        [TestCase("ayse@example", false)]
        [TestCase("ayse example@example.com", false)]
        [TestCase("@example.com", false)]
        [TestCase("ayse@", false)]
        [TestCase("", false)]
        public void Email_IsValidated(string email, bool expectedValid)
        {
            var input = TestData.ValidInput();
            input.Email = email;

            var result = ParticipantValidator.Validate(input, false);

            Assert.AreEqual(expectedValid, !result.Errors.ContainsKey(ParticipantValidator.FieldEmail), result.FirstError);
            if (!expectedValid)
            {
                Assert.AreEqual(
                    string.IsNullOrEmpty(email) ? "E-posta alanı zorunludur." : "E-posta adresi geçersiz.",
                    result.Errors[ParticipantValidator.FieldEmail]);
            }
        }

        [Test]
        public void ConsentRequired_RejectsMissingConsent()
        {
            var input = TestData.ValidInput();
            input.ConsentAccepted = false;
            var declined = ParticipantValidator.Validate(input, consentRequired: true);
            Assert.IsFalse(declined.IsValid);
            Assert.AreEqual("Devam etmek için aydınlatma metnini onaylamanız gerekir.", declined.Errors[ParticipantValidator.FieldConsent]);

            input.ConsentAccepted = null;
            Assert.IsFalse(ParticipantValidator.Validate(input, true).IsValid);

            input.ConsentAccepted = true;
            input.ConsentVersion = " 2026-01 ";
            var accepted = ParticipantValidator.Validate(input, true);
            Assert.IsTrue(accepted.IsValid);
            Assert.AreEqual(true, accepted.Normalized.ConsentAccepted);
            Assert.AreEqual("2026-01", accepted.Normalized.ConsentVersion);
        }

        [Test]
        public void ConsentNotRequired_IgnoresCheckbox()
        {
            var input = TestData.ValidInput();
            input.ConsentAccepted = false;
            input.ConsentVersion = "x";

            var result = ParticipantValidator.Validate(input, consentRequired: false);

            Assert.IsTrue(result.IsValid);
            Assert.IsNull(result.Normalized.ConsentAccepted);
            Assert.IsNull(result.Normalized.ConsentVersion);
        }

        [Test]
        public void NullInput_ReportsEveryRequiredField_InFormOrder()
        {
            var result = ParticipantValidator.Validate(null, consentRequired: true);

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(5, result.Errors.Count);
            Assert.AreEqual("Ad alanı zorunludur.", result.FirstError);
            Assert.IsTrue(result.Errors.ContainsKey(ParticipantValidator.FieldConsent));
        }
    }
}
