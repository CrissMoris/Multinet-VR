using Newtonsoft.Json;

namespace MultiTravel.Core.Backend
{
    /// <summary>
    /// Shared Newtonsoft settings for RPC bodies, responses and outbox files: every property is written (PostgREST
    /// resolves functions by the full named-parameter set, so nulls must be present), dates are ISO 8601 UTC.
    /// </summary>
    public static class BackendJson
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Include,
            DefaultValueHandling = DefaultValueHandling.Include,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            DateParseHandling = DateParseHandling.DateTime,
            DateFormatString = "yyyy-MM-dd'T'HH:mm:ss.fffK",
            Formatting = Formatting.None
        };

        public static string Serialize(object value)
        {
            return JsonConvert.SerializeObject(value, Settings);
        }

        public static T Deserialize<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json, Settings);
        }
    }
}
