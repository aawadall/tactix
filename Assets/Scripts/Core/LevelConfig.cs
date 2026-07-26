using System;
using System.Collections.Generic;

namespace Tactix.Core
{
    /// <summary>
    /// The hardcoded standard level: a 24x24 map, 180°-rotationally symmetric
    /// (tile (x,y) mirrors tile (W-1-x, H-1-y)). Board size lives only here —
    /// the engine and data structures are size-agnostic.
    ///
    /// The relief was produced offline by fractal value noise, symmetrized,
    /// quantized to four elevation bands, and repaired so every tile stays
    /// reachable under the cliff rule; the result is baked in below, so the
    /// shipped map is fixed with no runtime randomness. Layers are listed top
    /// row first (row 0 of the array is y = Height-1). Terrain: '.' open,
    /// 'F' forest, '#' impassable. Elevation: digits 0-3.
    /// </summary>
    public static class LevelConfig
    {
        private static readonly string[] TerrainRows =
        {
            "........................", // y23
            "........................", // y22
            "F.................F.....", // y21
            "F................FFF....", // y20
            "...............FFFFF....", // y19
            "...............FFFFF....", // y18
            "...............FF.......", // y17
            "FF......................", // y16
            "FFFF...................F", // y15
            "FF.....F............FF..", // y14
            "F.....FFF.......FFFFFFF.", // y13
            "FF.FFFFFF.......FFFFFFF.", // y12
            ".FFFFFFF.......FFFFFF.FF", // y11
            ".FFFFFFF.......FFF.....F", // y10
            "..FF............F.....FF", // y9
            "F...................FFFF", // y8
            "......................FF", // y7
            ".......FF...............", // y6
            "....FFFFF...............", // y5
            "....FFFFF...............", // y4
            "....FFF................F", // y3
            ".....F.................F", // y2
            "........................", // y1
            "........................", // y0
        };

        private static readonly string[] ElevationRows =
        {
            "000000000000000000000000", // y23
            "000000000000000000000000", // y22
            "000000000000000000000000", // y21
            "000000111100000000000011", // y20
            "000001122110000000011111", // y19
            "000011122211100111122111", // y18
            "000111122222212223332111", // y17
            "011111111111223333332211", // y16
            "111111011111223333333221", // y15
            "001111111112223333332111", // y14
            "000111111122222222222110", // y13
            "000112211223322222221000", // y12
            "000122222223322112211000", // y11
            "011222222222221111111000", // y10
            "111233333322211111111100", // y9
            "122333333322111110111111", // y8
            "112233333322111111111110", // y7
            "111233322212222221111000", // y6
            "111221111001112221110000", // y5
            "111110000000011221100000", // y4
            "110000000000001111000000", // y3
            "000000000000000000000000", // y2
            "000000000000000000000000", // y1
            "000000000000000000000000", // y0
        };

        /// <summary>
        /// Starting formation for player 0, authored for the 24-wide standard map;
        /// <see cref="DeployStandardArmies"/> re-centres it on any board width.
        /// Player 1 mirrors it through the board centre.
        /// </summary>
        /// <summary>
        /// A mixed order of battle: a brigade-weight anchor, companies holding the
        /// line, and small fast elements screening. Echelon is free per unit — the
        /// full ladder from fire team to theatre exists in the data model — but a
        /// playable starting force spans only a few steps of it.
        /// </summary>
        private static readonly (UnitType type, Echelon echelon, double x, double y)[] Formation =
        {
            // screening line
            (UnitType.Infantry, Echelon.Company, 6.0, 2.0),
            (UnitType.Infantry, Echelon.Company, 8.0, 2.0),
            (UnitType.Infantry, Echelon.Battalion, 10.0, 2.0),
            (UnitType.MechInfantry, Echelon.Company, 13.0, 2.0),
            (UnitType.MechInfantry, Echelon.Battalion, 15.0, 2.0),
            (UnitType.MechInfantry, Echelon.Platoon, 17.0, 2.0),
            // armour: one heavy formation, one manoeuvre element
            (UnitType.Armor, Echelon.Brigade, 10.5, 1.0),
            (UnitType.Armor, Echelon.Company, 13.5, 1.0),
            // scouts run small and fast
            (UnitType.Recon, Echelon.Platoon, 4.0, 1.0),
            // guns to the rear
            (UnitType.Artillery, Echelon.Company, 9.5, 0.0),
            (UnitType.Artillery, Echelon.Battalion, 11.5, 0.0),
            (UnitType.Artillery, Echelon.Company, 13.5, 0.0),
            // support echelon, tucked behind the line
            (UnitType.Medic, Echelon.Section, 7.5, 1.0),
            (UnitType.Service, Echelon.Company, 16.0, 0.6),
        };

        public static GameState CreateStandardGame()
        {
            int height = TerrainRows.Length;
            int width = TerrainRows[0].Length;

            var terrain = new TerrainType[height][];
            var elevation = new int[height][];
            for (int y = 0; y < height; y++)
            {
                terrain[y] = new TerrainType[width];
                elevation[y] = new int[width];
                string terrainRow = TerrainRows[height - 1 - y];
                string elevationRow = ElevationRows[height - 1 - y];
                for (int x = 0; x < width; x++)
                {
                    terrain[y][x] = ParseTerrain(terrainRow[x]);
                    elevation[y][x] = elevationRow[x] - '0';
                }
            }

            var state = new GameState
            {
                Terrain = terrain,
                Elevation = elevation,
                Units = new List<Unit>(),
                Ruleset = Ruleset.Standard,
                CurrentPlayer = 0,
                TurnPhase = TurnPhase.Move,
                TurnNumber = 1,
                Winner = null,
            };
            DeployStandardArmies(state);
            return state;
        }

        /// <summary>
        /// Places both armies on an already-built board: player 0 along the bottom
        /// edge and player 1 mirrored through the board centre, so the deployment
        /// is symmetric on any map. The formation is re-centred (and compressed if
        /// the board is narrow) to fit the board width.
        /// </summary>
        public static void DeployStandardArmies(GameState state)
        {
            const double authoredCentre = 11.5; // centre of the 24-wide standard map
            double centre = (state.Width - 1) / 2.0;

            double widestOffset = 0;
            foreach (var entry in Formation)
                widestOffset = Math.Max(widestOffset, Math.Abs(entry.x - authoredCentre));

            double usableHalfWidth = centre - 0.5;
            double scale = widestOffset > usableHalfWidth ? usableHalfWidth / widestOffset : 1.0;

            state.Units.Clear();
            int id = 0;
            foreach (var (type, echelon, x, y) in Formation)
                state.Units.Add(NewUnit(id++, 0, type, echelon, centre + (x - authoredCentre) * scale, y));
            foreach (var (type, echelon, x, y) in Formation)
                state.Units.Add(NewUnit(id++, 1, type, echelon,
                    centre - (x - authoredCentre) * scale, state.Height - 1 - y));

            ValidateDeployment(state);
        }

        /// <summary>Fails loudly rather than starting a game from an illegal position.</summary>
        private static void ValidateDeployment(GameState state)
        {
            foreach (var unit in state.Units)
            {
                if (state.TerrainAtPoint(unit.X, unit.Y) == TerrainType.Impassable)
                    throw new InvalidOperationException($"Unit {unit.Id} deployed inside impassable terrain at ({unit.X},{unit.Y})");
                foreach (var other in state.Units)
                {
                    if (other.Id == unit.Id) continue;
                    double separation = Geometry.Distance(unit.X, unit.Y, other.X, other.Y);
                    if (separation < unit.Stats.Radius + other.Stats.Radius)
                        throw new InvalidOperationException($"Units {unit.Id} and {other.Id} deployed overlapping");
                }
            }
        }

        private static TerrainType ParseTerrain(char c)
        {
            switch (c)
            {
                case '.': return TerrainType.Open;
                case 'F': return TerrainType.Forest;
                case '#': return TerrainType.Impassable;
                default: throw new InvalidOperationException($"Unknown terrain char '{c}'");
            }
        }

        private static Unit NewUnit(int id, int owner, UnitType type, Echelon echelon, double x, double y)
        {
            return new Unit
            {
                Id = id,
                Owner = owner,
                Type = type,
                Echelon = echelon,
                X = x,
                Y = y,
                Hp = UnitStats.For(type, echelon).MaxHp,
                HasMoved = false,
                HasAttacked = false,
            };
        }
    }
}
