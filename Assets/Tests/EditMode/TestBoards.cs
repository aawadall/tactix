using System.Collections.Generic;

namespace Tactix.Core.Tests
{
    /// <summary>Helpers for constructing small custom boards in tests.</summary>
    public static class TestBoards
    {
        public static GameState OpenBoard(int width, int height)
        {
            var terrain = new TerrainType[height][];
            var elevation = new int[height][];
            for (int y = 0; y < height; y++)
            {
                terrain[y] = new TerrainType[width];
                elevation[y] = new int[width];
            }
            return new GameState
            {
                Terrain = terrain,
                Elevation = elevation,
                Units = new List<Unit>(),
                CurrentPlayer = 0,
                TurnPhase = TurnPhase.Move,
                TurnNumber = 1,
            };
        }

        public static GameState WithTerrain(this GameState state, TerrainType type, params (int x, int y)[] tiles)
        {
            foreach (var (x, y) in tiles) state.Terrain[y][x] = type;
            return state;
        }

        public static GameState WithElevation(this GameState state, int elevation, params (int x, int y)[] tiles)
        {
            foreach (var (x, y) in tiles) state.Elevation[y][x] = elevation;
            return state;
        }

        public static GameState WithUnit(this GameState state, int id, int owner, UnitType type, double x, double y, int? hp = null)
        {
            state.Units.Add(new Unit
            {
                Id = id,
                Owner = owner,
                Type = type,
                X = x,
                Y = y,
                Hp = hp ?? UnitStats.For(type).MaxHp,
            });
            return state;
        }

        public static HashSet<int> AttackTargets(GameState state, int unitId)
        {
            var targets = new HashSet<int>();
            foreach (var attack in Rules.GetLegalAttacks(state, unitId)) targets.Add(attack.TargetUnitId);
            return targets;
        }

        public static MoveAction Move(int unitId, double x, double y) =>
            new MoveAction { UnitId = unitId, TargetX = x, TargetY = y };
    }
}
