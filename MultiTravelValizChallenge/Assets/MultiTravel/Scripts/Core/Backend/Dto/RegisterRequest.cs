using System;
using Newtonsoft.Json;

namespace MultiTravel.Core.Backend
{
    /// <summary>Body of <c>POST /rest/v1/rpc/register_participant</c>; property names are the RPC parameter names.</summary>
    public sealed class RegisterRequest
    {
        [JsonProperty("p_event_slug")]
        public string EventSlug { get; set; }

        [JsonProperty("p_access_code")]
        public string AccessCode { get; set; }

        [JsonProperty("p_station_id")]
        public string StationId { get; set; }

        [JsonProperty("p_client_session_id")]
        public Guid ClientSessionId { get; set; }

        [JsonProperty("p_first_name")]
        public string FirstName { get; set; }

        [JsonProperty("p_last_name")]
        public string LastName { get; set; }

        [JsonProperty("p_phone")]
        public string Phone { get; set; }

        [JsonProperty("p_email")]
        public string Email { get; set; }

        /// <summary>"female" / "male".</summary>
        [JsonProperty("p_gender")]
        public string Gender { get; set; }

        [JsonProperty("p_consent_accepted")]
        public bool? ConsentAccepted { get; set; }

        [JsonProperty("p_consent_version")]
        public string ConsentVersion { get; set; }
    }
}
