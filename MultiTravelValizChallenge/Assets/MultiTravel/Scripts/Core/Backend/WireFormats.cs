using MultiTravel.Core.Session;

namespace MultiTravel.Core.Backend
{
    /// <summary>String values used on the wire and in local files (ARCHITECTURE.md §4 table constraints).</summary>
    public static class WireFormats
    {
        public const string StatusCompleted = "completed";
        public const string StatusAbandoned = "abandoned";

        public const string GenderFemale = "female";
        public const string GenderMale = "male";

        public const string ReasonRequiredItemsPlaced = "required_items_placed";
        public const string ReasonManualConfirm = "manual_confirm";
        public const string ReasonTimeLimit = "time_limit";
        public const string ReasonOperatorForced = "operator_forced";

        /// <summary>"female" / "male".</summary>
        public static string GenderToWire(Gender gender)
        {
            return gender == Session.Gender.Female ? GenderFemale : GenderMale;
        }

        /// <summary>Parses "female" / "male" (case-insensitive).</summary>
        public static bool TryParseGender(string value, out Gender gender)
        {
            if (string.Equals(value, GenderFemale, System.StringComparison.OrdinalIgnoreCase))
            {
                gender = Session.Gender.Female;
                return true;
            }

            if (string.Equals(value, GenderMale, System.StringComparison.OrdinalIgnoreCase))
            {
                gender = Session.Gender.Male;
                return true;
            }

            gender = default;
            return false;
        }

        /// <summary>snake_case completion reason.</summary>
        public static string CompletionReasonToWire(CompletionReason reason)
        {
            switch (reason)
            {
                case CompletionReason.RequiredItemsPlaced:
                    return ReasonRequiredItemsPlaced;
                case CompletionReason.ManualConfirm:
                    return ReasonManualConfirm;
                case CompletionReason.TimeLimit:
                    return ReasonTimeLimit;
                default:
                    return ReasonOperatorForced;
            }
        }
    }
}
