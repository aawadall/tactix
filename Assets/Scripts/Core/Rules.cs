using System;
using System.Collections.Generic;
using System.Linq;

namespace Tactix.Core
{
    /// <summary>
    /// The authoritative rules engine. All legality questions are answered by the
    /// pure functions here, and <see cref="Apply"/> is the only way to advance a
    /// state. No illegal action is applicable: Apply validates against the same
    /// legal-action computation used for UI highlighting, bots, and (later) ML
    /// action masking.
    /// </summary>
    public static class Rules
    {
        /// <summary>All 8 neighbor offsets (diagonals included).</summary>
        private static readonly (int dx, int dy)[] Directions =
        {
            (-1, -1), (0, -1), (1, -1),
            (-1, 0), (1, 0),
            (-1, 1), (0, 1), (1, 1),
        };

        public static int ChebyshevDistance(int x0, int y0, int x1, int y1)
        {
            return Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
        }

        /// <summary>
        /// Legal moves for the given unit in the given state. Empty unless the unit
        /// belongs to the current player, has not moved, the turn is still in the
        /// Move phase, and the game is not over.
        /// Movement: BFS over the 8-connected grid, each step cost 1, up to the
        /// unit's move range. Impassable tiles and enemy units block; friendly
        /// units can be passed through but not ended on.
        /// </summary>
        public static List<MoveAction> GetLegalMoves(GameState state, int unitId)
        {
            var moves = new List<MoveAction>();
            var unit = state.GetUnit(unitId);
            if (unit == null || state.Winner != null) return moves;
            if (unit.Owner != state.CurrentPlayer || unit.HasMoved) return moves;
            if (state.TurnPhase != TurnPhase.Move) return moves;

            foreach (var (x, y) in ReachableTiles(state, unit))
            {
                moves.Add(new MoveAction { UnitId = unitId, TargetX = x, TargetY = y });
            }
            return moves;
        }

        /// <summary>
        /// Legal attacks for the given unit: enemy units within Chebyshev attack
        /// range, with line-of-sight required for units whose stats demand it.
        /// Attacks are legal in both phases (the first attack of a turn switches
        /// the phase to Attack).
        /// </summary>
        public static List<AttackAction> GetLegalAttacks(GameState state, int unitId)
        {
            var attacks = new List<AttackAction>();
            var unit = state.GetUnit(unitId);
            if (unit == null || state.Winner != null) return attacks;
            if (unit.Owner != state.CurrentPlayer || unit.HasAttacked) return attacks;

            var stats = unit.Stats;
            foreach (var target in state.Units)
            {
                if (target.Owner == unit.Owner) continue;
                if (ChebyshevDistance(unit.X, unit.Y, target.X, target.Y) > stats.AttackRange) continue;
                if (stats.RequiresLineOfSight &&
                    !LineOfSight.HasLineOfSight(state, unit.X, unit.Y, target.X, target.Y)) continue;
                attacks.Add(new AttackAction { UnitId = unitId, TargetUnitId = target.Id });
            }
            return attacks;
        }

        /// <summary>
        /// Every legal action for the current player, including EndTurn (always
        /// legal while the game is running). This is the action-mask source for
        /// bots and future ML models.
        /// </summary>
        public static List<GameAction> GetAllLegalActions(GameState state)
        {
            var actions = new List<GameAction>();
            if (state.Winner != null) return actions;

            foreach (var unit in state.Units)
            {
                if (unit.Owner != state.CurrentPlayer) continue;
                actions.AddRange(GetLegalMoves(state, unit.Id));
                actions.AddRange(GetLegalAttacks(state, unit.Id));
            }
            actions.Add(new EndTurnAction());
            return actions;
        }

        /// <summary>
        /// Applies an action, returning the resulting state. The input state is not
        /// modified. Throws <see cref="IllegalActionException"/> if the action is not
        /// legal in the given state.
        /// </summary>
        public static GameState Apply(GameState state, GameAction action)
        {
            if (state.Winner != null)
                throw new IllegalActionException("Game is already over");

            switch (action)
            {
                case MoveAction move: return ApplyMove(state, move);
                case AttackAction attack: return ApplyAttack(state, attack);
                case EndTurnAction _: return ApplyEndTurn(state);
                default:
                    throw new IllegalActionException($"Unknown action type {action?.GetType().Name ?? "null"}");
            }
        }

        private static GameState ApplyMove(GameState state, MoveAction move)
        {
            bool legal = GetLegalMoves(state, move.UnitId)
                .Any(m => m.TargetX == move.TargetX && m.TargetY == move.TargetY);
            if (!legal)
                throw new IllegalActionException($"Illegal move: {move}");

            var next = state.Clone();
            var unit = next.GetUnit(move.UnitId);
            unit.X = move.TargetX;
            unit.Y = move.TargetY;
            unit.HasMoved = true;
            return next;
        }

        private static GameState ApplyAttack(GameState state, AttackAction attack)
        {
            bool legal = GetLegalAttacks(state, attack.UnitId)
                .Any(a => a.TargetUnitId == attack.TargetUnitId);
            if (!legal)
                throw new IllegalActionException($"Illegal attack: {attack}");

            var next = state.Clone();
            next.TurnPhase = TurnPhase.Attack; // first attack ends movement for the whole turn

            var attacker = next.GetUnit(attack.UnitId);
            var target = next.GetUnit(attack.TargetUnitId);
            attacker.HasAttacked = true;

            int defense = next.TerrainAt(target.X, target.Y) == TerrainType.Forest ? 1 : 0;
            int damage = Math.Max(0, attacker.Stats.AttackPower - defense);
            target.Hp -= damage;
            attacker.Xp += 1; // display-only in schema v2

            if (target.Hp <= 0)
            {
                attacker.Xp += 2;
                next.Units.Remove(target);
                if (next.Units.All(u => u.Owner == attacker.Owner))
                    next.Winner = attacker.Owner;
            }
            return next;
        }

        private static GameState ApplyEndTurn(GameState state)
        {
            var next = state.Clone();
            next.CurrentPlayer = 1 - next.CurrentPlayer;
            next.TurnPhase = TurnPhase.Move;
            next.TurnNumber++;
            foreach (var unit in next.Units)
            {
                unit.HasMoved = false;
                unit.HasAttacked = false;
            }
            return next;
        }

        /// <summary>
        /// Tiles the unit can end a move on: BFS up to move range, 8-connected.
        /// The unit's own start tile is not a move target.
        /// </summary>
        private static IEnumerable<(int x, int y)> ReachableTiles(GameState state, Unit unit)
        {
            int range = unit.Stats.MoveRange;
            var visited = new HashSet<(int, int)> { (unit.X, unit.Y) };
            var frontier = new Queue<(int x, int y, int dist)>();
            frontier.Enqueue((unit.X, unit.Y, 0));

            while (frontier.Count > 0)
            {
                var (cx, cy, dist) = frontier.Dequeue();
                if (dist >= range) continue;

                foreach (var (dx, dy) in Directions)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (!state.IsInBounds(nx, ny) || visited.Contains((nx, ny))) continue;
                    if (state.TerrainAt(nx, ny) == TerrainType.Impassable) continue;

                    var occupant = state.GetUnitAt(nx, ny);
                    if (occupant != null && occupant.Owner != unit.Owner) continue; // enemies block pathing

                    visited.Add((nx, ny));
                    frontier.Enqueue((nx, ny, dist + 1));

                    if (occupant == null) // may pass through friendlies but not stop on them
                        yield return (nx, ny);
                }
            }
        }
    }

    public sealed class IllegalActionException : Exception
    {
        public IllegalActionException(string message) : base(message) { }
    }
}
