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

        /// <summary>Starting formation for player 0; player 1 mirrors it through the board centre.</summary>
        private static readonly (UnitType type, double x, double y)[] Formation =
        {
            // screening line
            (UnitType.Infantry, 6.0, 2.0),
            (UnitType.Infantry, 8.0, 2.0),
            (UnitType.Infantry, 10.0, 2.0),
            (UnitType.MechInfantry, 13.0, 2.0),
            (UnitType.MechInfantry, 15.0, 2.0),
            (UnitType.MechInfantry, 17.0, 2.0),
            // armour and scouts
            (UnitType.Armor, 10.5, 1.0),
            (UnitType.Armor, 13.5, 1.0),
            (UnitType.Recon, 4.0, 1.0),
            // guns to the rear
            (UnitType.Artillery, 9.5, 0.0),
            (UnitType.Artillery, 11.5, 0.0),
            (UnitType.Artillery, 13.5, 0.0),
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

            var units = new List<Unit>();
            int id = 0;
            foreach (var (type, x, y) in Formation)
                units.Add(NewUnit(id++, 0, type, x, y));
            foreach (var (type, x, y) in Formation)
                units.Add(NewUnit(id++, 1, type, width - 1 - x, height - 1 - y));

            return new GameState
            {
                Terrain = terrain,
                Elevation = elevation,
                Units = units,
                CurrentPlayer = 0,
                TurnPhase = TurnPhase.Move,
                TurnNumber = 1,
                Winner = null,
            };
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

        private static Unit NewUnit(int id, int owner, UnitType type, double x, double y)
        {
            return new Unit
            {
                Id = id,
                Owner = owner,
                Type = type,
                X = x,
                Y = y,
                Hp = UnitStats.For(type).MaxHp,
                HasMoved = false,
                HasAttacked = false,
            };
        }
    }
}
