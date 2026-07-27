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
        private readonly Random _rng;

        /// <summary>0 = pure uniform random, 1 = always advance on the nearest enemy.</summary>
        public double AdvanceBias { get; }

        /// <summary>
        /// Source for resolving outcomes under a stochastic ruleset. Kept separate
        /// from the bot's own decision randomness so that replaying a game's logged
        /// draws reproduces it regardless of which policy chose the actions.
        /// </summary>
        public IRandomSource OutcomeRandom { get; }

        public RandomBot(int? seed = null, double advanceBias = UnitAutonomy.DefaultAdvanceBias)
        {
            int resolved = seed ?? Environment.TickCount;
            _rng = new Random(resolved);
            OutcomeRandom = new SeededRandom(resolved ^ 0x5f3759df);
            AdvanceBias = advanceBias;
        }

        public GameAction ChooseAction(GameState state)
        {
            if (state.IsOver) return new EndTurnAction();

            var heal = UnitAutonomy.PickBestHeal(state, _rng);
            if (heal != null) return heal;

            var units = state.Units
                .Where(u => u.Owner == state.CurrentPlayer)
                .OrderBy(_ => _rng.Next())
                .ToList();

            foreach (var unit in units)
            {
                var action = UnitAutonomy.TryStep(state, unit.Id, _rng, AdvanceBias, includeHeal: false);
                if (action != null) return action;
            }

            var attacks = state.Units
                .Where(u => u.Owner == state.CurrentPlayer)
                .SelectMany(u => Rules.GetLegalAttacks(state, u.Id))
                .ToList();
            return attacks.Count > 0 ? attacks[_rng.Next(attacks.Count)] : (GameAction)new EndTurnAction();
        }
    }
}
