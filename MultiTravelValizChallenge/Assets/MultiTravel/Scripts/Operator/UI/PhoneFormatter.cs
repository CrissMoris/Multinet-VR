using System.Text;

namespace MultiTravel.Operator.UI
{
    /// <summary>
    /// Live phone number formatting for the registration form. Turkish mobile numbers starting with 0 become
    /// <c>0XXX XXX XX XX</c>; numbers typed without the leading zero use <c>XXX XXX XX XX</c>; international numbers
    /// (leading +) become <c>+CC XXX XXX XX XX</c>. Digits are never changed or dropped (the validator decides what is valid),
    /// only spaces are inserted, and the result never ends with a space.
    /// </summary>
    public static class PhoneFormatter
    {
        public const int MaxDigits = 15;

        private static readonly int[] DomesticWithZero = { 4, 3, 2, 2 };
        private static readonly int[] DomesticWithoutZero = { 3, 3, 2, 2 };
        private static readonly int[] International = { 2, 3, 3, 2, 2 };

        public static string Format(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            bool plus = false;
            var digits = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c >= '0' && c <= '9')
                {
                    if (digits.Length < MaxDigits)
                    {
                        digits.Append(c);
                    }
                }
                else if (c == '+' && digits.Length == 0)
                {
                    plus = true;
                }
            }

            if (digits.Length == 0)
            {
                return plus ? "+" : string.Empty;
            }

            int[] groups = plus ? International : (digits[0] == '0' ? DomesticWithZero : DomesticWithoutZero);
            var result = new StringBuilder(digits.Length + 6);
            if (plus)
            {
                result.Append('+');
            }

            int position = 0;
            for (int g = 0; g < groups.Length && position < digits.Length; g++)
            {
                if (g > 0)
                {
                    result.Append(' ');
                }

                int take = System.Math.Min(groups[g], digits.Length - position);
                result.Append(digits.ToString(position, take));
                position += take;
            }

            if (position < digits.Length)
            {
                result.Append(' ').Append(digits.ToString(position, digits.Length - position));
            }

            return result.ToString();
        }
    }
}
