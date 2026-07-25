using System;

namespace Tactix.Core
{
    /// <summary>
    /// Line-of-sight rule: sight runs along the segment between tile centers.
    /// A forest or impassable tile blocks sight iff the segment crosses that
    /// tile's *interior* (positive-length crossing). Touching only a corner or
    /// edge point does not block. The attacker's and target's own tiles never
    /// block. The rule is symmetric: LOS(a,b) == LOS(b,a).
    /// </summary>
    public static class LineOfSight
    {
        private const double Eps = 1e-9;

        public static bool HasLineOfSight(GameState state, int x0, int y0, int x1, int y1)
        {
            int minX = Math.Min(x0, x1);
            int maxX = Math.Max(x0, x1);
            int minY = Math.Min(y0, y1);
            int maxY = Math.Max(y0, y1);

            for (int ty = minY; ty <= maxY; ty++)
            {
                for (int tx = minX; tx <= maxX; tx++)
                {
                    if (tx == x0 && ty == y0) continue; // attacker tile
                    if (tx == x1 && ty == y1) continue; // target tile
                    var terrain = state.TerrainAt(tx, ty);
                    if (terrain != TerrainType.Forest && terrain != TerrainType.Impassable) continue;
                    if (SegmentCrossesTileInterior(x0, y0, x1, y1, tx, ty)) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Liang-Barsky clip of the segment (x0,y0)->(x1,y1) against the unit tile
        /// centered at (tx,ty). Returns true only for a positive-length crossing.
        /// </summary>
        private static bool SegmentCrossesTileInterior(double x0, double y0, double x1, double y1, int tx, int ty)
        {
            double dx = x1 - x0;
            double dy = y1 - y0;
            double t0 = 0.0, t1 = 1.0;

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
