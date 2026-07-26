using System;

namespace Tactix.Core
{
    /// <summary>
    /// Topographic line-of-sight. The sight line runs from the attacker's eye
    /// point (tile elevation + 1) to the target's eye point (tile elevation + 1)
    /// along the segment between tile centers. A tile crossed by the segment
    /// (positive-length interior crossing — corner/edge touches never count)
    /// blocks sight iff its effective height rises to or above the sight line
    /// anywhere over the crossing:
    ///   open tile:       elevation
    ///   forest tile:     elevation + 1 (canopy)
    ///   impassable tile: elevation + 3 (wall — taller than the whole relief,
    ///                    but a high vantage can still see over a low wall)
    /// The attacker's and target's own tiles never block. On an all-zero
    /// elevation map this reduces exactly to the flat v2 rule (forest and
    /// impassable block, open never does). Symmetric: LOS(a,b) == LOS(b,a).
    /// </summary>
    public static class LineOfSight
    {
        private const double Eps = 1e-9;
        private const double EyeHeight = 1.0;
        private const double CanopyHeight = 1.0;
        private const double WallHeight = 3.0;

        public static bool HasLineOfSight(GameState state, double x0, double y0, double x1, double y1)
        {
            double h0 = state.ElevationAtPoint(x0, y0) + EyeHeight;
            double h1 = state.ElevationAtPoint(x1, y1) + EyeHeight;

            int originTileX = Geometry.TileIndex(x0), originTileY = Geometry.TileIndex(y0);
            int targetTileX = Geometry.TileIndex(x1), targetTileY = Geometry.TileIndex(y1);

            int minX = Math.Min(originTileX, targetTileX);
            int maxX = Math.Max(originTileX, targetTileX);
            int minY = Math.Min(originTileY, targetTileY);
            int maxY = Math.Max(originTileY, targetTileY);

            for (int ty = minY; ty <= maxY; ty++)
            {
                for (int tx = minX; tx <= maxX; tx++)
                {
                    if (tx == originTileX && ty == originTileY) continue; // attacker's tile
                    if (tx == targetTileX && ty == targetTileY) continue; // target's tile
                    if (!state.IsInBounds(tx, ty)) continue;

                    double effective = state.ElevationAt(tx, ty);
                    switch (state.TerrainAt(tx, ty))
                    {
                        case TerrainType.Forest: effective += CanopyHeight; break;
                        case TerrainType.Impassable: effective += WallHeight; break;
                    }
                    // The sight line is always at least EyeHeight above the lower
                    // endpoint's ground; a tile of zero effective height can't block.
                    if (effective < Eps) continue;

                    if (!TryGetCrossingInterval(x0, y0, x1, y1, tx, ty, out double t0, out double t1)) continue;

                    // Line height is linear in t; the blocker pokes the line iff it
                    // reaches the line's minimum over the crossing interval.
                    double lineMin = Math.Min(Lerp(h0, h1, t0), Lerp(h0, h1, t1));
                    if (effective >= lineMin - Eps) return false;
                }
            }
            return true;
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        /// <summary>
        /// Liang-Barsky clip of the segment (x0,y0)->(x1,y1) against the unit tile
        /// centered at (tx,ty). True only for a positive-length interior crossing,
        /// with the crossing parameter interval returned in [t0,t1].
        /// </summary>
        private static bool TryGetCrossingInterval(double x0, double y0, double x1, double y1, int tx, int ty, out double t0, out double t1)
        {
            double dx = x1 - x0;
            double dy = y1 - y0;
            t0 = 0.0;
            t1 = 1.0;

            if (!ClipEdge(-dx, x0 - (tx - 0.5), ref t0, ref t1)) return false; // left
            if (!ClipEdge(dx, (tx + 0.5) - x0, ref t0, ref t1)) return false;  // right
            if (!ClipEdge(-dy, y0 - (ty - 0.5), ref t0, ref t1)) return false; // bottom
            if (!ClipEdge(dy, (ty + 0.5) - y0, ref t0, ref t1)) return false;  // top

            return t1 - t0 > Eps;
        }

        private static bool ClipEdge(double p, double q, ref double t0, ref double t1)
        {
            if (Math.Abs(p) < Eps) return q >= -Eps; // parallel: inside or on the boundary line
            double r = q / p;
            if (p < 0)
            {
                if (r > t1) return false;
                if (r > t0) t0 = r;
            }
            else
            {
                if (r < t0) return false;
                if (r < t1) t1 = r;
            }
            return true;
        }
    }
}
