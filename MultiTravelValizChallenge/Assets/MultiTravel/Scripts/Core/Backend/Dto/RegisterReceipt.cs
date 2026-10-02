using System;
using Newtonsoft.Json;

namespace MultiTravel.Core.Backend
{
    /// <summary>Response of <c>register_participant</c>: <c>{ "participant_id": uuid, "created": bool }</c>.</summary>
    public sealed class RegisterReceipt
    {
        [JsonProperty("participant_id")]
        public Guid ParticipantId { get; set; }

        /// <summary>False when the client session id was already registered (idempotent upsert).</summary>
        [JsonProperty("created")]
        public bool Created { get; set; }
    }
}
