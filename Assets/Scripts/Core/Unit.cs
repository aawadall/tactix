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

        /// <summary>World position (continuous). Tile (i,j) is centred at (i,j).</summary>
        [JsonProperty("x")]
        public double X { get; set; }

        [JsonProperty("y")]
        public double Y { get; set; }

        [JsonProperty("hp")]
        public int Hp { get; set; }

        /// <summary>
        /// Experience: +1 per attack made, +2 more per kill. Display/logging only
        /// in schema v2 — no gameplay effect yet.
        /// </summary>
        [JsonProperty("xp")]
        public int Xp { get; set; }

        [JsonProperty("hasMoved")]
        public bool HasMoved { get; set; }

        [JsonProperty("hasAttacked")]
        public bool HasAttacked { get; set; }

        /// <summary>
        /// Whether this unit has used its support action (heal/repair) this turn.
        /// Support has its own slot: it neither consumes the attack nor ends the
        /// army's movement phase.
        /// </summary>
        [JsonProperty("hasSupported")]
        public bool HasSupported { get; set; }

        [JsonIgnore]
        public UnitStats Stats => UnitStats.For(Type);

        public Unit Clone()
        {
            return (Unit)MemberwiseClone();
        }
    }
}
