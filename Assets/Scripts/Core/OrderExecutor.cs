using System;
using System.Collections.Generic;
using System.Linq;

namespace Tactix.Core
{
    /// <summary>
    /// Turns the head of a unit's order queue into one legal <see cref="GameAction"/>
    /// for the current ply. Returns null when the unit should idle this step
    /// (hold while inside the radius, or nothing useful left to do).
    /// </summary>
    public static class OrderExecutor
    {
        /// <summary>How close counts as "arrived" for a MoveTo order.</summary>
        public const double ArrivalTolerance = 0.35;

        /// <summary>
        /// Propose the next action for <paramref name="unitId"/> given its current
        /// head order. Sets <paramref name="orderComplete"/> when the order should
        /// be dequeued (arrived, target gone, heal delivered, etc.).
        /// </summary>
        public static GameAction TryStep(GameState state, int unitId, UnitOrder order, out bool orderComplete)
        {
            orderComplete = false;
            if (state == null || state.IsOver || order == null) return null;

            var unit = state.GetUnit(unitId);
            if (unit == null || unit.Owner != state.CurrentPlayer) return null;

            switch (order)
            {
                case MoveToOrder move: return StepMoveTo(state, unit, move, out orderComplete);
                case EngageOrder engage: return StepEngage(state, unit, engage, out orderComplete);
                case HoldOrder hold: return StepHold(state, unit, hold, out orderComplete);
                case SupportOrder support: return StepSupport(state, unit, support, out orderComplete);
                default: return null;
            }
        }

        /// <summary>
        /// Rough ETA in turns along the least-slope path at MoveRange per turn.
        /// Presentation only — ignores friction variance.
        /// </summary>
        public static int EstimateTurnsToGoal(GameState state, int unitId, double goalX, double goalY)
        {
            var unit = state?.GetUnit(unitId);
            if (unit == null) return 0;
            double range = unit.Stats.MoveRange;
            if (range <= Geometry.Epsilon) return int.MaxValue;

            double dist = Pathfinder.PathLength(state, unit.X, unit.Y, goalX, goalY);
            if (dist <= ArrivalTolerance) return 0;
            return Math.Max(1, (int)Math.Ceiling(dist / range));
        }

        /// <summary>
        /// World-space waypoints for drawing a path: unit position, then the
        /// slope-aware route through each queued order's goal.
        /// </summary>
        public static List<(double x, double y)> PathWaypoints(GameState state, int unitId, IReadOnlyList<UnitOrder> orders)
        {
            var points = new List<(double x, double y)>();
            var unit = state?.GetUnit(unitId);
            if (unit == null || orders == null || orders.Count == 0) return points;

            double cx = unit.X, cy = unit.Y;
            points.Add((cx, cy));

            foreach (var order in orders)
            {
                if (!TryGoal(state, order, out double gx, out double gy)) continue;
                if (Pathfinder.TryFindPath(state, cx, cy, gx, gy, out var segment) && segment.Count >= 2)
                {
                    for (int i = 1; i < segment.Count; i++)
                        points.Add(segment[i]);
                    var last = segment[segment.Count - 1];
                    cx = last.x;
                    cy = last.y;
                }
                else
                {
                    points.Add((gx, gy));
                    cx = gx;
                    cy = gy;
                }
            }
            return points;
        }

        /// <summary>
        /// Slope-aware route from the unit's current position to an order's goal.
        /// Recomputed each call — no caching.
        /// </summary>
        public static List<(double x, double y)> GetRoute(GameState state, int unitId, UnitOrder order)
        {
            var unit = state?.GetUnit(unitId);
            if (unit == null || order == null) return new List<(double x, double y)>();

            if (!TryGoal(state, order, out double gx, out double gy))
                return new List<(double x, double y)> { (unit.X, unit.Y) };

            if (Pathfinder.TryFindPath(state, unit.X, unit.Y, gx, gy, out var path))
                return path;

            return new List<(double x, double y)> { (unit.X, unit.Y), (gx, gy) };
        }

        public static bool TryGoal(GameState state, UnitOrder order, out double x, out double y)
        {
            x = y = 0;
            switch (order)
            {
                case MoveToOrder move:
                    x = move.X;
                    y = move.Y;
                    return true;
                case HoldOrder hold:
                    x = hold.X;
                    y = hold.Y;
                    return true;
                case EngageOrder engage:
                    var enemy = state?.GetUnit(engage.TargetUnitId);
                    if (enemy == null) return false;
                    x = enemy.X;
                    y = enemy.Y;
                    return true;
                case SupportOrder support:
                    var ally = state?.GetUnit(support.TargetUnitId);
                    if (ally == null) return false;
                    x = ally.X;
                    y = ally.Y;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Next waypoint along the route toward an order's goal.</summary>
        public static bool TryNextWaypoint(GameState state, Unit unit, UnitOrder order, out double wx, out double wy)
        {
            wx = wy = 0;
            if (unit == null || order == null) return false;

            if (!TryGoal(state, order, out double gx, out double gy)) return false;

            if (Rules.Distance(unit.X, unit.Y, gx, gy) <= ArrivalTolerance)
            {
                wx = gx;
                wy = gy;
                return true;
            }

            var route = GetRoute(state, unit.Id, order);
            if (route.Count < 2)
            {
                wx = gx;
                wy = gy;
                return true;
            }

            // Skip waypoints already reached.
            int next = 1;
            while (next < route.Count - 1
                   && Rules.Distance(unit.X, unit.Y, route[next].x, route[next].y) <= ArrivalTolerance)
                next++;

            wx = route[next].x;
            wy = route[next].y;
            return true;
        }

        private static GameAction StepMoveTo(GameState state, Unit unit, MoveToOrder order, out bool orderComplete)
        {
            double dist = Rules.Distance(unit.X, unit.Y, order.X, order.Y);
            if (dist <= ArrivalTolerance)
            {
                orderComplete = true;
                return null;
            }

            if (unit.HasMoved || state.TurnPhase != TurnPhase.Move)
            {
                orderComplete = false;
                return null;
            }

            if (!TryNextWaypoint(state, unit, order, out double wx, out double wy)
                || !Rules.ProjectMove(state, unit.Id, wx, wy, out double tx, out double ty))
            {
                orderComplete = true;
                return null;
            }

            orderComplete = Rules.Distance(tx, ty, order.X, order.Y) <= ArrivalTolerance;
            return new MoveAction { UnitId = unit.Id, TargetX = tx, TargetY = ty };
        }

        private static GameAction StepEngage(GameState state, Unit unit, EngageOrder order, out bool orderComplete)
        {
            var target = state.GetUnit(order.TargetUnitId);
            if (target == null || target.Owner == unit.Owner)
            {
                orderComplete = true;
                return null;
            }

            var attack = Rules.GetLegalAttacks(state, unit.Id)
                .FirstOrDefault(a => a.TargetUnitId == order.TargetUnitId);
            if (attack != null)
            {
                orderComplete = false;
                return attack;
            }

            if (unit.HasMoved || state.TurnPhase != TurnPhase.Move)
            {
                orderComplete = false;
                return null;
            }

            if (!TryNextWaypoint(state, unit, order, out double wx, out double wy)
                || !Rules.ProjectMove(state, unit.Id, wx, wy, out double tx, out double ty))
            {
                orderComplete = true;
                return null;
            }

            orderComplete = false;
            return new MoveAction { UnitId = unit.Id, TargetX = tx, TargetY = ty };
        }

        private static GameAction StepHold(GameState state, Unit unit, HoldOrder order, out bool orderComplete)
        {
            orderComplete = false;
            double dist = Rules.Distance(unit.X, unit.Y, order.X, order.Y);
            if (dist <= order.Radius) return null;

            if (unit.HasMoved || state.TurnPhase != TurnPhase.Move) return null;

            if (!TryNextWaypoint(state, unit, order, out double wx, out double wy)
                || !Rules.ProjectMove(state, unit.Id, wx, wy, out double tx, out double ty))
                return null;

            return new MoveAction { UnitId = unit.Id, TargetX = tx, TargetY = ty };
        }

        private static GameAction StepSupport(GameState state, Unit unit, SupportOrder order, out bool orderComplete)
        {
            var target = state.GetUnit(order.TargetUnitId);
            if (target == null || target.Owner != unit.Owner)
            {
                orderComplete = true;
                return null;
            }

            var heal = Rules.GetLegalHeals(state, unit.Id)
                .FirstOrDefault(h => h.TargetUnitId == order.TargetUnitId);
            if (heal != null)
            {
                orderComplete = true;
                return heal;
            }

            if (target.Hp >= target.Stats.MaxHp)
            {
                orderComplete = true;
                return null;
            }

            if (unit.HasMoved || state.TurnPhase != TurnPhase.Move)
            {
                orderComplete = false;
                return null;
            }

            if (!TryNextWaypoint(state, unit, order, out double wx, out double wy)
                || !Rules.ProjectMove(state, unit.Id, wx, wy, out double tx, out double ty))
            {
                orderComplete = true;
                return null;
            }

            orderComplete = false;
            return new MoveAction { UnitId = unit.Id, TargetX = tx, TargetY = ty };
        }
    }
}
