namespace MultiTravel.Core.Session
{
    /// <summary>
    /// Raw registration form values (ARCHITECTURE.md §2.3). Instances coming from the operator form are
    /// normalised by <see cref="ParticipantValidator"/> before they are stored on a <see cref="ParticipantSession"/>.
    /// </summary>
    public sealed class ParticipantInput
    {
        public string FirstName { get; set; }

        public string LastName { get; set; }

        /// <summary>Phone number; normalised to an optional leading '+' followed by 10..15 digits.</summary>
        public string Phone { get; set; }

        public string Email { get; set; }

        /// <summary>Null when consent is not collected (empty consent text), otherwise the checkbox value.</summary>
        public bool? ConsentAccepted { get; set; }

        /// <summary>Version of the consent wording the participant accepted; null when consent is not collected.</summary>
        public string ConsentVersion { get; set; }

        /// <summary>Shallow copy.</summary>
        public ParticipantInput Clone()
        {
            return new ParticipantInput
            {
                FirstName = FirstName,
                LastName = LastName,
                Phone = Phone,
                Email = Email,
                ConsentAccepted = ConsentAccepted,
                ConsentVersion = ConsentVersion
            };
        }
    }
}
