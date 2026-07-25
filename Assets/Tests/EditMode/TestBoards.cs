using System.Collections.Generic;
using System.Linq;

namespace Tactix.Core.Tests
{
    /// <summary>Helpers for constructing small custom boards in tests.</summary>
    public static class TestBoards
    {
        public static GameState OpenBoard(int width, int height)
        {
            var terrain = new TerrainType[height][];
            for (int y = 0; y < height; y++)
                terrain[y] = new TerrainType[width];
            return new GameState
            {
                Terrain = terrain,
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

        public static GameState WithUnit(this GameState state, int id, int owner, UnitType type, int x, int y, int? hp = null)
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

        public static HashSet<(int x, int y)> MoveTargets(GameState state, int unitId)
        {
            return Rules.GetLegalMoves(state, unitId).Select(m => (m.TargetX, m.TargetY)).ToHashSet();
        }

        public static HashSet<int> AttackTargets(GameState state, int unitId)
        {
            return Rules.GetLegalAttacks(state, unitId).Select(a => a.TargetUnitId).ToHashSet();
        }
    }
}
