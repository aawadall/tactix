using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Tactix.Core
{
    /// <summary>Which part of the active player's turn we are in. The first attack ends movement for all units.</summary>
    public enum TurnPhase
    {
        Move = 0,
        Attack = 1,
    }

    /// <summary>
    /// Complete, serializable game state. The terrain grid is arbitrary-size (board
    /// dimensions come from the array, never from constants). JSON property names
    /// are part of the logged schema — do not rename.
    /// </summary>
    public sealed class GameState
    {
        /// <summary>
        /// Terrain as rows of columns: terrain[y][x], serialized as integer codes
        /// (0 = open, 1 = forest, 2 = impassable). Row 0 is the "bottom" of the board.
        /// </summary>
        [JsonProperty("terrain")]
        public TerrainType[][] Terrain { get; set; }

        /// <summary>
        /// Topographic layer: elevation[y][x] in whole levels (0-3 on the standard
        /// map), same shape as <see cref="Terrain"/>. Affects movement (steps of
        /// |Δelev| &gt; 1 are cliffs), combat (+1 damage from higher ground), and
        /// line of sight (see <see cref="LineOfSight"/>).
        /// </summary>
        [JsonProperty("elevation")]
        public int[][] Elevation { get; set; }

        [JsonProperty("units")]
        public List<Unit> Units { get; set; } = new List<Unit>();

        /// <summary>Player whose turn it is: 0 or 1.</summary>
        [JsonProperty("currentPlayer")]
        public int CurrentPlayer { get; set; }

        [JsonProperty("turnPhase")]
        [JsonConverter(typeof(StringEnumConverter), true)] // camelCase string
        public TurnPhase TurnPhase { get; set; }

        /// <summary>1-based ply counter: increments every time a player ends their turn.</summary>
        [JsonProperty("turnNumber")]
        public int TurnNumber { get; set; } = 1;

        /// <summary>Winning player (0 or 1), or null while the game is in progress.</summary>
        [JsonProperty("winner")]
        public int? Winner { get; set; }

        [JsonIgnore]
        public int Height => Terrain.Length;

        [JsonIgnore]
        public int Width => Terrain.Length > 0 ? Terrain[0].Length : 0;

        public bool IsInBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public TerrainType TerrainAt(int x, int y) => Terrain[y][x];

        /// <summary>Elevation at a tile; 0 when no elevation layer is present (pre-v3 states).</summary>
        public int ElevationAt(int x, int y) => Elevation != null ? Elevation[y][x] : 0;

        public Unit GetUnit(int unitId) => Units.FirstOrDefault(u => u.Id == unitId);

        public Unit GetUnitAt(int x, int y) => Units.FirstOrDefault(u => u.X == x && u.Y == y);

        public GameState Clone()
        {
            var terrain = new TerrainType[Terrain.Length][];
            for (int y = 0; y < Terrain.Length; y++)
                terrain[y] = (TerrainType[])Terrain[y].Clone();

            int[][] elevation = null;
            if (Elevation != null)
            {
                elevation = new int[Elevation.Length][];
                for (int y = 0; y < Elevation.Length; y++)
                    elevation[y] = (int[])Elevation[y].Clone();
            }

            return new GameState
            {
                Terrain = terrain,
                Elevation = elevation,
                Units = Units.Select(u => u.Clone()).ToList(),
                CurrentPlayer = CurrentPlayer,
                TurnPhase = TurnPhase,
                TurnNumber = TurnNumber,
                Winner = Winner,
            };
        }

        public string ToJson(bool indented = false)
        {
            return JsonConvert.SerializeObject(this, indented ? Formatting.Indented : Formatting.None, TactixJson.Settings);
        }

        public static GameState FromJson(string json)
        {
            return JsonConvert.DeserializeObject<GameState>(json, TactixJson.Settings);
        }
    }
}
