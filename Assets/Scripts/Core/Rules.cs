using System;
using System.Collections.Generic;
using System.Linq;

namespace Tactix.Core
{
    /// <summary>
    /// The authoritative rules engine for continuous space.
    ///
    /// Movement is a straight-line dash: a unit may move to any point within its
    /// move range whose connecting segment stays in bounds, avoids impassable
    /// terrain, never crosses a cliff, and leaves the unit clear of others. The
    /// legal region is therefore star-shaped around the unit, which makes both
    /// exact testing (<see cref="IsLegalMoveTarget"/>) and constraint projection
    /// (<see cref="ProjectMove"/>) cheap and deterministic.
    ///
    /// Because move targets are continuous there is no finite list of legal
    /// moves. The authority is the predicate; <see cref="GetMoveRegion"/> and
    /// <see cref="SampleLegalMoves"/> describe or sample that region for UI,
    /// bots, and future policies, and <see cref="Apply"/> re-validates every
    /// action against the predicate. Attacks stay pointer-based and fully
    /// enumerable.
    /// </summary>
    public static class Rules
    {
        /// <summary>Rays used to describe the move region (UI outline and sampling).</summary>
        public const int MoveRegionRays = 72;

        public static double Distance(double x0, double y0, double x1, double y1) =>
            Geometry.Distance(x0, y0, x1, y1);

        // ---------- movement ----------

        /// <summary>
        /// The authoritative movement predicate: true iff this unit may legally
        /// dash to (targetX, targetY) in the given state.
        /// </summary>
        public static bool IsLegalMoveTarget(GameState state, int unitId, double targetX, double targetY)
        {
            var unit = state.GetUnit(unitId);
            if (unit == null || state.Winner != null) return false;
            if (unit.Owner != state.CurrentPlayer || unit.HasMoved) return false;
            if (state.TurnPhase != TurnPhase.Move) return false;

            if (double.IsNaN(targetX) || double.IsNaN(targetY)) return false;
            if (!Geometry.IsInsideBoard(state, targetX, targetY)) return false;

            double distance = Distance(unit.X, unit.Y, targetX, targetY);
            if (distance > unit.Stats.MoveRange + Geometry.Epsilon) return false;
            if (distance <= Geometry.Epsilon) return false; // a no-op is not a move

            if (state.TerrainAtPoint(targetX, targetY) == TerrainType.Impassable) return false;
            if (!Geometry.IsPathWalkable(state, unit.X, unit.Y, targetX, targetY)) return false;

            return IsClearOfOtherUnits(state, unit, targetX, targetY);
        }

        /// <summary>Destination must not overlap another unit's body.</summary>
        private static bool IsClearOfOtherUnits(GameState state, Unit unit, double x, double y)
        {
            foreach (var other in state.Units)
            {
                if (other.Id == unit.Id) continue;
                double minSeparation = unit.Stats.Radius + other.Stats.Radius;
                if (Distance(other.X, other.Y, x, y) < minSeparation - Geometry.Epsilon) return false;
            }
            return true;
        }

        /// <summary>
        /// Star-shaped description of everywhere the unit may move: the maximum
        /// travelable distance along <see cref="MoveRegionRays"/> evenly spaced
        /// directions (terrain only — unit bodies are checked per destination).
        /// Empty when the unit cannot move at all.
        /// </summary>
        public static double[] GetMoveRegion(GameState state, int unitId, int rayCount = MoveRegionRays)
        {
            var unit = state.GetUnit(unitId);
            if (unit == null || state.Winner != null) return Array.Empty<double>();
            if (unit.Owner != state.CurrentPlayer || unit.HasMoved) return Array.Empty<double>();
            if (state.TurnPhase != TurnPhase.Move) return Array.Empty<double>();

            var reach = new double[rayCount];
            for (int i = 0; i < rayCount; i++)
            {
                double angle = 2.0 * Math.PI * i / rayCount;
                Geometry.RayDistance(state, unit.X, unit.Y, Math.Cos(angle), Math.Sin(angle),
                    unit.Stats.MoveRange, out double reachable);
                reach[i] = reachable;
            }
            return reach;
        }

        /// <summary>
        /// Constraint projection: the closest legal destination to the requested
        /// point along the same heading (what a continuous policy's raw output
        /// gets clamped to). Returns false when the unit cannot move at all.
        /// </summary>
        public static bool ProjectMove(GameState state, int unitId, double requestedX, double requestedY, out double x, out double y)
        {
            x = requestedX;
            y = requestedY;
            var unit = state.GetUnit(unitId);
            if (unit == null) return false;
            if (IsLegalMoveTarget(state, unitId, requestedX, requestedY)) return true;

            double dx = requestedX - unit.X, dy = requestedY - unit.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < Geometry.Epsilon) return false;
            double ux = dx / len, uy = dy / len;

            Geometry.RayDistance(state, unit.X, unit.Y, ux, uy, unit.Stats.MoveRange, out double reach);
            double candidate = Math.Min(len, reach);

            // Back off along the ray until the destination is also clear of bodies.
            const double stepBack = 0.05;
            for (double d = candidate; d > Geometry.Epsilon; d -= stepBack)
            {
                double px = unit.X + ux * d, py = unit.Y + uy * d;
                if (IsLegalMoveTarget(state, unitId, px, py))
                {
                    x = px;
                    y = py;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Samples legal destinations for the unit (area-uniform within the
        /// star-shaped region). Sampling is for bots, UI, and data generation —
        /// legality is always decided by <see cref="IsLegalMoveTarget"/>, which
        /// every sample is validated against before being returned.
        /// </summary>
        public static List<MoveAction> SampleLegalMoves(GameState state, int unitId, int count, Random rng)
        {
            var moves = new List<MoveAction>();
            var reach = GetMoveRegion(state, unitId);
            if (reach.Length == 0) return moves;

            var unit = state.GetUnit(unitId);
            for (int attempt = 0; attempt < count * 6 && moves.Count < count; attempt++)
            {
                int ray = rng.Next(reach.Length);
                double angle = 2.0 * Math.PI * ray / reach.Length;
                double maxDistance = reach[ray];
                if (maxDistance <= Geometry.PathBackoff) continue;

                // sqrt keeps samples area-uniform rather than clustered at the centre
                double distance = maxDistance * Math.Sqrt(rng.NextDouble());
                double x = unit.X + Math.Cos(angle) * distance;
                double y = unit.Y + Math.Sin(angle) * distance;
                if (!IsLegalMoveTarget(state, unitId, x, y)) continue;
                moves.Add(new MoveAction { UnitId = unitId, TargetX = x, TargetY = y });
            }
            return moves;
        }

        /// <summary>True when the unit has anywhere at all to move.</summary>
        public static bool CanMove(GameState state, int unitId)
        {
            var reach = GetMoveRegion(state, unitId);
            return reach.Any(r => r > Geometry.PathBackoff);
        }

        // ---------- attacks ----------

        /// <summary>
        /// Legal attacks for the given unit: enemy units within Euclidean attack
        /// range, with line of sight required for units whose stats demand it.
        /// Fully enumerable — the target space stays discrete.
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
                if (Distance(unit.X, unit.Y, target.X, target.Y) > stats.AttackRange + Geometry.Epsilon) continue;
                if (stats.RequiresLineOfSight &&
                    !LineOfSight.HasLineOfSight(state, unit.X, unit.Y, target.X, target.Y)) continue;
                attacks.Add(new AttackAction { UnitId = unitId, TargetUnitId = target.Id });
            }
            return attacks;
        }

        // ---------- action enumeration ----------

        /// <summary>
        /// Every legal attack plus EndTurn, together with a sample of legal moves
        /// per movable unit. NOTE: unlike the discrete engine this is not an
        /// exhaustive enumeration — move targets are continuous. It is a valid
        /// action set (everything returned is legal), suitable for baseline bots
        /// and data generation.
        /// </summary>
        public static List<GameAction> GetAllLegalActions(GameState state, Random rng = null, int movesPerUnit = 8)
        {
            var actions = new List<GameAction>();
            if (state.Winner != null) return actions;
            rng = rng ?? new Random(0);

            foreach (var unit in state.Units)
            {
                if (unit.Owner != state.CurrentPlayer) continue;
                actions.AddRange(SampleLegalMoves(state, unit.Id, movesPerUnit, rng));
                actions.AddRange(GetLegalAttacks(state, unit.Id));
            }
            actions.Add(new EndTurnAction());
            return actions;
        }

        // ---------- application ----------

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
            if (!IsLegalMoveTarget(state, move.UnitId, move.TargetX, move.TargetY))
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

            int defense = next.TerrainAtPoint(target.X, target.Y) == TerrainType.Forest ? 1 : 0;
            int highGround = next.ElevationAtPoint(attacker.X, attacker.Y) > next.ElevationAtPoint(target.X, target.Y) ? 1 : 0;
            int damage = Math.Max(0, attacker.Stats.AttackPower + highGround - defense);
            target.Hp -= damage;
            attacker.Xp += 1; // display-only

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
    }

    public sealed class IllegalActionException : Exception
    {
        public IllegalActionException(string message) : base(message) { }
    }
}
