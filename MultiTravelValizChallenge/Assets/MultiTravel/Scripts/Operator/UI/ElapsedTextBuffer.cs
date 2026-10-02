using TMPro;

namespace MultiTravel.Operator.UI
{
    /// <summary>
    /// Allocation-free live timer text: writes <c>mm:ss.f</c> (same format as <c>MultiTravel.Core.Utility.TimeFormat.FormatTenths</c>)
    /// into a reused char buffer and pushes it with <see cref="TMP_Text.SetText(char[], int, int)"/> only when the tenth changes.
    /// </summary>
    public sealed class ElapsedTextBuffer
    {
        private readonly char[] buffer = new char[32];
        private readonly char[] digits = new char[20];
        private long lastTenths = -1;

        /// <summary>Forces the next <see cref="Apply"/> to write even when the value is unchanged.</summary>
        public void Invalidate()
        {
            lastTenths = -1;
        }

        /// <summary>Updates <paramref name="label"/> when the displayed tenth changed. Returns true when the text was written.</summary>
        public bool Apply(TMP_Text label, long elapsedMs)
        {
            if (label == null)
            {
                return false;
            }

            if (elapsedMs < 0)
            {
                elapsedMs = 0;
            }

            long totalTenths = elapsedMs / 100;
            if (totalTenths == lastTenths)
            {
                return false;
            }

            lastTenths = totalTenths;
            long tenths = totalTenths % 10;
            long totalSeconds = totalTenths / 10;
            long minutes = totalSeconds / 60;
            long seconds = totalSeconds % 60;

            int length = 0;
            if (minutes < 10)
            {
                buffer[length++] = '0';
            }

            length = AppendNumber(minutes, length);
            buffer[length++] = ':';
            buffer[length++] = (char)('0' + (int)(seconds / 10));
            buffer[length++] = (char)('0' + (int)(seconds % 10));
            buffer[length++] = '.';
            buffer[length++] = (char)('0' + (int)tenths);

            label.SetText(buffer, 0, length);
            return true;
        }

        private int AppendNumber(long value, int length)
        {
            int count = 0;
            do
            {
                digits[count++] = (char)('0' + (int)(value % 10));
                value /= 10;
            }
            while (value > 0 && count < digits.Length);

            while (count > 0 && length < buffer.Length)
            {
                buffer[length++] = digits[--count];
            }

            return length;
        }
    }
}
