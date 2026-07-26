using System.Collections.Generic;
using Tactix.Core;
using UnityEngine;

namespace Tactix.Game
{
    /// <summary>
    /// Draws the continuous board: terrain raster, topographic contour lines
    /// (marching squares over the elevation field), spot heights on summits,
    /// the selected unit's reachable region, and units at their world positions.
    /// Grid coordinates map 1:1 to world XY; tile (i,j) is centred on (i,j).
    /// </summary>
    public sealed class BoardRenderer : MonoBehaviour
    {
        private const float TileZ = 0f;
        private const float OverlayZ = -0.1f;
        private const float UnitZ = -0.2f;
        private const float TextZ = -0.3f;

        // Sprite sorting: terrain < region < contours < units < labels.
        private const int TileOrder = 0;
        private const int RegionOrder = 1;
        private const int ContourOrder = 2;
        private const int UnitOrder = 3;
        private const int LabelOrder = 4;

        private readonly List<GameObject> _terrain = new List<GameObject>();
        private readonly List<GameObject> _overlays = new List<GameObject>();
        private readonly List<GameObject> _unitObjects = new List<GameObject>();
        private GameObject _moveRegion;

        // ---------- terrain, contours, spot heights ----------

        public void BuildTerrain(GameState state)
        {
            Clear();

            for (int y = 0; y < state.Height; y++)
            {
                for (int x = 0; x < state.Width; x++)
                {
                    Color color;
                    switch (state.TerrainAt(x, y))
                    {
                        case TerrainType.Forest: color = VisualAssets.ForestColor; break;
                        case TerrainType.Impassable: color = VisualAssets.ImpassableColor; break;
                        default: color = VisualAssets.OpenColor; break;
                    }
                    _terrain.Add(MakeSprite($"Tile {x},{y}", VisualAssets.Square, color,
                        new Vector3(x, y, TileZ), Vector3.one, 0f, TileOrder));
                }
            }

            BuildContours(state);
            BuildSpotHeights(state);
        }

        /// <summary>
        /// Marching squares over the elevation field: for each half-level
        /// threshold, emit the isoline crossing each cell of tile centres. This
        /// produces the diagonal, organically shaped contours of a topographic
        /// map rather than tile-edge staircases. Contours are drawn heavier
        /// where the relief steps by 2 or more (a cliff, impassable to movement).
        /// </summary>
        private void BuildContours(GameState state)
        {
            int maxElevation = 0;
            for (int y = 0; y < state.Height; y++)
                for (int x = 0; x < state.Width; x++)
                    maxElevation = Mathf.Max(maxElevation, state.ElevationAt(x, y));

            for (int level = 1; level <= maxElevation; level++)
            {
                float threshold = level - 0.5f;
                for (int y = 0; y < state.Height - 1; y++)
                {
                    for (int x = 0; x < state.Width - 1; x++)
                    {
                        float bl = state.ElevationAt(x, y);
                        float br = state.ElevationAt(x + 1, y);
                        float tr = state.ElevationAt(x + 1, y + 1);
                        float tl = state.ElevationAt(x, y + 1);

                        int index = 0;
                        if (bl >= threshold) index |= 1;
                        if (br >= threshold) index |= 2;
                        if (tr >= threshold) index |= 4;
                        if (tl >= threshold) index |= 8;
                        if (index == 0 || index == 15) continue;

                        var bottom = new Vector2(x + Cross(bl, br, threshold), y);
                        var right = new Vector2(x + 1, y + Cross(br, tr, threshold));
                        var top = new Vector2(x + Cross(tl, tr, threshold), y + 1);
                        var left = new Vector2(x, y + Cross(bl, tl, threshold));

                        float span = Mathf.Max(Mathf.Max(bl, br), Mathf.Max(tr, tl))
                                   - Mathf.Min(Mathf.Min(bl, br), Mathf.Min(tr, tl));
                        bool cliff = span >= 2f;

                        switch (index)
                        {
                            case 1: case 14: AddContour(left, bottom, cliff); break;
                            case 2: case 13: AddContour(bottom, right, cliff); break;
                            case 3: case 12: AddContour(left, right, cliff); break;
                            case 4: case 11: AddContour(right, top, cliff); break;
                            case 6: case 9: AddContour(bottom, top, cliff); break;
                            case 7: case 8: AddContour(left, top, cliff); break;
                            case 5: // saddle
                                AddContour(left, bottom, cliff);
                                AddContour(right, top, cliff);
                                break;
                            case 10: // saddle
                                AddContour(left, top, cliff);
                                AddContour(bottom, right, cliff);
                                break;
                        }
                    }
                }
            }
        }

        private static float Cross(float a, float b, float threshold)
        {
            float d = b - a;
            return Mathf.Abs(d) < 1e-6f ? 0.5f : Mathf.Clamp01((threshold - a) / d);
        }

        private void AddContour(Vector2 from, Vector2 to, bool cliff)
        {
            Vector2 delta = to - from;
            float length = delta.magnitude;
            if (length < 1e-4f) return;

            Vector2 mid = (from + to) * 0.5f;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            float thickness = cliff ? 0.13f : 0.055f;

            _terrain.Add(MakeSprite("Contour", VisualAssets.Square, VisualAssets.ContourColor,
                new Vector3(mid.x, mid.y, OverlayZ), new Vector3(length + thickness * 0.5f, thickness, 1f),
                angle, ContourOrder));
        }

        /// <summary>
        /// Spot heights: one label per summit region, the way a topo map marks
        /// hilltops, rather than a digit on every raised tile.
        /// </summary>
        private void BuildSpotHeights(GameState state)
        {
            var visited = new bool[state.Width, state.Height];
            for (int y = 0; y < state.Height; y++)
            {
                for (int x = 0; x < state.Width; x++)
                {
                    if (visited[x, y]) continue;
                    int elevation = state.ElevationAt(x, y);
                    if (elevation <= 0 || !IsSummitTile(state, x, y)) continue;

                    // Flood the connected summit plateau and label its centre.
                    var region = new List<Vector2Int>();
                    var queue = new Queue<Vector2Int>();
                    queue.Enqueue(new Vector2Int(x, y));
                    visited[x, y] = true;
                    while (queue.Count > 0)
                    {
                        var cell = queue.Dequeue();
                        region.Add(cell);
                        for (int dx = -1; dx <= 1; dx++)
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                int nx = cell.x + dx, ny = cell.y + dy;
                                if (!state.IsInBounds(nx, ny) || visited[nx, ny]) continue;
                                if (state.ElevationAt(nx, ny) != elevation || !IsSummitTile(state, nx, ny)) continue;
                                visited[nx, ny] = true;
                                queue.Enqueue(new Vector2Int(nx, ny));
                            }
                    }

                    Vector2 centre = Vector2.zero;
                    foreach (var cell in region) centre += new Vector2(cell.x, cell.y);
                    centre /= region.Count;
                    _terrain.Add(MakeLabel(elevation.ToString(), new Vector3(centre.x, centre.y, OverlayZ),
                        VisualAssets.ElevationDigitColor, 0.075f, ContourOrder));
                }
            }
        }

        private static bool IsSummitTile(GameState state, int x, int y)
        {
            int elevation = state.ElevationAt(x, y);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (!state.IsInBounds(nx, ny)) continue;
                    if (state.ElevationAt(nx, ny) > elevation) return false;
                }
            return true;
        }

        // ---------- units ----------

        public void RenderUnits(GameState state)
        {
            foreach (var go in _unitObjects) Destroy(go);
            _unitObjects.Clear();

            foreach (var unit in state.Units)
            {
                bool exhausted = unit.Owner == state.CurrentPlayer && unit.HasAttacked;
                Color tint = exhausted ? VisualAssets.ExhaustedMul : Color.white;

                var sprite = VisualAssets.UnitSymbol(unit.Type, unit.Owner);
                var go = MakeSprite($"Unit {unit.Id}", sprite, tint,
                    new Vector3((float)unit.X, (float)unit.Y - 0.06f, UnitZ), Vector3.one, 0f, UnitOrder);

                var label = MakeLabel(unit.Hp.ToString(), new Vector3(0.33f, -0.20f, TextZ - UnitZ),
                    Color.white, 0.075f, LabelOrder);
                label.transform.SetParent(go.transform, false);

                _unitObjects.Add(go);
            }
        }

        // ---------- selection overlays ----------

        /// <summary>Draws the star-shaped region the selected unit can dash to.</summary>
        public void SetMoveRegion(Unit unit, double[] reach)
        {
            ClearMoveRegion();
            if (unit == null || reach == null || reach.Length < 3) return;

            var vertices = new Vector3[reach.Length + 1];
            var colors = new Color[vertices.Length];
            var triangles = new int[reach.Length * 3];

            vertices[0] = Vector3.zero;
            colors[0] = VisualAssets.MoveTint;
            for (int i = 0; i < reach.Length; i++)
            {
                float angle = 2f * Mathf.PI * i / reach.Length;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * (float)reach[i], Mathf.Sin(angle) * (float)reach[i], 0f);
                colors[i + 1] = VisualAssets.MoveTintEdge;

                int next = (i + 1) % reach.Length;
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = next + 1;
            }

            var mesh = new Mesh { vertices = vertices, colors = colors, triangles = triangles };
            mesh.RecalculateBounds();

            _moveRegion = new GameObject("MoveRegion");
            _moveRegion.transform.SetParent(transform, false);
            _moveRegion.transform.position = new Vector3((float)unit.X, (float)unit.Y, OverlayZ);
            _moveRegion.AddComponent<MeshFilter>().mesh = mesh;
            var renderer = _moveRegion.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = VisualAssets.ShapeMaterial;
            renderer.sortingOrder = RegionOrder;
        }

        public void SetSelection(Unit selected, IEnumerable<Unit> attackTargets, IEnumerable<Unit> healTargets = null)
        {
            ClearOverlays();
            if (selected != null)
            {
                _overlays.Add(MakeSprite("Selected", VisualAssets.Ring, VisualAssets.SelectedTint,
                    new Vector3((float)selected.X, (float)selected.Y, OverlayZ),
                    Vector3.one * (float)(selected.Stats.Radius * 2.6), 0f, ContourOrder));
            }
            if (attackTargets != null)
            {
                foreach (var target in attackTargets)
                {
                    _overlays.Add(MakeSprite($"Target {target.Id}", VisualAssets.Ring, VisualAssets.AttackTint,
                        new Vector3((float)target.X, (float)target.Y, OverlayZ),
                        Vector3.one * (float)(target.Stats.Radius * 3.0), 0f, ContourOrder));
                }
            }
            if (healTargets != null)
            {
                foreach (var target in healTargets)
                {
                    _overlays.Add(MakeSprite($"Casualty {target.Id}", VisualAssets.Ring, VisualAssets.HealTint,
                        new Vector3((float)target.X, (float)target.Y, OverlayZ),
                        Vector3.one * (float)(target.Stats.Radius * 3.0), 0f, ContourOrder));
                }
            }
        }

        /// <summary>
        /// Draws a unit's capability envelopes (attack / support / sight) as thin
        /// circles. Used by the Field Manual to make the raw numbers legible.
        /// </summary>
        public void SetCapabilityRings(Unit unit)
        {
            if (unit == null) return;
            var stats = unit.Stats;
            var centre = new Vector2((float)unit.X, (float)unit.Y);

            if (stats.Sight > 0)
                DrawCircleOutline(centre, (float)stats.Sight, 0.05f, new Color(1f, 1f, 1f, 0.45f));
            if (stats.CanAttack)
                DrawCircleOutline(centre, (float)stats.AttackRange, 0.07f, new Color(1f, 0.25f, 0.2f, 0.9f));
            if (stats.CanSupport)
                DrawCircleOutline(centre, (float)stats.SupportRange, 0.07f, new Color(0.3f, 0.95f, 0.45f, 0.9f));
        }

        /// <summary>Circle outline built from rotated quads, so thickness stays constant in world units.</summary>
        private void DrawCircleOutline(Vector2 centre, float radius, float thickness, Color color)
        {
            int segments = Mathf.Clamp(Mathf.RoundToInt(radius * 24f), 24, 160);
            float step = 2f * Mathf.PI / segments;
            float chord = 2f * radius * Mathf.Sin(step / 2f);

            for (int i = 0; i < segments; i++)
            {
                float angle = (i + 0.5f) * step;
                var point = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                float rotation = (angle + Mathf.PI / 2f) * Mathf.Rad2Deg;
                _overlays.Add(MakeSprite("RangeRing", VisualAssets.Square, color,
                    new Vector3(point.x, point.y, OverlayZ),
                    new Vector3(chord + thickness, thickness, 1f), rotation, ContourOrder));
            }
        }

        public void ClearHighlights()
        {
            ClearOverlays();
            ClearMoveRegion();
        }

        private void ClearOverlays()
        {
            foreach (var go in _overlays) Destroy(go);
            _overlays.Clear();
        }

        private void ClearMoveRegion()
        {
            if (_moveRegion != null) Destroy(_moveRegion);
            _moveRegion = null;
        }

        public void Clear()
        {
            ClearHighlights();
            foreach (var go in _unitObjects) Destroy(go);
            _unitObjects.Clear();
            foreach (var go in _terrain) Destroy(go);
            _terrain.Clear();
        }

        // ---------- primitives ----------

        private GameObject MakeSprite(string name, Sprite sprite, Color color, Vector3 pos, Vector3 scale, float angle, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            if (Mathf.Abs(angle) > 1e-4f) go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            return go;
        }

        private GameObject MakeLabel(string text, Vector3 localPos, Color color, float size, int sortingOrder)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.font = VisualAssets.UiFont;
            mesh.fontSize = 48;
            mesh.characterSize = size;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = color;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = VisualAssets.UiFont.material;
            renderer.sortingOrder = sortingOrder;
            return go;
        }
    }
}
