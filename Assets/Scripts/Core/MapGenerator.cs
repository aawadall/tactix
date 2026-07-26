using System;
using System.Collections.Generic;

namespace Tactix.Core
{
    /// <summary>
    /// Procedural map generation: fractal value noise for the relief, symmetrized
    /// through the board centre so neither player gets the better ground, quantized
    /// into elevation bands, then repaired so every tile stays reachable under the
    /// cliff rule.
    ///
    /// Generation is fully deterministic for a given (width, height, seed), and the
    /// seed is recorded in the game log, so any logged game can be reproduced
    /// exactly. This is the same procedure that produced the baked standard map in
    /// <see cref="LevelConfig"/>.
    /// </summary>
    public static class MapGenerator
    {
        /// <summary>Rows at each end kept flat and open, so both armies deploy on clear ground.</summary>
        public const int DeploymentDepth = 3;

        /// <summary>Smallest board the standard armies fit on.</summary>
        public const int MinimumSize = 16;

        public static GameState Generate(int width, int height, int seed)
        {
            if (width < MinimumSize || height < MinimumSize)
                throw new ArgumentException($"Maps must be at least {MinimumSize}x{MinimumSize}", nameof(width));

            var rng = new Random(seed);

            var height01 = Normalize(Symmetrize(Field(width, height, rng, new[] { 3, 6, 12 }, new[] { 1.0, 0.5, 0.22 })));
            TaperDeploymentEdges(height01, width, height);
            height01 = Normalize(height01);

            var elevation = Quantize(height01, width, height);
            FlattenDeploymentZones(elevation, width, height);
            RepairReachability(elevation, width, height);

            var forest = Normalize(Symmetrize(Field(width, height, rng, new[] { 4, 9 }, new[] { 1.0, 0.55 })));
            var rock = Normalize(Symmetrize(Field(width, height, rng, new[] { 5, 11 }, new[] { 1.0, 0.5 })));
            var terrain = PaintTerrain(elevation, forest, rock, width, height);
            ClearRockBlockages(terrain, elevation, width, height);

            var state = new GameState
            {
                Terrain = terrain,
                Elevation = ToJagged(elevation, width, height),
                Units = new List<Unit>(),
                CurrentPlayer = 0,
                TurnPhase = TurnPhase.Move,
                TurnNumber = 1,
                Winner = null,
            };

            LevelConfig.DeployStandardArmies(state);
            return state;
        }

        // ---------- relief ----------

        private static double[,] Field(int width, int height, Random rng, int[] octaveCells, double[] weights)
        {
            var grids = new double[octaveCells.Length][,];
            for (int k = 0; k < octaveCells.Length; k++) grids[k] = NoiseGrid(octaveCells[k], rng);

            var field = new double[width, height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    double u = x / (width - 1.0), v = y / (height - 1.0), sum = 0;
                    for (int k = 0; k < octaveCells.Length; k++)
                        sum += weights[k] * Sample(grids[k], octaveCells[k], u, v);
                    field[x, y] = sum;
                }
            return field;
        }

        private static double[,] NoiseGrid(int cells, Random rng)
        {
            var grid = new double[cells + 1, cells + 1];
            for (int j = 0; j <= cells; j++)
                for (int i = 0; i <= cells; i++) grid[i, j] = rng.NextDouble();
            return grid;
        }

        private static double Sample(double[,] grid, int cells, double u, double v)
        {
            double fx = u * cells, fy = v * cells;
            int x0 = Math.Min((int)fx, cells - 1), y0 = Math.Min((int)fy, cells - 1);
            double tx = Smoothstep(fx - x0), ty = Smoothstep(fy - y0);
            double bottom = Lerp(grid[x0, y0], grid[x0 + 1, y0], tx);
            double top = Lerp(grid[x0, y0 + 1], grid[x0 + 1, y0 + 1], tx);
            return Lerp(bottom, top, ty);
        }

        private static double Smoothstep(double t) => t * t * (3 - 2 * t);
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        private static double[,] Symmetrize(double[,] field)
        {
            int width = field.GetLength(0), height = field.GetLength(1);
            var output = new double[width, height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    output[x, y] = 0.5 * (field[x, y] + field[width - 1 - x, height - 1 - y]);
            return output;
        }

        private static double[,] Normalize(double[,] field)
        {
            int width = field.GetLength(0), height = field.GetLength(1);
            double min = double.MaxValue, max = double.MinValue;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    min = Math.Min(min, field[x, y]);
                    max = Math.Max(max, field[x, y]);
                }

            var output = new double[width, height];
            double range = max - min;
            if (range < 1e-12) return output;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    output[x, y] = (field[x, y] - min) / range;
            return output;
        }

        /// <summary>Pulls the relief down toward both deployment edges so armies start on low, open ground.</summary>
        private static void TaperDeploymentEdges(double[,] field, int width, int height)
        {
            for (int y = 0; y < height; y++)
            {
                double edge = Math.Min(y, height - 1 - y) / ((height - 1) / 2.0);
                double falloff = Math.Min(1.0, edge * 1.9);
                for (int x = 0; x < width; x++) field[x, y] *= falloff;
            }
        }

        private static int[,] Quantize(double[,] field, int width, int height)
        {
            var values = new List<double>(width * height);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++) values.Add(field[x, y]);
            values.Sort();

            double t1 = values[(int)(values.Count * 0.44)];
            double t2 = values[(int)(values.Count * 0.74)];
            double t3 = values[(int)(values.Count * 0.92)];

            var elevation = new int[width, height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    double v = field[x, y];
                    elevation[x, y] = v >= t3 ? 3 : v >= t2 ? 2 : v >= t1 ? 1 : 0;
                }
            return elevation;
        }

        private static void FlattenDeploymentZones(int[,] elevation, int width, int height)
        {
            for (int y = 0; y < DeploymentDepth; y++)
                for (int x = 0; x < width; x++)
                {
                    elevation[x, y] = 0;
                    elevation[x, height - 1 - y] = 0;
                }
        }

        /// <summary>
        /// Lowers cliff shoulders until every tile is reachable from the corner
        /// under the |Δelevation| ≤ 1 movement rule, keeping edits symmetric.
        /// </summary>
        private static void RepairReachability(int[,] elevation, int width, int height)
        {
            for (int pass = 0; pass < 60; pass++)
            {
                var reached = Flood(elevation, null, width, height);
                var edits = new List<(int x, int y, int e)>();
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        if (reached[x, y]) continue;
                        for (int dx = -1; dx <= 1; dx++)
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                int nx = x + dx, ny = y + dy;
                                if (nx < 0 || ny < 0 || nx >= width || ny >= height || !reached[nx, ny]) continue;
                                if (elevation[x, y] - elevation[nx, ny] > 1)
                                    edits.Add((x, y, elevation[nx, ny] + 1));
                            }
                    }
                if (edits.Count == 0) return;
                foreach (var (x, y, e) in edits)
                {
                    elevation[x, y] = e;
                    elevation[width - 1 - x, height - 1 - y] = e;
                }
            }
        }

        // ---------- terrain ----------

        private static TerrainType[][] PaintTerrain(int[,] elevation, double[,] forest, double[,] rock, int width, int height)
        {
            var terrain = new TerrainType[height][];
            for (int y = 0; y < height; y++)
            {
                terrain[y] = new TerrainType[width];
                for (int x = 0; x < width; x++)
                {
                    var type = TerrainType.Open;
                    if (forest[x, y] > 0.60 && elevation[x, y] <= 2) type = TerrainType.Forest;
                    if (rock[x, y] > 0.86 && elevation[x, y] >= 2) type = TerrainType.Impassable;
                    terrain[y][x] = type;
                }
            }
            for (int y = 0; y < DeploymentDepth; y++)
                for (int x = 0; x < width; x++)
                {
                    terrain[y][x] = TerrainType.Open;
                    terrain[height - 1 - y][x] = TerrainType.Open;
                }
            return terrain;
        }

        /// <summary>Opens any rock formation that would seal off part of the map.</summary>
        private static void ClearRockBlockages(TerrainType[][] terrain, int[,] elevation, int width, int height)
        {
            for (int pass = 0; pass < 40; pass++)
            {
                var reached = Flood(elevation, terrain, width, height);
                bool complete = true;
                for (int y = 0; y < height && complete; y++)
                    for (int x = 0; x < width; x++)
                        if (terrain[y][x] != TerrainType.Impassable && !reached[x, y]) { complete = false; break; }
                if (complete) return;

                var openings = new List<(int x, int y)>();
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        if (terrain[y][x] != TerrainType.Impassable) continue;
                        bool touchesReached = false, touchesCutOff = false;
                        for (int dx = -1; dx <= 1; dx++)
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                int nx = x + dx, ny = y + dy;
                                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                                if (terrain[ny][nx] == TerrainType.Impassable) continue;
                                if (Math.Abs(elevation[nx, ny] - elevation[x, y]) > 1) continue;
                                if (reached[nx, ny]) touchesReached = true; else touchesCutOff = true;
                            }
                        if (touchesReached && touchesCutOff) openings.Add((x, y));
                    }
                if (openings.Count == 0) return;
                foreach (var (x, y) in openings)
                {
                    terrain[y][x] = TerrainType.Open;
                    terrain[height - 1 - y][width - 1 - x] = TerrainType.Open;
                }
            }
        }

        // ---------- helpers ----------

        private static bool[,] Flood(int[,] elevation, TerrainType[][] terrain, int width, int height)
        {
            var seen = new bool[width, height];
            var queue = new Queue<(int x, int y)>();
            seen[0, 0] = true;
            queue.Enqueue((0, 0));
            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height || seen[nx, ny]) continue;
                        if (terrain != null && terrain[ny][nx] == TerrainType.Impassable) continue;
                        if (Math.Abs(elevation[nx, ny] - elevation[cx, cy]) > 1) continue;
                        seen[nx, ny] = true;
                        queue.Enqueue((nx, ny));
                    }
            }
            return seen;
        }

        private static int[][] ToJagged(int[,] elevation, int width, int height)
        {
            var jagged = new int[height][];
            for (int y = 0; y < height; y++)
            {
                jagged[y] = new int[width];
                for (int x = 0; x < width; x++) jagged[y][x] = elevation[x, y];
            }
            return jagged;
        }
    }
}
