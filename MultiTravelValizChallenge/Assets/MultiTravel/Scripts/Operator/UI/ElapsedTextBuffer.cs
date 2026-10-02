using TMPro;

namespace MultiTravel.Operator.UI
{
    /// <summary>
    /// Allocation-free live timer text: writes <c>mm:ss.f</c> (same format as <c>MultiTravel.Core.Utility.TimeFormat.FormatTenths</c>)
    /// into a reused char buffer and pushes it with <see cref="TMP_Text.SetText(char[], int, int)"/> only when the tenth changes.
    /// With <c>tabular</c> every digit group is wrapped in <c>&lt;mspace&gt;</c> so the timer never jitters; the target label
    /// must have rich text enabled in that case.
    /// </summary>
    public sealed class ElapsedTextBuffer
    {
        private const string MspaceOpen = OperatorUiStyle.TabularOpen;
        private const string MspaceClose = OperatorUiStyle.TabularClose;

        private readonly char[] buffer = new char[160];
        private readonly char[] digits = new char[20];
        private readonly bool tabular;
        private long lastTenths = -1;

        public ElapsedTextBuffer(bool tabular = false)
        {
            this.tabular = tabular;
        }

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

            int length = Format(elapsedMs, out bool changed);
            if (!changed)
            {
                return false;
            }

            label.SetText(buffer, 0, length);
            return true;
        }

        /// <summary>Formats into the internal buffer (exposed for tests). Returns the length; <paramref name="changed"/> is false when unchanged.</summary>
        public int Format(long elapsedMs, out bool changed)
        {
            if (elapsedMs < 0)
            {
                elapsedMs = 0;
            }

            long totalTenths = elapsedMs / 100;
            if (totalTenths == lastTenths)
            {
                changed = false;
                return 0;
            }

            changed = true;
            lastTenths = totalTenths;
            long tenths = totalTenths % 10;
            long totalSeconds = totalTenths / 10;
            long minutes = totalSeconds / 60;
            long seconds = totalSeconds % 60;

            int length = 0;
            if (tabular)
            {
                length = Append(MspaceOpen, length);
            }

            if (minutes < 10)
            {
                buffer[length++] = '0';
            }

            length = AppendNumber(minutes, length);
            if (tabular)
            {
                length = Append(MspaceClose, length);
            }

            buffer[length++] = ':';
            if (tabular)
            {
                length = Append(MspaceOpen, length);
            }

            buffer[length++] = (char)('0' + (int)(seconds / 10));
            buffer[length++] = (char)('0' + (int)(seconds % 10));
            if (tabular)
            {
                length = Append(MspaceClose, length);
            }

            buffer[length++] = '.';
            if (tabular)
            {
                length = Append(MspaceOpen, length);
            }

            buffer[length++] = (char)('0' + (int)tenths);
            if (tabular)
            {
                length = Append(MspaceClose, length);
            }

            return length;
        }

        /// <summary>Text produced by the last <see cref="Format"/> (allocates; tests only).</summary>
        public string LastText(int length)
        {
            return new string(buffer, 0, length);
        }

        private int Append(string text, int length)
        {
            for (int i = 0; i < text.Length && length < buffer.Length; i++)
            {
                buffer[length++] = text[i];
            }

            return length;
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
