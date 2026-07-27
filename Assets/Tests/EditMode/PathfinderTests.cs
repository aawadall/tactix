using System.Linq;
using NUnit.Framework;

namespace Tactix.Core.Tests
{
    public class PathfinderTests
    {
        [Test]
        public void FlatTerrain_FindsDirectPath()
        {
            var state = Board();
            Assert.IsTrue(Pathfinder.TryFindPath(state, 5, 5, 10, 5, out var path));
            Assert.GreaterOrEqual(path.Count, 2);
            var last = path[path.Count - 1];
            Assert.AreEqual(10, last.x, 1e-6);
            Assert.AreEqual(5, last.y, 1e-6);
        }

        [Test]
        public void ImpassableTile_ForcesDetour()
        {
            var state = Board();
            // Block the direct row between (2,5) and (8,5).
            for (int x = 4; x <= 6; x++)
                state.WithTerrain(TerrainType.Impassable, (x, 5));

            Assert.IsTrue(Pathfinder.TryFindPath(state, 2, 5, 8, 5, out var path));
            foreach (var (x, y) in path)
            {
                int tx = Geometry.TileIndex(x);
                int ty = Geometry.TileIndex(y);
                if (tx >= 4 && tx <= 6 && ty == 5)
                    Assert.Fail($"Path crossed impassable tile ({tx},{ty})");
            }
        }

        [Test]
        public void Cliff_IsBlocked()
        {
            var state = Board()
                .WithElevation(0, (5, 5), (6, 5))
                .WithElevation(2, (7, 5));

            // A cliff between (6,5) and (7,5) — must route around.
            Assert.IsTrue(Pathfinder.TryFindPath(state, 5, 5, 8, 5, out var path));
            for (int i = 1; i < path.Count; i++)
            {
                int ax = Geometry.TileIndex(path[i - 1].x);
                int ay = Geometry.TileIndex(path[i - 1].y);
                int bx = Geometry.TileIndex(path[i].x);
                int by = Geometry.TileIndex(path[i].y);
                if (ax == 6 && ay == 5 && bx == 7 && by == 5)
                    Assert.Fail("Path crossed cliff");
            }
        }

        [Test]
        public void PrefersGentleSlope_OverSteepClimb()
        {
            // A cliff ridge at (2,5) blocks the direct row — path must detour flat.
            var state = Board()
                .WithElevation(2, (2, 5));

            Assert.IsTrue(Pathfinder.TryFindPath(state, 0, 5, 4, 5, out var path));
            bool crossesRidge = path.Any(p =>
                Geometry.TileIndex(p.x) == 2 && Geometry.TileIndex(p.y) == 5);
            Assert.IsFalse(crossesRidge, "Should detour around the cliff ridge");
        }

        [Test]
        public void Unreachable_ReturnsFalse()
        {
            var state = Board();
            state.WithTerrain(TerrainType.Impassable,
                (5, 4), (5, 5), (5, 6),
                (4, 4), (4, 5), (4, 6),
                (6, 4), (6, 5), (6, 6));
            // Goal inside a sealed pocket — only tile (5,5) is open in the ring.
            state.WithTerrain(TerrainType.Impassable, (5, 5));
            Assert.IsFalse(Pathfinder.TryFindPath(state, 0, 0, 5, 5, out _));
        }

        [Test]
        public void PathLength_MatchesWaypointSum()
        {
            var state = Board();
            Assert.IsTrue(Pathfinder.TryFindPath(state, 0, 0, 6, 0, out var path));
            double sum = 0;
            for (int i = 1; i < path.Count; i++)
                sum += Geometry.Distance(path[i - 1].x, path[i - 1].y, path[i].x, path[i].y);
            Assert.AreEqual(sum, Pathfinder.PathLength(state, 0, 0, 6, 0), 1e-6);
        }

        private static GameState Board() => TestBoards.OpenBoard(24, 24);
    }
}
