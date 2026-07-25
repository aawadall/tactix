using Newtonsoft.Json;

namespace Tactix.Core
{
    /// <summary>
    /// Shared serializer settings so every producer (logger, tests, future tools)
    /// emits the exact same schema.
    /// </summary>
    public static class TactixJson
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Include,
            Converters = { new GameActionConverter() },
        };

        public static string Serialize(object value)
        {
            return JsonConvert.SerializeObject(value, Formatting.None, Settings);
        }

        public static T Deserialize<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json, Settings);
        }
    }
}
