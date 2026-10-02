using MultiTravel.Core.Session;

namespace MultiTravel.Operator.Panels
{
    /// <summary>Turkish display strings derived from session data (operator screen only).</summary>
    public static class ParticipantText
    {
        /// <summary>"Ad Soyad" of the normalised registration input; empty when unavailable.</summary>
        public static string FullName(ParticipantInput input)
        {
            if (input == null)
            {
                return string.Empty;
            }

            var first = input.FirstName ?? string.Empty;
            var last = input.LastName ?? string.Empty;
            if (first.Length == 0)
            {
                return last;
            }

            return last.Length == 0 ? first : first + " " + last;
        }

        /// <summary>Turkish label of the selected product set.</summary>
        public static string GenderLabel(Gender gender)
        {
            return gender == Gender.Female ? "Kadın" : "Erkek";
        }

        /// <summary>Turkish explanation of why the game ended.</summary>
        public static string CompletionReasonLabel(CompletionReason reason)
        {
            switch (reason)
            {
                case CompletionReason.RequiredItemsPlaced:
                    return "Tüm gerekli ürünler valize yerleştirildi.";
                case CompletionReason.ManualConfirm:
                    return "Katılımcı valizi tamamladı.";
                case CompletionReason.TimeLimit:
                    return "Süre doldu.";
                case CompletionReason.OperatorForced:
                    return "Oyun operatör tarafından bitirildi.";
                default:
                    return string.Empty;
            }
        }
    }
}
