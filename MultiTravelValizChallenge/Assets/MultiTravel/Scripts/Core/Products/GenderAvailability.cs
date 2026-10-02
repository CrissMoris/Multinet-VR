using System;
using MultiTravel.Core.Session;

namespace MultiTravel.Core.Products
{
    /// <summary>Which gender variants a product appears in (ARCHITECTURE.md §2.4).</summary>
    [Flags]
    public enum GenderAvailability
    {
        None = 0,
        Female = 1,
        Male = 2,
        Both = Female | Male
    }

    public static class GenderAvailabilityExtensions
    {
        /// <summary>True when the availability flags include the given gender.</summary>
        public static bool Includes(this GenderAvailability availability, Gender gender)
        {
            var flag = gender == Gender.Female ? GenderAvailability.Female : GenderAvailability.Male;
            return (availability & flag) != 0;
        }

        /// <summary>Converts a gender to its single availability flag.</summary>
        public static GenderAvailability ToAvailability(this Gender gender)
        {
            return gender == Gender.Female ? GenderAvailability.Female : GenderAvailability.Male;
        }
    }
}
