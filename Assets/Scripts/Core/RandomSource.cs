using System;
using System.Collections.Generic;

namespace Tactix.Core
{
    /// <summary>
    /// Source of the random draws the rules engine consumes. Every stochastic
    /// outcome in the game comes from here and nowhere else — no ambient
    /// randomness — so a game can be replayed exactly from its logged draws.
    /// </summary>
    public interface IRandomSource
    {
        /// <summary>Next draw, uniform in [0, 1).</summary>
        double NextDouble();
    }

    /// <summary>A plain seeded source.</summary>
    public sealed class SeededRandom : IRandomSource
    {
        private readonly Random _random;

        public int Seed { get; }

        public SeededRandom(int seed)
        {
            Seed = seed;
            _random = new Random(seed);
        }

        public double NextDouble() => _random.NextDouble();
    }

    /// <summary>
    /// Wraps another source and records every value handed out, so the draws
    /// consumed while resolving one action can be written into the game log.
    /// </summary>
    public sealed class RecordingRandom : IRandomSource
    {
        private readonly IRandomSource _inner;
        private readonly List<double> _draws = new List<double>();

        public RecordingRandom(IRandomSource inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <summary>Draws taken since the last <see cref="Reset"/>, in order.</summary>
        public IReadOnlyList<double> Draws => _draws;

        public double NextDouble()
        {
            double value = _inner.NextDouble();
            _draws.Add(value);
            return value;
        }

        public void Reset() => _draws.Clear();
    }

    /// <summary>Replays a recorded sequence of draws; used to verify a logged game.</summary>
    public sealed class ReplayRandom : IRandomSource
    {
        private readonly IReadOnlyList<double> _draws;
        private int _next;

        public ReplayRandom(IReadOnlyList<double> draws)
        {
            _draws = draws ?? throw new ArgumentNullException(nameof(draws));
        }

        public double NextDouble()
        {
            if (_next >= _draws.Count)
                throw new InvalidOperationException("Replay ran out of recorded draws");
            return _draws[_next++];
        }
    }
}
