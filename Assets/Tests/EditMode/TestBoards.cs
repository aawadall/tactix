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
                // Rules tests assert exact outcomes, so boards default to the
                // deterministic ruleset; variance is exercised by its own tests.
                Ruleset = Ruleset.Deterministic,
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

        public static GameState WithUnit(this GameState state, int id, int owner, UnitType type, double x, double y,
            int? hp = null, Echelon echelon = Echelon.Company)
        {
            state.Units.Add(new Unit
            {
                Id = id,
                Owner = owner,
                Type = type,
                Echelon = echelon,
                X = x,
                Y = y,
                Hp = hp ?? UnitStats.For(type, echelon).MaxHp,
            });
            return state;
        }

        public static GameState WithRuleset(this GameState state, Ruleset ruleset)
        {
            state.Ruleset = ruleset;
            return state;
        }

        public static GameState WithObjective(this GameState state, int id, double x, double y, double radius, int value)
        {
            state.Objectives.Add(new Objective { Id = id, X = x, Y = y, Radius = radius, Value = value });
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
