using System.Globalization;
using System.Text;

namespace MultiTravel.Core.Utility
{
    /// <summary>
    /// Elapsed-time formatting shared by the VR HUD and the operator screen.
    /// Minutes are not wrapped at 60 so very long games still display correctly ("75:02.3").
    /// </summary>
    public static class TimeFormat
    {
        /// <summary>Formats milliseconds as <c>mm:ss.f</c> (tenths), e.g. 65 300 ms → "01:05.3".</summary>
        public static string FormatTenths(long elapsedMs)
        {
            if (elapsedMs < 0)
            {
                elapsedMs = 0;
            }

            long totalTenths = elapsedMs / 100;
            long tenths = totalTenths % 10;
            long totalSeconds = totalTenths / 10;
            return Build(totalSeconds, tenths, 1);
        }

        /// <summary>Formats milliseconds as <c>mm:ss.SS</c> (hundredths), e.g. 65 370 ms → "01:05.37".</summary>
        public static string FormatHundredths(long elapsedMs)
        {
            if (elapsedMs < 0)
            {
                elapsedMs = 0;
            }

            long totalHundredths = elapsedMs / 10;
            long hundredths = totalHundredths % 100;
            long totalSeconds = totalHundredths / 100;
            return Build(totalSeconds, hundredths, 2);
        }

        /// <summary>Formats milliseconds as <c>mm:ss</c>, e.g. 65 370 ms → "01:05".</summary>
        public static string FormatSeconds(long elapsedMs)
        {
            if (elapsedMs < 0)
            {
                elapsedMs = 0;
            }

            long totalSeconds = elapsedMs / 1000;
            return Build(totalSeconds, -1, 0);
        }

        private static string Build(long totalSeconds, long fraction, int fractionDigits)
        {
            long minutes = totalSeconds / 60;
            long seconds = totalSeconds % 60;

            var builder = new StringBuilder(10);
            if (minutes < 10)
            {
                builder.Append('0');
            }

            builder.Append(minutes.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            if (seconds < 10)
            {
                builder.Append('0');
            }

            builder.Append(seconds.ToString(CultureInfo.InvariantCulture));

            if (fractionDigits > 0)
            {
                builder.Append('.');
                var text = fraction.ToString(CultureInfo.InvariantCulture);
                for (int i = text.Length; i < fractionDigits; i++)
                {
                    builder.Append('0');
                }

                builder.Append(text);
            }

            return builder.ToString();
        }
    }
}
