using System;

namespace MultiTravel.Core.Session
{
    /// <summary>Categories of unrecoverable errors reported through <see cref="SessionController.ReportFatal"/>.</summary>
    public enum FatalReason
    {
        /// <summary>The Main scene could not be loaded by the bootstrap.</summary>
        SceneLoadFailed,

        /// <summary>The XR loader / OpenXR runtime could not be initialised.</summary>
        XrInitializationFailed,

        /// <summary>The effective configuration is unusable (e.g. missing catalog).</summary>
        ConfigurationInvalid,

        /// <summary>Any other unexpected error.</summary>
        Unexpected
    }

    /// <summary>Describes a fatal error for the operator screen (Turkish message) and the logs (English detail).</summary>
    public sealed class FatalInfo
    {
        public FatalInfo(FatalReason reason, string detail, DateTime occurredAtUtc)
        {
            Reason = reason;
            Detail = detail ?? string.Empty;
            OccurredAtUtc = occurredAtUtc;
            Message = MessageFor(reason);
        }

        /// <summary>Error category.</summary>
        public FatalReason Reason { get; }

        /// <summary>User-facing Turkish message.</summary>
        public string Message { get; }

        /// <summary>Technical detail for logs (English, may be empty).</summary>
        public string Detail { get; }

        /// <summary>UTC timestamp of the report.</summary>
        public DateTime OccurredAtUtc { get; }

        /// <summary>Turkish message for a reason.</summary>
        public static string MessageFor(FatalReason reason)
        {
            switch (reason)
            {
                case FatalReason.SceneLoadFailed:
                    return "Oyun sahnesi yüklenemedi. Lütfen yeniden deneyin.";
                case FatalReason.XrInitializationFailed:
                    return "VR başlığı başlatılamadı. Bağlantıyı kontrol edip yeniden deneyin.";
                case FatalReason.ConfigurationInvalid:
                    return "Uygulama yapılandırması hatalı. Lütfen teknik ekibe haber verin.";
                default:
                    return "Beklenmeyen bir hata oluştu. Lütfen yeniden deneyin.";
            }
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Detail) ? $"{Reason}: {Message}" : $"{Reason}: {Message} ({Detail})";
        }
    }
}
