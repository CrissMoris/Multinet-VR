using Newtonsoft.Json;

namespace MultiTravel.Core.Backend
{
    /// <summary>PostgREST error body: <c>{ "message", "details", "hint", "code" }</c>. For RPC exceptions raised with
    /// <c>RAISE EXCEPTION USING MESSAGE = '&lt;CODE&gt;'</c>, <see cref="Message"/> carries the application code.</summary>
    public sealed class PostgrestError
    {
        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("details")]
        public string Details { get; set; }

        [JsonProperty("hint")]
        public string Hint { get; set; }

        /// <summary>SQLSTATE or PostgREST code (e.g. P0001, PGRST202).</summary>
        [JsonProperty("code")]
        public string Code { get; set; }
    }
}
