using System;
using System.Collections.Generic;

namespace Tactix.Core
{
    /// <summary>
    /// Hardcoded v1 level: a 16x16 board, 180°-rotationally symmetric
    /// (tile (x,y) mirrors tile (W-1-x, H-1-y)). Board size lives only here —
    /// the engine and data structures are size-agnostic.
    ///
    /// The map is authored as two ASCII layers, listed top row first
    /// (row 0 of the array is y = Height-1). Terrain: '.' open, 'F' forest,
    /// '#' impassable. Elevation: digits 0-3. A central peak (3) sits on a
    /// plateau (2) ringed by slopes (1) and valley floors (0); ring steps of 1
    /// keep every slope climbable while interior shortcuts can still form
    /// cliffs against the knolls.
    /// </summary>
    public static class LevelConfig
    {
        private static readonly string[] TerrainRows =
        {
            //0123456789012345
            "................", // y15
            "....F...........", // y14
            "..F.F......#....", // y13
            ".FF.......FF....", // y12
            "....#...F.......", // y11
            "......FF....F...", // y10
            "...F......##....", // y9
            ".....F.F........", // y8
            "........F.F.....", // y7
            "....##......F...", // y6
            "...F....FF......", // y5
            ".......F...#....", // y4
            "....FF.......FF.", // y3
            "....#......F.F..", // y2
            "...........F....", // y1
            "................", // y0
        };

        private static readonly string[] ElevationRows =
        {
            //0123456789012345
            "0000000000000000", // y15
            "0000000000000000", // y14
            "0001111111111000", // y13
            "0001122112211000", // y12
            "0011222222221100", // y11
            "0011222222221100", // y10
            "0012223333222100", // y9
            "0012233333322100", // y8
            "0012233333322100", // y7
            "0012223333222100", // y6
            "0011222222221100", // y5
            "0011222222221100", // y4
            "0001122112211000", // y3
            "0001111111111000", // y2
            "0000000000000000", // y1
            "0000000000000000", // y0
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

            var units = new List<Unit>
            {
                // Player 0 (bottom, rows 0-1): 2x Infantry, 2x Mech Inf, 1x Armor, 2x Artillery, 1x Recon
                NewUnit(0, 0, UnitType.MechInfantry, 5, 1),
                NewUnit(1, 0, UnitType.Infantry, 6, 1),
                NewUnit(2, 0, UnitType.Armor, 7, 1),
                NewUnit(3, 0, UnitType.Recon, 8, 1),
                NewUnit(4, 0, UnitType.Infantry, 9, 1),
                NewUnit(5, 0, UnitType.MechInfantry, 10, 1),
                NewUnit(6, 0, UnitType.Artillery, 7, 0),
                NewUnit(7, 0, UnitType.Artillery, 8, 0),
                // Player 1 (top, rows 14-15), mirrored through 180° rotation
                NewUnit(8, 1, UnitType.MechInfantry, 10, 14),
                NewUnit(9, 1, UnitType.Infantry, 9, 14),
                NewUnit(10, 1, UnitType.Armor, 8, 14),
                NewUnit(11, 1, UnitType.Recon, 7, 14),
                NewUnit(12, 1, UnitType.Infantry, 6, 14),
                NewUnit(13, 1, UnitType.MechInfantry, 5, 14),
                NewUnit(14, 1, UnitType.Artillery, 8, 15),
                NewUnit(15, 1, UnitType.Artillery, 7, 15),
            };

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

        private static Unit NewUnit(int id, int owner, UnitType type, int x, int y)
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
