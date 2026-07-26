using System;
using System.Collections.Generic;
using System.Linq;

namespace Tactix.Core
{
    /// <summary>
    /// Baseline opponent. It only ever plays actions drawn from the rules engine
    /// (sampled moves and enumerated attacks), never constructing an action of
    /// its own.
    ///
    /// Movement is uniform sampling of the legal region with an <em>advance
    /// bias</em>: with probability <see cref="AdvanceBias"/> it keeps the sampled
    /// destination that closes the most distance to the nearest enemy. Pure
    /// uniform play (bias 0) tends to wander indefinitely in continuous space,
    /// so the bias is what makes self-play games terminate and produce useful
    /// log data.
    /// </summary>
    public sealed class RandomBot
    {
        private const int MoveSamplesPerUnit = 12;
        private const double AttackProbability = 0.9;

        private readonly Random _rng;

        /// <summary>0 = pure uniform random, 1 = always advance on the nearest enemy.</summary>
        public double AdvanceBias { get; }

        /// <summary>
        /// Source for resolving outcomes under a stochastic ruleset. Kept separate
        /// from the bot's own decision randomness so that replaying a game's logged
        /// draws reproduces it regardless of which policy chose the actions.
        /// </summary>
        public IRandomSource OutcomeRandom { get; }

        public RandomBot(int? seed = null, double advanceBias = 0.6)
        {
            int resolved = seed ?? Environment.TickCount;
            _rng = new Random(resolved);
            OutcomeRandom = new SeededRandom(resolved ^ 0x5f3759df);
            AdvanceBias = advanceBias;
        }

        public GameAction ChooseAction(GameState state)
        {
            if (state.Winner != null) return new EndTurnAction();

            // Support is free (its own slot), so always spend it when available,
            // preferring the most badly hurt casualty.
            var heals = state.Units
                .Where(u => u.Owner == state.CurrentPlayer)
                .SelectMany(u => Rules.GetLegalHeals(state, u.Id))
                .ToList();
            if (heals.Count > 0)
            {
                return heals
                    .OrderBy(h => state.GetUnit(h.TargetUnitId).Hp)
                    .ThenBy(_ => _rng.Next())
                    .First();
            }

            var attacks = state.Units
                .Where(u => u.Owner == state.CurrentPlayer)
                .SelectMany(u => Rules.GetLegalAttacks(state, u.Id))
                .ToList();
            if (attacks.Count > 0 && _rng.NextDouble() < AttackProbability)
                return attacks[_rng.Next(attacks.Count)];

            var movable = state.Units
                .Where(u => u.Owner == state.CurrentPlayer && !u.HasMoved)
                .OrderBy(_ => _rng.Next())
                .ToList();

            foreach (var unit in movable)
            {
                var samples = Rules.SampleLegalMoves(state, unit.Id, MoveSamplesPerUnit, _rng);
                if (samples.Count == 0) continue;
                return _rng.NextDouble() < AdvanceBias
                    ? BestAdvance(state, unit, samples)
                    : samples[_rng.Next(samples.Count)];
            }

            return attacks.Count > 0 ? attacks[_rng.Next(attacks.Count)] : (GameAction)new EndTurnAction();
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
