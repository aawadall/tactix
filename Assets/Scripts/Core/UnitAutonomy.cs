using System;
using System.Collections.Generic;
using System.Linq;

namespace Tactix.Core
{
    /// <summary>
    /// Per-unit initiative when a human player has not queued orders. Uses the
    /// same priorities as <see cref="RandomBot"/> (heal, attack, reorganise, advance).
    /// </summary>
    public static class UnitAutonomy
    {
        public const int MoveSamplesPerUnit = 12;
        public const double AttackProbability = 0.9;
        public const double ReorganiseProbability = 0.08;
        public const double DefaultAdvanceBias = 0.6;

        /// <summary>
        /// Proposes one legal action for <paramref name="unitId"/> on the current
        /// ply, or null when the unit has nothing useful left to do.
        /// </summary>
        public static GameAction TryStep(
            GameState state,
            int unitId,
            Random rng,
            double advanceBias = DefaultAdvanceBias,
            bool includeHeal = true)
        {
            if (state == null || state.IsOver || rng == null) return null;

            var unit = state.GetUnit(unitId);
            if (unit == null || unit.Owner != state.CurrentPlayer) return null;

            if (includeHeal)
            {
                var heal = PickHealForUnit(state, unitId);
                if (heal != null) return heal;
            }

            var attacks = Rules.GetLegalAttacks(state, unitId);
            if (attacks.Count > 0 && rng.NextDouble() < AttackProbability)
                return attacks[rng.Next(attacks.Count)];

            if (!unit.HasMoved && rng.NextDouble() < ReorganiseProbability)
            {
                var reorg = TryReorganise(state, unitId, rng);
                if (reorg != null) return reorg;
            }

            if (!unit.HasMoved)
            {
                var samples = Rules.SampleLegalMoves(state, unitId, MoveSamplesPerUnit, rng);
                if (samples.Count > 0)
                {
                    return rng.NextDouble() < advanceBias
                        ? BestAdvance(state, unit, samples)
                        : samples[rng.Next(samples.Count)];
                }
            }

            if (attacks.Count > 0)
                return attacks[rng.Next(attacks.Count)];

            return null;
        }

        /// <summary>
        /// Best heal any friendly unit can deliver this ply (most wounded first).
        /// Used by <see cref="RandomBot"/> before per-unit steps.
        /// </summary>
        public static GameAction PickBestHeal(GameState state, Random rng)
        {
            if (state == null || rng == null) return null;

            var heals = state.Units
                .Where(u => u.Owner == state.CurrentPlayer)
                .SelectMany(u => Rules.GetLegalHeals(state, u.Id))
                .ToList();
            if (heals.Count == 0) return null;

            return heals
                .OrderBy(h => state.GetUnit(h.TargetUnitId).Hp)
                .ThenBy(_ => rng.Next())
                .First();
        }

        private static HealAction PickHealForUnit(GameState state, int unitId)
        {
            var heals = Rules.GetLegalHeals(state, unitId);
            if (heals.Count == 0) return null;

            return heals
                .OrderBy(h => state.GetUnit(h.TargetUnitId).Hp)
                .First();
        }

        private static GameAction TryReorganise(GameState state, int unitId, Random rng)
        {
            var unit = state.GetUnit(unitId);
            if (unit == null || unit.HasMoved) return null;

            var merges = Rules.GetLegalMerges(state, unitId);
            var splits = Rules.SampleLegalSplits(state, unitId, 3, rng);

            bool preferMerge = merges.Count > 0 && (splits.Count == 0 || rng.NextDouble() < 0.5);
            if (preferMerge) return merges[rng.Next(merges.Count)];
            if (splits.Count > 0) return splits[rng.Next(splits.Count)];
            return null;
        }

        private static MoveAction BestAdvance(GameState state, Unit unit, List<MoveAction> samples)
        {
            var enemies = state.Units.Where(u => u.Owner != unit.Owner).ToList();
            if (enemies.Count == 0) return samples[0];

            MoveAction best = samples[0];
            double bestDistance = double.MaxValue;
            foreach (var move in samples)
            {
                double nearest = enemies.Min(e => Rules.Distance(move.TargetX, move.TargetY, e.X, e.Y));
                if (nearest < bestDistance)
                {
                    bestDistance = nearest;
                    best = move;
                }
            }
            return best;
        }
    }
}
