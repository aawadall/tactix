using System.Collections.Generic;

namespace Tactix.Core
{
    /// <summary>
    /// Hardcoded v1 level: an 8x8 board, 180°-rotationally symmetric
    /// (tile (x,y) mirrors tile (W-1-x, H-1-y)). Board size lives only here —
    /// the engine and data structures are size-agnostic.
    /// </summary>
    public static class LevelConfig
    {
        public const int Width = 8;
        public const int Height = 8;

        private static readonly (int x, int y)[] Forests =
        {
            (2, 3), (3, 3), (4, 4), (5, 4), (1, 5), (6, 2),
        };

        private static readonly (int x, int y)[] Impassables =
        {
            (3, 5), (4, 2), (0, 4), (7, 3),
        };

        public static GameState CreateStandardGame()
        {
            var terrain = new TerrainType[Height][];
            for (int y = 0; y < Height; y++)
                terrain[y] = new TerrainType[Width];

            foreach (var (x, y) in Forests) terrain[y][x] = TerrainType.Forest;
            foreach (var (x, y) in Impassables) terrain[y][x] = TerrainType.Impassable;

            var units = new List<Unit>
            {
                // Player 0 (bottom, rows 0-1): 2x Infantry, 1x Mech Inf, 1x Armor, 1x Artillery, 1x Recon
                NewUnit(0, 0, UnitType.Infantry, 2, 1),
                NewUnit(1, 0, UnitType.Infantry, 5, 1),
                NewUnit(2, 0, UnitType.MechInfantry, 4, 1),
                NewUnit(3, 0, UnitType.Armor, 3, 1),
                NewUnit(4, 0, UnitType.Artillery, 3, 0),
                NewUnit(5, 0, UnitType.Recon, 4, 0),
                // Player 1 (top, rows 6-7), mirrored through 180° rotation
                NewUnit(6, 1, UnitType.Infantry, 5, 6),
                NewUnit(7, 1, UnitType.Infantry, 2, 6),
                NewUnit(8, 1, UnitType.MechInfantry, 3, 6),
                NewUnit(9, 1, UnitType.Armor, 4, 6),
                NewUnit(10, 1, UnitType.Artillery, 4, 7),
                NewUnit(11, 1, UnitType.Recon, 3, 7),
            };

            return new GameState
            {
                Terrain = terrain,
                Units = units,
                CurrentPlayer = 0,
                TurnPhase = TurnPhase.Move,
                TurnNumber = 1,
                Winner = null,
            };
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
