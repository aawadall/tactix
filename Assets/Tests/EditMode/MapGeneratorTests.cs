using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Tactix.Core.Tests
{
    /// <summary>
    /// Generated maps must be fair (symmetric), playable (both armies deployed
    /// legally and able to reach each other), and reproducible (seeded).
    /// </summary>
    public class MapGeneratorTests
    {
        private static readonly int[] Seeds = { 1, 7, 42, 1234, 20260726 };

        [Test]
        public void Generated_IsDeterministicForASeed_AndVariesBetweenSeeds()
        {
            string first = MapGenerator.Generate(24, 24, 99).ToJson();
            string again = MapGenerator.Generate(24, 24, 99).ToJson();
            Assert.AreEqual(first, again, "same seed must produce the same map");

            string other = MapGenerator.Generate(24, 24, 100).ToJson();
            Assert.AreNotEqual(first, other, "different seeds should produce different maps");
        }

        [Test]
        public void Generated_IsRotationallySymmetric([ValueSource(nameof(Seeds))] int seed)
        {
            var state = MapGenerator.Generate(24, 24, seed);
            int w = state.Width, h = state.Height;

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Assert.AreEqual(state.TerrainAt(x, y), state.TerrainAt(w - 1 - x, h - 1 - y),
                        $"terrain asymmetric at ({x},{y}) for seed {seed}");
                    Assert.AreEqual(state.ElevationAt(x, y), state.ElevationAt(w - 1 - x, h - 1 - y),
                        $"elevation asymmetric at ({x},{y}) for seed {seed}");
                }
        }

        [Test]
        public void Generated_DeploysBothArmiesLegally([ValueSource(nameof(Seeds))] int seed)
        {
            var state = MapGenerator.Generate(24, 24, seed);
            Assert.AreEqual(28, state.Units.Count);
            Assert.AreEqual(14, state.Units.Count(u => u.Owner == 0));

            foreach (var unit in state.Units)
            {
                Assert.AreNotEqual(TerrainType.Impassable, state.TerrainAtPoint(unit.X, unit.Y));
                Assert.IsTrue(Geometry.IsInsideBoard(state, unit.X, unit.Y));
                foreach (var other in state.Units)
                {
                    if (other.Id == unit.Id) continue;
                    Assert.GreaterOrEqual(Rules.Distance(unit.X, unit.Y, other.X, other.Y),
                        unit.Stats.Radius + other.Stats.Radius,
                        $"units {unit.Id} and {other.Id} overlap on seed {seed}");
                }
            }

            foreach (var unit in state.Units.Where(u => u.Owner == 0))
                Assert.IsTrue(Rules.CanMove(state, unit.Id), $"unit {unit.Id} boxed in on seed {seed}");
        }

        [Test]
        public void Generated_ArmiesCanReachEachOther([ValueSource(nameof(Seeds))] int seed)
        {
            var state = MapGenerator.Generate(24, 24, seed);
            Assert.IsTrue(ArmiesConnected(state), $"armies cannot reach each other on seed {seed}");
        }

        [Test]
        public void Generated_HasVariedReliefAndTerrain([ValueSource(nameof(Seeds))] int seed)
        {
            var state = MapGenerator.Generate(24, 24, seed);
            var elevations = new HashSet<int>();
            int forest = 0, rock = 0, tiles = state.Width * state.Height;

            for (int y = 0; y < state.Height; y++)
                for (int x = 0; x < state.Width; x++)
                {
                    elevations.Add(state.ElevationAt(x, y));
                    if (state.TerrainAt(x, y) == TerrainType.Forest) forest++;
                    if (state.TerrainAt(x, y) == TerrainType.Impassable) rock++;
                }

            Assert.GreaterOrEqual(elevations.Count, 3, $"relief too flat on seed {seed}");
            Assert.Greater(forest, tiles * 0.03, $"almost no forest on seed {seed}");
            Assert.Less(forest + rock, tiles * 0.55, $"map is choked with obstacles on seed {seed}");
        }

        [Test]
        public void Generated_DeploymentZonesAreFlatAndOpen([ValueSource(nameof(Seeds))] int seed)
        {
            var state = MapGenerator.Generate(24, 24, seed);
            for (int y = 0; y < MapGenerator.DeploymentDepth; y++)
                for (int x = 0; x < state.Width; x++)
                    foreach (int row in new[] { y, state.Height - 1 - y })
                    {
                        Assert.AreEqual(TerrainType.Open, state.TerrainAt(x, row));
                        Assert.AreEqual(0, state.ElevationAt(x, row));
                    }
        }

        [Test]
        public void Generated_SupportsOtherBoardSizes()
        {
            foreach (int size in new[] { 16, 20, 32 })
            {
                var state = MapGenerator.Generate(size, size, 5);
                Assert.AreEqual(size, state.Width);
                Assert.AreEqual(28, state.Units.Count);
                Assert.IsTrue(ArmiesConnected(state), $"armies cannot meet on a {size}x{size} map");
            }

            // Non-square boards work too.
            var wide = MapGenerator.Generate(32, 18, 5);
            Assert.AreEqual(32, wide.Width);
            Assert.AreEqual(18, wide.Height);
            Assert.IsTrue(ArmiesConnected(wide));

            Assert.Throws<ArgumentException>(() => MapGenerator.Generate(8, 8, 1));
        }

        [Test]
        public void Generated_MapsArePlayableToCompletion()
        {
            var bot = new RandomBot(seed: 3);
            var state = MapGenerator.Generate(24, 24, 777);
            int steps = 0;
            while (state.Winner == null && steps < 20000)
            {
                var action = bot.ChooseAction(state);
                Assert.DoesNotThrow(() => state = Rules.Apply(state, action), $"rejected {action}");
                steps++;
            }
            Assert.IsNotNull(state.Winner, "self-play on a generated map did not terminate");
        }

        /// <summary>Half-tile flood fill using the real path rule, from one army to the other.</summary>
        private static bool ArmiesConnected(GameState state)
        {
            const double step = 0.5;
            var start = Snap(state.Units.First(u => u.Owner == 0), step);
            var goal = Snap(state.Units.First(u => u.Owner == 1), step);

            var seen = new HashSet<(double, double)> { start };
            var queue = new Queue<(double x, double y)>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        double nx = cx + dx * step, ny = cy + dy * step;
                        if (!Geometry.IsInsideBoard(state, nx, ny) || seen.Contains((nx, ny))) continue;
                        if (state.TerrainAtPoint(nx, ny) == TerrainType.Impassable) continue;
                        if (!Geometry.IsPathWalkable(state, cx, cy, nx, ny)) continue;
                        seen.Add((nx, ny));
                        queue.Enqueue((nx, ny));
                    }
            }
            return seen.Contains(goal);
        }

        private static (double, double) Snap(Unit unit, double step) =>
            (Math.Round(unit.X / step) * step, Math.Round(unit.Y / step) * step);
    }
}
