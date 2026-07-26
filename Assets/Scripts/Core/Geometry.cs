using System;
using System.Collections.Generic;

namespace Tactix.Core
{
    /// <summary>
    /// Continuous-space geometry over the tile raster.
    ///
    /// Positions are world coordinates (doubles). The terrain and elevation
    /// rasters are sampled by tile: tile (i,j) is centered at (i,j) and covers
    /// [i-0.5, i+0.5) x [j-0.5, j+0.5), so the playable area is
    /// [-0.5, Width-0.5] x [-0.5, Height-0.5].
    /// </summary>
    public static class Geometry
    {
        public const double Epsilon = 1e-9;
        /// <summary>Clearance kept between a legal destination and a blocked tile edge.</summary>
        public const double PathBackoff = 1e-3;

        public static int TileIndex(double worldCoord) => (int)Math.Floor(worldCoord + 0.5);

        public static double Distance(double x0, double y0, double x1, double y1)
        {
            double dx = x1 - x0, dy = y1 - y0;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// Tiles crossed by the segment, in order, with the parametric position
        /// (0..1 along the segment) at which each tile is entered. The starting
        /// tile is yielded first at t = 0. Amanatides-Woo voxel traversal.
        /// </summary>
        public static IEnumerable<(int x, int y, double t)> TraverseTiles(double x0, double y0, double x1, double y1)
        {
            int i = TileIndex(x0), j = TileIndex(y0);
            yield return (i, j, 0.0);

            double dx = x1 - x0, dy = y1 - y0;
            if (Math.Abs(dx) < Epsilon && Math.Abs(dy) < Epsilon) yield break;

            int stepX = dx > 0 ? 1 : dx < 0 ? -1 : 0;
            int stepY = dy > 0 ? 1 : dy < 0 ? -1 : 0;

            double tMaxX = stepX != 0 ? ((i + stepX * 0.5) - x0) / dx : double.PositiveInfinity;
            double tMaxY = stepY != 0 ? ((j + stepY * 0.5) - y0) / dy : double.PositiveInfinity;
            double tDeltaX = stepX != 0 ? Math.Abs(1.0 / dx) : double.PositiveInfinity;
            double tDeltaY = stepY != 0 ? Math.Abs(1.0 / dy) : double.PositiveInfinity;

            while (true)
            {
                double t;
                if (tMaxX < tMaxY)
                {
                    t = tMaxX;
                    if (t > 1.0) yield break;
                    i += stepX;
                    tMaxX += tDeltaX;
                }
                else
                {
                    t = tMaxY;
                    if (t > 1.0) yield break;
                    j += stepY;
                    tMaxY += tDeltaY;
                }
                yield return (i, j, t);
            }
        }

        /// <summary>
        /// True when a unit may walk the straight segment: it stays in bounds and
        /// never enters an impassable tile or steps across a cliff (an elevation
        /// change of 2 or more between consecutive tiles).
        /// </summary>
        public static bool IsPathWalkable(GameState state, double x0, double y0, double x1, double y1)
        {
            return RayDistance(state, x0, y0, x1 - x0, y1 - y0, Distance(x0, y0, x1, y1), out _);
        }

        /// <summary>
        /// How far a unit can travel from (x0,y0) along the given direction before
        /// terrain stops it. Returns true when the full <paramref name="maxDistance"/>
        /// is walkable; <paramref name="reachable"/> always holds the usable distance
        /// (backed off slightly from the blocking edge).
        /// </summary>
        public static bool RayDistance(GameState state, double x0, double y0, double dirX, double dirY, double maxDistance, out double reachable)
        {
            reachable = 0.0;
            double len = Math.Sqrt(dirX * dirX + dirY * dirY);
            if (len < Epsilon || maxDistance <= 0)
            {
                reachable = 0.0;
                return true;
            }
            double ux = dirX / len, uy = dirY / len;
            double x1 = x0 + ux * maxDistance, y1 = y0 + uy * maxDistance;

            int prevX = TileIndex(x0), prevY = TileIndex(y0);
            foreach (var (tx, ty, t) in TraverseTiles(x0, y0, x1, y1))
            {
                if (tx == prevX && ty == prevY && t == 0.0) continue; // starting tile

                bool blocked =
                    !state.IsInBounds(tx, ty) ||
                    state.TerrainAt(tx, ty) == TerrainType.Impassable ||
                    Math.Abs(state.ElevationAt(tx, ty) - state.ElevationAt(prevX, prevY)) > 1;

                if (blocked)
                {
                    reachable = Math.Max(0.0, t * maxDistance - PathBackoff);
                    return false;
                }
                prevX = tx;
                prevY = ty;
            }
            reachable = maxDistance;
            return true;
        }

        /// <summary>True when the point lies inside the board's playable rectangle.</summary>
        public static bool IsInsideBoard(GameState state, double x, double y)
        {
            return x >= -0.5 && y >= -0.5 && x <= state.Width - 0.5 && y <= state.Height - 0.5;
        }
    }
}
