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

        /// <summary>Which scale-driven uncertainties are active for this game.</summary>
        [JsonProperty("ruleset")]
        public Ruleset Ruleset { get; set; } = Ruleset.Standard;

        /// <summary>Player whose turn it is: 0 or 1.</summary>
        [JsonProperty("currentPlayer")]
        public int CurrentPlayer { get; set; }

        [JsonProperty("turnPhase")]
        [JsonConverter(typeof(StringEnumConverter), true)] // camelCase string
        public TurnPhase TurnPhase { get; set; }

        /// <summary>1-based ply counter: increments every time a player ends their turn.</summary>
        [JsonProperty("turnNumber")]
        public int TurnNumber { get; set; } = 1;

        /// <summary>Key ground contested for points.</summary>
        [JsonProperty("objectives")]
        public List<Objective> Objectives { get; set; } = new List<Objective>();

        /// <summary>Victory points, indexed by player.</summary>
        [JsonProperty("score")]
        public int[] Score { get; set; } = { 0, 0 };

        /// <summary>
        /// Each side's combat strength at deployment (sum of max HP), used as the
        /// baseline for the rout threshold.
        /// </summary>
        [JsonProperty("startingStrength")]
        public int[] StartingStrength { get; set; } = { 0, 0 };

        /// <summary>Ply count after which the game is decided on points; null for unlimited.</summary>
        [JsonProperty("turnLimit")]
        public int? TurnLimit { get; set; }

        /// <summary>Fraction of starting strength below which an army breaks.</summary>
        [JsonProperty("routThreshold")]
        public double RoutThreshold { get; set; } = 0.25;

        /// <summary>Winning player (0 or 1); null while in progress *and* on a draw.</summary>
        [JsonProperty("winner")]
        public int? Winner { get; set; }

        /// <summary>
        /// How the game ended, or null while it is still running. Prefer
        /// <see cref="IsOver"/> over checking <see cref="Winner"/>, which is also
        /// null for a draw.
        /// </summary>
        [JsonProperty("outcome")]
        [JsonConverter(typeof(StringEnumConverter), true)]
        public GameOutcome? Outcome { get; set; }

        [JsonIgnore]
        public bool IsOver => Outcome.HasValue;

        [JsonIgnore]
        public int Height => Terrain.Length;

        [JsonIgnore]
        public int Width => Terrain.Length > 0 ? Terrain[0].Length : 0;

        public bool IsInBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public TerrainType TerrainAt(int x, int y) => Terrain[y][x];

        /// <summary>Elevation at a tile; 0 when no elevation layer is present (pre-v3 states).</summary>
        public int ElevationAt(int x, int y) => Elevation != null ? Elevation[y][x] : 0;

        /// <summary>
        /// Terrain under a world position. Tile indices are clamped, so points
        /// exactly on the board's outer edge sample the edge tile.
        /// </summary>
        public TerrainType TerrainAtPoint(double x, double y) =>
            TerrainAt(ClampTileX(Geometry.TileIndex(x)), ClampTileY(Geometry.TileIndex(y)));

        /// <summary>Elevation under a world position (tile indices clamped as above).</summary>
        public int ElevationAtPoint(double x, double y) =>
            ElevationAt(ClampTileX(Geometry.TileIndex(x)), ClampTileY(Geometry.TileIndex(y)));

        private int ClampTileX(int i) => i < 0 ? 0 : i >= Width ? Width - 1 : i;

        private int ClampTileY(int j) => j < 0 ? 0 : j >= Height ? Height - 1 : j;

        public Unit GetUnit(int unitId) => Units.FirstOrDefault(u => u.Id == unitId);

        /// <summary>A player's surviving combat strength: the sum of their units' maximum HP.</summary>
        public int StrengthOf(int player) => Units.Where(u => u.Owner == player).Sum(u => u.Stats.MaxHp);

        /// <summary>The unit whose body covers the given world position, if any.</summary>
        public Unit GetUnitAtPoint(double x, double y)
        {
            return Units.FirstOrDefault(u => Geometry.Distance(u.X, u.Y, x, y) <= u.Stats.Radius);
        }

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
                Objectives = Objectives.Select(o => o.Clone()).ToList(),
                Score = (int[])Score.Clone(),
                StartingStrength = (int[])StartingStrength.Clone(),
                TurnLimit = TurnLimit,
                RoutThreshold = RoutThreshold,
                Ruleset = Ruleset?.Clone() ?? Ruleset.Deterministic,
                CurrentPlayer = CurrentPlayer,
                TurnPhase = TurnPhase,
                TurnNumber = TurnNumber,
                Winner = Winner,
                Outcome = Outcome,
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
