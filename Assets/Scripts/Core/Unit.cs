using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Tactix.Core
{
    /// <summary>
    /// A unit entity. JSON property names are part of the logged schema — do not rename.
    /// </summary>
    public sealed class Unit
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        /// <summary>Owning player: 0 or 1.</summary>
        [JsonProperty("owner")]
        public int Owner { get; set; }

        [JsonProperty("type")]
        [JsonConverter(typeof(StringEnumConverter), true)] // camelCase string
        public UnitType Type { get; set; }

        [JsonProperty("x")]
        public int X { get; set; }

        [JsonProperty("y")]
        public int Y { get; set; }

        [JsonProperty("hp")]
        public int Hp { get; set; }

        [JsonProperty("hasMoved")]
        public bool HasMoved { get; set; }

        [JsonProperty("hasAttacked")]
        public bool HasAttacked { get; set; }

        [JsonIgnore]
        public UnitStats Stats => UnitStats.For(Type);

        public Unit Clone()
        {
            return (Unit)MemberwiseClone();
        }
    }
}
