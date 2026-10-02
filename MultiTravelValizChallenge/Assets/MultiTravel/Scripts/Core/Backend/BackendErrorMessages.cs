using System;

namespace MultiTravel.Core.Backend
{
    /// <summary>Turkish user-facing messages for backend errors (ARCHITECTURE.md §4).</summary>
    public static class BackendErrorMessages
    {
        /// <summary>Message for every transport / unknown error.</summary>
        public const string ServerUnreachable = "Sunucuya ulaşılamadı";

        /// <summary>HTTP 401 / 403: the anon key or gateway rejected the request.</summary>
        public const string Unauthorized = "Sunucuya ulaşılamadı (yetkilendirme hatası)";

        public const string EventAccessDenied = "Etkinlik erişimi reddedildi. Etkinlik kodu veya erişim şifresi hatalı.";

        public const string EventInactive = "Etkinlik şu anda aktif değil.";

        public const string ParticipantNotFound = "Katılımcı kaydı bulunamadı.";

        public const string ConfigurationMissing = "Sunucu ayarları eksik. Lütfen yapılandırma dosyasını kontrol edin.";

        public const string OutboxCorrupt = "Yerel gönderim kaydı okunamadı.";

        /// <summary>"Geçersiz alan: {label}" for VALIDATION_FAILED:&lt;field&gt;.</summary>
        public static string ValidationFailed(string field)
        {
            return "Geçersiz alan: " + FieldLabel(field);
        }

        /// <summary>Turkish label for a backend / form field name (snake_case or camelCase).</summary>
        public static string FieldLabel(string field)
        {
            if (string.IsNullOrWhiteSpace(field))
            {
                return "bilinmeyen";
            }

            var key = field.Trim().Replace("_", string.Empty).ToLowerInvariant();
            switch (key)
            {
                case "firstname":
                    return "Ad";
                case "lastname":
                    return "Soyad";
                case "phone":
                    return "Telefon";
                case "email":
                    return "E-posta";
                case "gender":
                    return "Cinsiyet";
                case "consent":
                case "consentaccepted":
                    return "Onay";
                case "consentversion":
                    return "Onay sürümü";
                case "clientsessionid":
                    return "Oturum kimliği";
                case "submissionid":
                    return "Gönderim kimliği";
                case "stationid":
                    return "İstasyon";
                case "score":
                    return "Puan";
                case "completionms":
                    return "Süre";
                case "status":
                    return "Durum";
                case "completionreason":
                    return "Tamamlanma nedeni";
                case "completedat":
                    return "Tamamlanma zamanı";
                case "placedproductids":
                    return "Ürün listesi";
                case "eventslug":
                    return "Etkinlik";
                case "accesscode":
                    return "Erişim şifresi";
                default:
                    return field.Trim();
            }
        }

        /// <summary>Message for a known server code; null when the code is not one of the documented ones.</summary>
        public static string ForKnownCode(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return null;
            }

            switch (code)
            {
                case BackendError.CodeEventAccessDenied:
                    return EventAccessDenied;
                case BackendError.CodeEventInactive:
                    return EventInactive;
                case BackendError.CodeParticipantNotFound:
                    return ParticipantNotFound;
                default:
                    if (code.StartsWith(BackendError.CodeValidationFailedPrefix, StringComparison.Ordinal))
                    {
                        return ValidationFailed(BackendError.ValidationFieldOf(code));
                    }

                    return null;
            }
        }
    }
}
