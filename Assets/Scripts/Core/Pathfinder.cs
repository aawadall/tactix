using System;
using System.Collections.Generic;

namespace Tactix.Core
{
    /// <summary>
    /// Tile-grid A* routing that prefers gentle slopes over steep climbs.
    /// Used for order planning, ETA estimates, and path overlays.
    /// </summary>
    public static class Pathfinder
    {
        /// <summary>Extra cost per elevation band crossed (on top of base 1.0).</summary>
        public const double SlopeWeight = 2.0;

        private static readonly (int dx, int dy)[] Neighbors =
        {
            (-1, -1), (0, -1), (1, -1),
            (-1, 0),           (1, 0),
            (-1, 1),  (0, 1),  (1, 1),
        };

        /// <summary>
        /// Finds a least-cost route from a world position to a goal. Waypoints
        /// begin at <paramref name="fromX"/>/<paramref name="fromY"/>, include
        /// tile centres along the path, and end at the goal.
        /// </summary>
        public static bool TryFindPath(
            GameState state,
            double fromX, double fromY,
            double toX, double toY,
            out List<(double x, double y)> waypoints)
        {
            waypoints = new List<(double x, double y)>();
            if (state == null) return false;

            int startX = Geometry.TileIndex(fromX);
            int startY = Geometry.TileIndex(fromY);
            int goalX = Geometry.TileIndex(toX);
            int goalY = Geometry.TileIndex(toY);

            if (!state.IsInBounds(startX, startY) || !state.IsInBounds(goalX, goalY))
                return false;

            if (startX == goalX && startY == goalY)
            {
                waypoints.Add((fromX, fromY));
                if (Geometry.Distance(fromX, fromY, toX, toY) > Geometry.Epsilon)
                    waypoints.Add((toX, toY));
                return true;
            }

            var tilePath = FindTilePath(state, startX, startY, goalX, goalY);
            if (tilePath == null || tilePath.Count == 0) return false;

            waypoints.Add((fromX, fromY));
            foreach (var (tx, ty) in tilePath)
            {
                var last = waypoints[waypoints.Count - 1];
                if (Geometry.Distance(last.x, last.y, tx, ty) > Geometry.Epsilon)
                    waypoints.Add((tx, ty));
            }

            var end = waypoints[waypoints.Count - 1];
            if (Geometry.Distance(end.x, end.y, toX, toY) > Geometry.Epsilon)
                waypoints.Add((toX, toY));

            return true;
        }

        /// <summary>Sum of edge costs along the least-slope path, or positive infinity when unreachable.</summary>
        public static double EstimatePathCost(GameState state, double fromX, double fromY, double toX, double toY)
        {
            if (!TryFindPath(state, fromX, fromY, toX, toY, out var waypoints) || waypoints.Count < 2)
                return double.PositiveInfinity;

            double cost = 0;
            for (int i = 1; i < waypoints.Count; i++)
            {
                int ax = Geometry.TileIndex(waypoints[i - 1].x);
                int ay = Geometry.TileIndex(waypoints[i - 1].y);
                int bx = Geometry.TileIndex(waypoints[i].x);
                int by = Geometry.TileIndex(waypoints[i].y);
                if (ax == bx && ay == by)
                    cost += Geometry.Distance(waypoints[i - 1].x, waypoints[i - 1].y,
                        waypoints[i].x, waypoints[i].y);
                else
                    cost += EdgeCost(state, ax, ay, bx, by);
            }
            return cost;
        }

        /// <summary>World-space length of the least-slope path.</summary>
        public static double PathLength(GameState state, double fromX, double fromY, double toX, double toY)
        {
            if (!TryFindPath(state, fromX, fromY, toX, toY, out var waypoints) || waypoints.Count < 2)
                return Geometry.Distance(fromX, fromY, toX, toY);

            double len = 0;
            for (int i = 1; i < waypoints.Count; i++)
                len += Geometry.Distance(waypoints[i - 1].x, waypoints[i - 1].y, waypoints[i].x, waypoints[i].y);
            return len;
        }

        private static List<(double x, double y)> FindTilePath(GameState state, int startX, int startY, int goalX, int goalY)
        {
            int w = state.Width, h = state.Height;
            int cells = w * h;
            var gScore = new double[cells];
            var cameFrom = new int[cells];
            var closed = new bool[cells];
            for (int i = 0; i < cells; i++)
            {
                gScore[i] = double.PositiveInfinity;
                cameFrom[i] = -1;
            }

            int startIdx = startY * w + startX;
            int goalIdx = goalY * w + goalX;
            gScore[startIdx] = 0;

            var open = new List<int> { startIdx };

            while (open.Count > 0)
            {
                int bestPos = 0;
                double bestF = double.PositiveInfinity;
                for (int i = 0; i < open.Count; i++)
                {
                    int idx = open[i];
                    double f = gScore[idx] + Heuristic(idx, goalX, goalY, w);
                    if (f < bestF)
                    {
                        bestF = f;
                        bestPos = i;
                    }
                }

                int current = open[bestPos];
                open.RemoveAt(bestPos);

                if (current == goalIdx)
                    return Reconstruct(state, cameFrom, current, w);

                if (closed[current]) continue;
                closed[current] = true;

                int cx = current % w;
                int cy = current / w;

                foreach (var (dx, dy) in Neighbors)
                {
                    int nx = cx + dx;
                    int ny = cy + dy;
                    if (!state.IsInBounds(nx, ny)) continue;

                    double edge = EdgeCost(state, cx, cy, nx, ny);
                    if (double.IsPositiveInfinity(edge)) continue;

                    int neighbor = ny * w + nx;
                    if (closed[neighbor]) continue;

                    double tentative = gScore[current] + edge;
                    if (tentative < gScore[neighbor])
                    {
                        cameFrom[neighbor] = current;
                        gScore[neighbor] = tentative;
                        if (!open.Contains(neighbor))
                            open.Add(neighbor);
                    }
                }
            }

            return null;
        }

        private static List<(double x, double y)> Reconstruct(GameState state, int[] cameFrom, int current, int w)
        {
            var tiles = new List<(int x, int y)>();
            while (current >= 0)
            {
                tiles.Add((current % w, current / w));
                current = cameFrom[current];
            }
            tiles.Reverse();

            // Skip the start tile — the caller already has the unit position.
            var result = new List<(double x, double y)>();
            for (int i = 1; i < tiles.Count; i++)
                result.Add((tiles[i].x, tiles[i].y));
            return result;
        }

        private static double Heuristic(int idx, int goalX, int goalY, int w)
        {
            int x = idx % w;
            int y = idx / w;
            return Math.Max(Math.Abs(goalX - x), Math.Abs(goalY - y));
        }

        private static double EdgeCost(GameState state, int x0, int y0, int x1, int y1)
        {
            if (!state.IsInBounds(x0, y0) || !state.IsInBounds(x1, y1)) return double.PositiveInfinity;
            if (state.TerrainAt(x1, y1) == TerrainType.Impassable) return double.PositiveInfinity;

            int e0 = state.ElevationAt(x0, y0);
            int e1 = state.ElevationAt(x1, y1);
            if (Math.Abs(e1 - e0) > 1) return double.PositiveInfinity;

            double dist = (x0 != x1 && y0 != y1) ? Math.Sqrt(2.0) : 1.0;
            return dist + SlopeWeight * Math.Abs(e1 - e0);
        }
    }
}
