using System;
using System.Linq;

namespace Tactix.Core
{
    /// <summary>
    /// Baseline opponent: picks uniformly at random among the legal Move and
    /// Attack actions, and ends the turn only when no other action is available.
    /// Only ever constructs actions from <see cref="Rules.GetAllLegalActions"/>.
    /// </summary>
    public sealed class RandomBot
    {
        private readonly Random _rng;

        public RandomBot(int? seed = null)
        {
            _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public GameAction ChooseAction(GameState state)
        {
            var options = Rules.GetAllLegalActions(state)
                .Where(a => !(a is EndTurnAction))
                .ToList();
            if (options.Count == 0) return new EndTurnAction();
            return options[_rng.Next(options.Count)];
        }
    }
}
