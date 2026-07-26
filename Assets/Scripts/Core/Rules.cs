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

        // ---------- support (heal / repair) ----------

        /// <summary>
        /// Legal support actions for the given unit: wounded friendly units of a
        /// type this supporter can work on, within support range. Fully
        /// enumerable. Support has its own per-turn slot — it does not consume
        /// the attack and never ends the movement phase.
        /// </summary>
        public static List<HealAction> GetLegalHeals(GameState state, int unitId)
        {
            var heals = new List<HealAction>();
            var unit = state.GetUnit(unitId);
            if (unit == null || state.Winner != null) return heals;
            if (unit.Owner != state.CurrentPlayer || unit.HasSupported) return heals;

            var stats = unit.Stats;
            if (!stats.CanSupport) return heals;

            foreach (var target in state.Units)
            {
                if (target.Id == unit.Id) continue;          // no self-treatment
                if (target.Owner != unit.Owner) continue;    // friendlies only
                if (target.Hp >= target.Stats.MaxHp) continue; // already at full strength
                if (!stats.CanSupportType(target.Type)) continue;
                if (Distance(unit.X, unit.Y, target.X, target.Y) > stats.SupportRange + Geometry.Epsilon) continue;
                heals.Add(new HealAction { UnitId = unitId, TargetUnitId = target.Id });
            }
            return heals;
        }

        // ---------- amalgamation and detachment ----------

        /// <summary>How close two formations must be before they can combine.</summary>
        public const double MergeContactBuffer = 0.5;

        /// <summary>How far beyond touching a detachment may form up.</summary>
        public const double SplitFormUpBuffer = 1.0;

        /// <summary>
        /// Legal amalgamations for this unit: friendly formations of the same
        /// size, standing within contact, where the combined formation fits.
        /// Both units spend their move, so neither may have moved already.
        /// Enumerable — merge targets are discrete units.
        /// </summary>
        public static List<MergeAction> GetLegalMerges(GameState state, int unitId)
        {
            var merges = new List<MergeAction>();
            var unit = state.GetUnit(unitId);
            if (unit == null || state.Winner != null) return merges;
            if (unit.Owner != state.CurrentPlayer || unit.HasMoved) return merges;
            if (state.TurnPhase != TurnPhase.Move) return merges;
            if (EchelonScale.Larger(unit.Echelon) == null) return merges; // already at the top

            foreach (var other in state.Units)
            {
                if (other.Id == unit.Id || other.Owner != unit.Owner) continue;
                if (other.Echelon != unit.Echelon || other.HasMoved) continue;

                double contact = unit.Stats.Radius + other.Stats.Radius + MergeContactBuffer;
                if (Distance(unit.X, unit.Y, other.X, other.Y) > contact) continue;
                if (!TryPlanMerge(state, unit, other, out _, out _, out _, out _)) continue;

                merges.Add(new MergeAction { UnitId = unit.Id, AbsorbedUnitId = other.Id });
            }
            return merges;
        }

        /// <summary>
        /// Works out what a merge would produce and whether the result fits on the
        /// board. Same branch keeps that branch; unlike branches produce a
        /// combined-arms formation.
        /// </summary>
        private static bool TryPlanMerge(GameState state, Unit unit, Unit other,
            out UnitType type, out Echelon echelon, out double x, out double y)
        {
            type = unit.Type == other.Type ? unit.Type : UnitType.CombinedArms;
            echelon = EchelonScale.Larger(unit.Echelon) ?? unit.Echelon;
            x = 0;
            y = 0;

            double radius = UnitStats.For(type, echelon).Radius;
            // Prefer forming up between the two; fall back to either's ground.
            var candidates = new[]
            {
                ((unit.X + other.X) / 2, (unit.Y + other.Y) / 2),
                (unit.X, unit.Y),
                (other.X, other.Y),
            };

            foreach (var (cx, cy) in candidates)
            {
                if (!Geometry.IsInsideBoard(state, cx, cy)) continue;
                if (state.TerrainAtPoint(cx, cy) == TerrainType.Impassable) continue;
                if (!IsClearOfUnitsExcept(state, cx, cy, radius, unit.Id, other.Id)) continue;
                x = cx;
                y = cy;
                return true;
            }
            return false;
        }

        /// <summary>
        /// The region a detachment may form up in: the same ray-cast description
        /// used for movement, bounded by how far a detaching formation can go.
        /// Empty when the unit cannot split.
        /// </summary>
        public static double[] GetSplitRegion(GameState state, int unitId, int rayCount = MoveRegionRays)
        {
            var unit = state.GetUnit(unitId);
            if (!CanSplit(state, unit)) return Array.Empty<double>();

            var child = UnitStats.For(unit.Type, EchelonScale.Smaller(unit.Echelon).Value);
            double range = unit.Stats.Radius + child.Radius + SplitFormUpBuffer;

            var reach = new double[rayCount];
            for (int i = 0; i < rayCount; i++)
            {
                double angle = 2.0 * Math.PI * i / rayCount;
                Geometry.RayDistance(state, unit.X, unit.Y, Math.Cos(angle), Math.Sin(angle),
                    range, out double reachable);
                reach[i] = reachable;
            }
            return reach;
        }

        /// <summary>Authoritative predicate for where a detachment may form up.</summary>
        public static bool IsLegalSplitTarget(GameState state, int unitId, double targetX, double targetY)
        {
            var unit = state.GetUnit(unitId);
            if (!CanSplit(state, unit)) return false;
            if (double.IsNaN(targetX) || double.IsNaN(targetY)) return false;
            if (!Geometry.IsInsideBoard(state, targetX, targetY)) return false;
            if (state.TerrainAtPoint(targetX, targetY) == TerrainType.Impassable) return false;

            var childEchelon = EchelonScale.Smaller(unit.Echelon).Value;
            var child = UnitStats.For(unit.Type, childEchelon);

            double distance = Distance(unit.X, unit.Y, targetX, targetY);
            if (distance > unit.Stats.Radius + child.Radius + SplitFormUpBuffer + Geometry.Epsilon) return false;
            if (distance <= Geometry.Epsilon) return false;
            if (!Geometry.IsPathWalkable(state, unit.X, unit.Y, targetX, targetY)) return false;

            // The parent shrinks too, so it only has to clear everyone else.
            if (!IsClearOfUnitsExcept(state, targetX, targetY, child.Radius, unit.Id, -1)) return false;
            return Distance(unit.X, unit.Y, targetX, targetY) >= 2 * child.Radius - Geometry.Epsilon;
        }

        /// <summary>
        /// Samples form-up points for a detachment, validated against
        /// <see cref="IsLegalSplitTarget"/>. Split placement is continuous, so as
        /// with movement this samples rather than enumerates.
        /// </summary>
        public static List<SplitAction> SampleLegalSplits(GameState state, int unitId, int count, Random rng)
        {
            var splits = new List<SplitAction>();
            var reach = GetSplitRegion(state, unitId);
            if (reach.Length == 0) return splits;

            var unit = state.GetUnit(unitId);
            for (int attempt = 0; attempt < count * 8 && splits.Count < count; attempt++)
            {
                int ray = rng.Next(reach.Length);
                double angle = 2.0 * Math.PI * ray / reach.Length;
                double maxDistance = reach[ray];
                if (maxDistance <= Geometry.PathBackoff) continue;

                double distance = maxDistance * (0.5 + 0.5 * rng.NextDouble());
                double x = unit.X + Math.Cos(angle) * distance;
                double y = unit.Y + Math.Sin(angle) * distance;
                if (!IsLegalSplitTarget(state, unitId, x, y)) continue;
                splits.Add(new SplitAction { UnitId = unitId, TargetX = x, TargetY = y });
            }
            return splits;
        }

        private static bool CanSplit(GameState state, Unit unit)
        {
            if (unit == null || state.Winner != null) return false;
            if (unit.Owner != state.CurrentPlayer || unit.HasMoved) return false;
            if (state.TurnPhase != TurnPhase.Move) return false;
            return EchelonScale.Smaller(unit.Echelon) != null;
        }

        private static bool IsClearOfUnitsExcept(GameState state, double x, double y, double radius, int ignoreA, int ignoreB)
        {
            foreach (var other in state.Units)
            {
                if (other.Id == ignoreA || other.Id == ignoreB) continue;
                if (Distance(other.X, other.Y, x, y) < radius + other.Stats.Radius - Geometry.Epsilon) return false;
            }
            return true;
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
                actions.AddRange(GetLegalHeals(state, unit.Id));
                actions.AddRange(GetLegalMerges(state, unit.Id));
                actions.AddRange(SampleLegalSplits(state, unit.Id, 2, rng));
            }
            actions.Add(new EndTurnAction());
            return actions;
        }

        // ---------- application ----------

        /// <summary>
        /// Applies an action, returning the resulting state. The input state is not
        /// modified. Throws <see cref="IllegalActionException"/> if the action is not
        /// legal in the given state.
        ///
        /// Under a stochastic ruleset a random source is required: damage rolls and
        /// movement shortfalls are drawn from it, in a fixed order, so that
        /// recording the draws makes a game exactly replayable. Passing an
        /// unnecessary source is harmless — it simply goes unused.
        /// </summary>
        public static GameState Apply(GameState state, GameAction action, IRandomSource rng = null)
        {
            if (state.Winner != null)
                throw new IllegalActionException("Game is already over");
            if (state.Ruleset != null && state.Ruleset.IsStochastic && rng == null)
                throw new InvalidOperationException(
                    "This ruleset resolves outcomes randomly; Apply needs an IRandomSource so the draws can be logged.");

            switch (action)
            {
                case MoveAction move: return ApplyMove(state, move, rng);
                case AttackAction attack: return ApplyAttack(state, attack, rng);
                case HealAction heal: return ApplyHeal(state, heal);
                case MergeAction merge: return ApplyMerge(state, merge);
                case SplitAction split: return ApplySplit(state, split);
                case EndTurnAction _: return ApplyEndTurn(state);
                default:
                    throw new IllegalActionException($"Unknown action type {action?.GetType().Name ?? "null"}");
            }
        }

        private static GameState ApplyMove(GameState state, MoveAction move, IRandomSource rng)
        {
            if (!IsLegalMoveTarget(state, move.UnitId, move.TargetX, move.TargetY))
                throw new IllegalActionException($"Illegal move: {move}");

            var next = state.Clone();
            var unit = next.GetUnit(move.UnitId);
            var (x, y) = ResolveMoveDestination(next, unit, move.TargetX, move.TargetY, rng);
            unit.X = x;
            unit.Y = y;
            unit.HasMoved = true;
            return next;
        }

        /// <summary>
        /// Where a formation actually ends up. Small units arrive exactly where
        /// ordered; larger ones may fall short by up to their friction fraction,
        /// stopping early along the same heading. The shortfall is only applied
        /// where it leaves a legal position — a formation never grinds to a halt
        /// inside another unit.
        /// </summary>
        private static (double x, double y) ResolveMoveDestination(
            GameState state, Unit unit, double targetX, double targetY, IRandomSource rng)
        {
            double friction = unit.Stats.MovementFriction;
            if (rng == null || state.Ruleset == null || !state.Ruleset.MovementFriction || friction <= 0)
                return (targetX, targetY);

            double dx = targetX - unit.X, dy = targetY - unit.Y;
            double ordered = Math.Sqrt(dx * dx + dy * dy);
            if (ordered <= Geometry.Epsilon) return (targetX, targetY);

            double shortfall = friction * rng.NextDouble();
            double ux = dx / ordered, uy = dy / ordered;

            // Walk back from the frictioned distance to the first legal stopping
            // point; if none exists, the formation covers the full distance.
            const double stepBack = 0.05;
            for (double distance = ordered * (1.0 - shortfall); distance > Geometry.Epsilon; distance -= stepBack)
            {
                double px = unit.X + ux * distance, py = unit.Y + uy * distance;
                if (IsLegalMoveTarget(state, unit.Id, px, py)) return (px, py);
            }
            return (targetX, targetY);
        }

        private static GameState ApplyAttack(GameState state, AttackAction attack, IRandomSource rng)
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
            int power = RollDamage(next, attacker, rng);
            int damage = Math.Max(0, power + highGround - defense);
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

        /// <summary>
        /// The damage a formation actually inflicts. Small units deal their exact
        /// attack power; larger ones are abstractions over many separate
        /// engagements, so their output is drawn uniformly from a spread that
        /// widens with size.
        /// </summary>
        private static int RollDamage(GameState state, Unit attacker, IRandomSource rng)
        {
            int power = attacker.Stats.AttackPower;
            int spread = attacker.Stats.DamageSpread;
            if (rng == null || state.Ruleset == null || !state.Ruleset.DamageVariance || spread <= 0)
                return power;

            int outcomes = 2 * spread + 1;
            int roll = (int)(rng.NextDouble() * outcomes);
            if (roll >= outcomes) roll = outcomes - 1; // guard against a draw of exactly 1.0
            return Math.Max(0, power - spread + roll);
        }

        private static GameState ApplyHeal(GameState state, HealAction heal)
        {
            bool legal = GetLegalHeals(state, heal.UnitId)
                .Any(h => h.TargetUnitId == heal.TargetUnitId);
            if (!legal)
                throw new IllegalActionException($"Illegal heal: {heal}");

            var next = state.Clone();
            var supporter = next.GetUnit(heal.UnitId);
            var target = next.GetUnit(heal.TargetUnitId);

            supporter.HasSupported = true; // own slot: phase and attack are untouched
            target.Hp = Math.Min(target.Stats.MaxHp, target.Hp + supporter.Stats.SupportPower);
            supporter.Xp += 1;
            return next;
        }

        private static GameState ApplyMerge(GameState state, MergeAction merge)
        {
            bool legal = GetLegalMerges(state, merge.UnitId)
                .Any(m => m.AbsorbedUnitId == merge.AbsorbedUnitId);
            if (!legal)
                throw new IllegalActionException($"Illegal merge: {merge}");

            var next = state.Clone();
            var unit = next.GetUnit(merge.UnitId);
            var absorbed = next.GetUnit(merge.AbsorbedUnitId);

            TryPlanMerge(next, unit, absorbed, out var type, out var echelon, out double x, out double y);

            int pooledHp = unit.Hp + absorbed.Hp;
            int pooledXp = unit.Xp + absorbed.Xp;
            bool attacked = unit.HasAttacked || absorbed.HasAttacked;
            bool supported = unit.HasSupported || absorbed.HasSupported;

            unit.Type = type;
            unit.Echelon = echelon;
            unit.X = x;
            unit.Y = y;
            // Powers of two make this exact from company scale up; at the smallest
            // sizes integer rounding caps the pooled strength.
            unit.Hp = Math.Min(unit.Stats.MaxHp, pooledHp);
            unit.Xp = pooledXp;
            unit.HasMoved = true;      // combining is the formation's move
            unit.HasAttacked = attacked;
            unit.HasSupported = supported;

            next.Units.Remove(absorbed);
            return next;
        }

        private static GameState ApplySplit(GameState state, SplitAction split)
        {
            if (!IsLegalSplitTarget(state, split.UnitId, split.TargetX, split.TargetY))
                throw new IllegalActionException($"Illegal split: {split}");

            var next = state.Clone();
            var parent = next.GetUnit(split.UnitId);
            var childEchelon = EchelonScale.Smaller(parent.Echelon).Value;

            int totalHp = parent.Hp;
            int totalXp = parent.Xp;
            bool attacked = parent.HasAttacked;
            bool supported = parent.HasSupported;

            parent.Echelon = childEchelon;
            int halfMax = parent.Stats.MaxHp;
            int parentHp = Math.Min(halfMax, (totalHp + 1) / 2); // the parent keeps the odd point
            int childHp = Math.Min(halfMax, totalHp - parentHp);

            parent.Hp = Math.Max(1, parentHp);
            parent.Xp = (totalXp + 1) / 2;
            parent.HasMoved = true;

            var detachment = new Unit
            {
                Id = NextUnitId(next),
                Owner = parent.Owner,
                Type = parent.Type,
                Echelon = childEchelon,
                X = split.TargetX,
                Y = split.TargetY,
                Hp = Math.Max(1, childHp),
                Xp = totalXp / 2,
                HasMoved = true,
                HasAttacked = attacked,
                HasSupported = supported,
            };
            next.Units.Add(detachment);
            return next;
        }

        /// <summary>Ids are never reused, so a detachment always gets a fresh one.</summary>
        private static int NextUnitId(GameState state)
        {
            int highest = -1;
            foreach (var unit in state.Units) highest = Math.Max(highest, unit.Id);
            return highest + 1;
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
                unit.HasSupported = false;
            }
            return next;
        }
    }

    public sealed class IllegalActionException : Exception
    {
        public IllegalActionException(string message) : base(message) { }
    }
}
