using System;
using MultiTravel.Core.Session;
using Newtonsoft.Json;

namespace MultiTravel.Core.Backend
{
    /// <summary>The <c>p_participant</c> jsonb argument of <c>submit_result</c> (same fields as <c>register_participant</c>).</summary>
    public sealed class ParticipantPayload
    {
        [JsonProperty("first_name")]
        public string FirstName { get; set; }

        [JsonProperty("last_name")]
        public string LastName { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("company")]
        public string Company { get; set; }

        [JsonProperty("location")]
        public string Location { get; set; }

        [JsonProperty("phone")]
        public string Phone { get; set; }

        [JsonProperty("email")]
        public string Email { get; set; }

        /// <summary>"female" / "male".</summary>
        [JsonProperty("gender")]
        public string Gender { get; set; }

        [JsonProperty("consent_accepted")]
        public bool? ConsentAccepted { get; set; }

        [JsonProperty("consent_version")]
        public string ConsentVersion { get; set; }

        [JsonProperty("client_session_id")]
        public Guid ClientSessionId { get; set; }

        [JsonProperty("station_id")]
        public string StationId { get; set; }

        /// <summary>Builds the payload from a session whose gender has been selected.</summary>
        public static ParticipantPayload FromSession(ParticipantSession session, string stationId)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            var input = session.Input;
            return new ParticipantPayload
            {
                FirstName = input.FirstName,
                LastName = input.LastName,
                Title = input.Title,
                Company = input.Company,
                Location = input.Location,
                Phone = input.Phone,
                Email = input.Email,
                Gender = WireFormats.GenderToWire(session.Gender),
                ConsentAccepted = input.ConsentAccepted,
                ConsentVersion = input.ConsentVersion,
                ClientSessionId = session.ClientSessionId,
                StationId = stationId ?? string.Empty
            };
        }
    }
}
