using System.Collections.Generic;
using Tactix.Core;
using UnityEngine;

namespace Tactix.Game
{
    /// <summary>
    /// Draws the continuous board: terrain raster, topographic contour lines
    /// (marching squares over the elevation field), spot heights on summits,
    /// the selected unit's reachable region, order-path arrows, and units at
    /// their world positions. Grid coordinates map 1:1 to world XY; tile (i,j)
    /// is centred on (i,j).
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
        private const int PathOrder = 3;
        private const int UnitOrder = 4;
        private const int LabelOrder = 5;

        private static readonly Color PathDashColor = new Color(1f, 0.92f, 0.35f, 0.55f);
        private static readonly Color PathEngageColor = new Color(1f, 0.45f, 0.35f, 0.6f);
        private static readonly Color PathSupportColor = new Color(0.4f, 0.95f, 0.5f, 0.6f);
        private static readonly Color PathHoldColor = new Color(0.75f, 0.75f, 0.7f, 0.55f);
        private static readonly Color PathStepColor = new Color(0.35f, 0.95f, 1f, 0.95f);
        private static readonly Color PathAimColor = new Color(1f, 1f, 1f, 0.4f);

        private readonly List<GameObject> _terrain = new List<GameObject>();
        private readonly List<GameObject> _overlays = new List<GameObject>();
        private readonly List<GameObject> _pathOverlays = new List<GameObject>();
        private readonly List<GameObject> _unitObjects = new List<GameObject>();
        private GameObject _moveRegion;
        private GameObject _aimPreview;
        private GameObject _selectionBox;

        // ---------- terrain, contours, spot heights ----------

        public void BuildTerrain(GameState state)
        {
            Clear();

            float cx = (state.Width - 1) * 0.5f;
            float cy = (state.Height - 1) * 0.5f;
            float boardW = state.Width + 0.6f;
            float boardH = state.Height + 0.6f;

            // Parchment base (one sheet — not a WxH tile grid).
            _terrain.Add(MakeSprite("Paper", VisualAssets.Paper, Color.white,
                new Vector3(cx, cy, TileZ),
                new Vector3(boardW, boardH, 1f), 0f, TileOrder));

            BuildElevationWash(state);
            BuildTerrainSymbols(state);
            BuildContours(state);
            BuildSpotHeights(state);
            BuildObjectives(state);
        }

        /// <summary>
        /// Soft sepia wash from elevation — one bilinear texture, not per-tile
        /// terrain-type squares. Contours remain the primary relief language.
        /// </summary>
        private void BuildElevationWash(GameState state)
        {
            int w = state.Width, h = state.Height;
            int maxE = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    maxE = Mathf.Max(maxE, state.ElevationAt(x, y));
            if (maxE <= 0) return;

            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float t = state.ElevationAt(x, y) / (float)maxE;
                    // Ease so low ground stays nearly paper-white.
                    t = t * t;
                    px[y * w + x] = Color.Lerp(VisualAssets.ElevationWashLow, VisualAssets.ElevationWashHigh, t);
                }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            tex.SetPixels(px);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 1f);
            float cx = (w - 1) * 0.5f;
            float cy = (h - 1) * 0.5f;
            _terrain.Add(MakeSprite("ElevationWash", sprite, Color.white,
                new Vector3(cx, cy, TileZ - 0.005f),
                Vector3.one, 0f, TileOrder));
        }

        /// <summary>
        /// Sparse topo marks — forests get ~1/3 of tiles with jittered pines;
        /// rocks get small peak marks on impassable cells. No filled color tiles.
        /// </summary>
        private void BuildTerrainSymbols(GameState state)
        {
            for (int y = 0; y < state.Height; y++)
            {
                for (int x = 0; x < state.Width; x++)
                {
                    var terrain = state.TerrainAt(x, y);
                    if (terrain == TerrainType.Forest)
                    {
                        if (!ShouldPlaceForestMark(x, y)) continue;
                        float jx = ((Hash2(x, y) % 9) - 4) * 0.04f;
                        float jy = ((Hash2(y, x) % 9) - 4) * 0.04f;
                        float scale = 0.38f + (Hash2(x * 3, y * 5) % 5) * 0.03f;
                        _terrain.Add(MakeSprite($"Forest {x},{y}", VisualAssets.ForestSymbol, VisualAssets.ForestInk,
                            new Vector3(x + jx, y + jy, TileZ - 0.01f),
                            Vector3.one * scale, 0f, TileOrder + 1));
                    }
                    else if (terrain == TerrainType.Impassable)
                    {
                        float jx = ((Hash2(x + 11, y) % 7) - 3) * 0.03f;
                        float jy = ((Hash2(y + 7, x) % 7) - 3) * 0.03f;
                        _terrain.Add(MakeSprite($"Rock {x},{y}", VisualAssets.RockSymbol, VisualAssets.RockInk,
                            new Vector3(x + jx, y + jy, TileZ - 0.01f),
                            Vector3.one * 0.32f, 0f, TileOrder + 1));
                    }
                }
            }
        }

        private static bool ShouldPlaceForestMark(int x, int y)
        {
            // ~30% density, biased toward a staggered lattice so stands read as texture.
            int h = Hash2(x, y);
            if ((x + y * 2) % 3 == 0) return (h % 100) < 55;
            return (h % 100) < 18;
        }

        private static int Hash2(int a, int b)
        {
            unchecked
            {
                int n = a * 73856093 ^ b * 19349663;
                n = (n ^ (n >> 13)) * 1274126177;
                return n & 0x7fffffff;
            }
        }

        private void BuildObjectives(GameState state)
        {
            if (state.Objectives == null) return;
            foreach (var objective in state.Objectives)
            {
                _terrain.Add(MakeSprite($"Objective {objective.Id}", VisualAssets.ObjectiveSymbol,
                    VisualAssets.ObjectiveInk,
                    new Vector3((float)objective.X, (float)objective.Y, OverlayZ + 0.02f),
                    Vector3.one * 0.55f, 0f, ContourOrder));
                DrawCircleOutline(
                    new Vector2((float)objective.X, (float)objective.Y),
                    (float)objective.Radius, 0.035f,
                    new Color(VisualAssets.ObjectiveInk.r, VisualAssets.ObjectiveInk.g,
                        VisualAssets.ObjectiveInk.b, 0.28f));
            }
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
            float thickness = cliff ? 0.11f : 0.04f;

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

        public void RenderUnits(GameState state, OrderBook orders = null, bool showAutonomy = false)
        {
            foreach (var go in _unitObjects) Destroy(go);
            _unitObjects.Clear();

            foreach (var unit in state.Units)
            {
                bool exhausted = unit.Owner == state.CurrentPlayer && unit.HasAttacked;
                Color tint = exhausted ? VisualAssets.ExhaustedMul : Color.white;

                var sprite = VisualAssets.UnitSymbol(unit.Type, unit.Owner, unit.Echelon);
                float size = (float)EchelonScale.FootprintMultiplier(unit.Echelon);
                var go = MakeSprite($"Unit {unit.Id}", sprite, tint,
                    new Vector3((float)unit.X, (float)unit.Y - 0.06f * size, UnitZ),
                    Vector3.one * size, 0f, UnitOrder);

                var label = MakeLabel(unit.Hp.ToString(), new Vector3(0.33f, -0.20f, (TextZ - UnitZ) / size),
                    Color.white, 0.075f, LabelOrder);
                label.transform.SetParent(go.transform, false);

                if (showAutonomy && orders != null
                    && unit.Owner == state.CurrentPlayer
                    && !orders.HasOrders(unit.Id))
                {
                    var auto = MakeLabel("AUTO", new Vector3(0f, 0.42f, (TextZ - UnitZ) / size),
                        new Color(0.75f, 0.85f, 1f, 0.85f), 0.055f, LabelOrder);
                    auto.transform.SetParent(go.transform, false);
                }

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
            SetSelection(
                selected != null ? new[] { selected } : null,
                attackTargets,
                healTargets);
        }

        public void SetSelection(IEnumerable<Unit> selected, IEnumerable<Unit> attackTargets, IEnumerable<Unit> healTargets = null)
        {
            ClearOverlays();
            if (selected != null)
            {
                foreach (var unit in selected)
                {
                    if (unit == null) continue;
                    _overlays.Add(MakeSprite($"Selected {unit.Id}", VisualAssets.Ring, VisualAssets.SelectedTint,
                        new Vector3((float)unit.X, (float)unit.Y, OverlayZ),
                        Vector3.one * (float)(unit.Stats.Radius * 2.6), 0f, ContourOrder));
                }
            }
            if (attackTargets != null)
            {
                foreach (var target in attackTargets)
                {
                    if (target == null) continue;
                    _overlays.Add(MakeSprite($"Target {target.Id}", VisualAssets.Ring, VisualAssets.AttackTint,
                        new Vector3((float)target.X, (float)target.Y, OverlayZ),
                        Vector3.one * (float)(target.Stats.Radius * 3.0), 0f, ContourOrder));
                }
            }
            if (healTargets != null)
            {
                foreach (var target in healTargets)
                {
                    if (target == null) continue;
                    _overlays.Add(MakeSprite($"Casualty {target.Id}", VisualAssets.Ring, VisualAssets.HealTint,
                        new Vector3((float)target.X, (float)target.Y, OverlayZ),
                        Vector3.one * (float)(target.Stats.Radius * 3.0), 0f, ContourOrder));
                }
            }
        }

        /// <summary>Screen-space marquee converted to a world-space rectangle outline.</summary>
        public void SetSelectionBox(Vector2 screenA, Vector2 screenB)
        {
            ClearSelectionBox();
            var cam = Camera.main;
            if (cam == null) return;

            Vector3 a = ScreenToBoard(cam, screenA);
            Vector3 b = ScreenToBoard(cam, screenB);
            float minX = Mathf.Min(a.x, b.x), maxX = Mathf.Max(a.x, b.x);
            float minY = Mathf.Min(a.y, b.y), maxY = Mathf.Max(a.y, b.y);
            float cx = (minX + maxX) * 0.5f, cy = (minY + maxY) * 0.5f;
            float w = Mathf.Max(0.05f, maxX - minX), h = Mathf.Max(0.05f, maxY - minY);
            const float t = 0.05f;
            var color = new Color(1f, 0.92f, 0.35f, 0.75f);

            _selectionBox = new GameObject("SelectionBox");
            _selectionBox.transform.SetParent(transform, false);
            MakeBoxEdge(_selectionBox.transform, "Bottom", new Vector3(cx, minY, OverlayZ), new Vector3(w, t, 1f), color);
            MakeBoxEdge(_selectionBox.transform, "Top", new Vector3(cx, maxY, OverlayZ), new Vector3(w, t, 1f), color);
            MakeBoxEdge(_selectionBox.transform, "Left", new Vector3(minX, cy, OverlayZ), new Vector3(t, h, 1f), color);
            MakeBoxEdge(_selectionBox.transform, "Right", new Vector3(maxX, cy, OverlayZ), new Vector3(t, h, 1f), color);
        }

        public void ClearSelectionBox()
        {
            if (_selectionBox == null) return;
            Destroy(_selectionBox);
            _selectionBox = null;
        }

        public void ClearMoveRegionPublic() => ClearMoveRegion();

        private void MakeBoxEdge(Transform parent, string name, Vector3 pos, Vector3 scale, Color color)
        {
            var go = MakeSprite(name, VisualAssets.Square, color, pos, scale, 0f, ContourOrder);
            go.transform.SetParent(parent, true);
        }

        private static Vector3 ScreenToBoard(Camera cam, Vector2 screen)
        {
            var ray = cam.ScreenPointToRay(screen);
            float t = -ray.origin.z / ray.direction.z;
            var p = ray.origin + ray.direction * t;
            return new Vector3(p.x, p.y, 0f);
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

        /// <summary>Rubber-band cursor point while aiming a MoveTo / Hold order.</summary>
        public void SetAimPreview(Vector2? world)
        {
            if (_aimPreview != null)
            {
                Destroy(_aimPreview);
                _aimPreview = null;
            }
            if (!world.HasValue) return;
            _aimPreview = MakeSprite("Aim", VisualAssets.Ring, PathAimColor,
                new Vector3(world.Value.x, world.Value.y, OverlayZ),
                Vector3.one * 0.45f, 0f, PathOrder);
        }

        /// <summary>
        /// Draws curved military-style paths for every queued order, a solid
        /// segment for this turn's projected step, chevrons along the route,
        /// an arrowhead at the final goal, and an ETA label.
        /// </summary>
        public void SetOrderPaths(GameState state, OrderBook book, int? focusUnitId = null, Vector3? aimWorld = null)
        {
            ClearPaths();
            if (state == null || book == null) return;

            foreach (var unitId in book.UnitIds)
            {
                var unit = state.GetUnit(unitId);
                if (unit == null) continue;
                var orders = book.PeekAll(unitId);
                if (orders.Count == 0) continue;

                var waypoints = OrderExecutor.PathWaypoints(state, unitId, orders);
                if (aimWorld.HasValue && focusUnitId == unitId)
                {
                    var aim = (aimWorld.Value.x, aimWorld.Value.y);
                    if (waypoints.Count == 0)
                        waypoints.Add((unit.X, unit.Y));
                    if (waypoints.Count > 0)
                    {
                        var tail = waypoints[waypoints.Count - 1];
                        if (System.Math.Abs(tail.x - aim.Item1) > 1e-6 || System.Math.Abs(tail.y - aim.Item2) > 1e-6)
                            waypoints.Add(aim);
                    }
                }

                if (OrderExecutor.TryNextWaypoint(state, unit, orders[0], out double wx, out double wy)
                    && Rules.ProjectMove(state, unitId, wx, wy, out double sx, out double sy))
                {
                    DrawSegment((float)unit.X, (float)unit.Y, (float)sx, (float)sy, PathStepColor, 0.1f);
                }

                Color routeColor = PathColorForOrder(orders[0]);
                if (waypoints.Count >= 2)
                {
                    var curve = SampleCatmullRom(waypoints, 20);
                    DrawDashedCurve(curve, routeColor, 0.06f);
                    DrawChevronsAlongCurve(curve, routeColor, 0.8f);

                    var end = curve[curve.Count - 1];
                    var prev = curve[curve.Count - 2];
                    float angle = Mathf.Atan2(end.y - prev.y, end.x - prev.x) * Mathf.Rad2Deg;
                    DrawArrowHead(end.x, end.y, angle, routeColor);

                    int turns = OrderExecutor.EstimateTurnsToGoal(state, unitId, waypoints[waypoints.Count - 1].x,
                        waypoints[waypoints.Count - 1].y);
                    if (turns > 0)
                    {
                        _pathOverlays.Add(MakeLabel($"~{turns}t",
                            new Vector3(end.x, end.y + 0.55f, TextZ),
                            routeColor, 0.08f, LabelOrder));
                    }
                }
            }
        }

        private static Color PathColorForOrder(UnitOrder order)
        {
            switch (order)
            {
                case EngageOrder _: return PathEngageColor;
                case SupportOrder _: return PathSupportColor;
                case HoldOrder _: return PathHoldColor;
                default: return PathDashColor;
            }
        }

        private static List<Vector2> SampleCatmullRom(List<(double x, double y)> points, int samplesPerSegment)
        {
            var result = new List<Vector2>();
            if (points.Count == 0) return result;
            if (points.Count == 1)
            {
                result.Add(new Vector2((float)points[0].x, (float)points[0].y));
                return result;
            }

            for (int i = 0; i < points.Count - 1; i++)
            {
                var p0 = i > 0 ? points[i - 1] : points[i];
                var p1 = points[i];
                var p2 = points[i + 1];
                var p3 = i + 2 < points.Count ? points[i + 2] : points[i + 1];

                int start = i == 0 ? 0 : 1;
                for (int s = start; s <= samplesPerSegment; s++)
                {
                    float t = s / (float)samplesPerSegment;
                    result.Add(CatmullRom(
                        (float)p0.x, (float)p0.y,
                        (float)p1.x, (float)p1.y,
                        (float)p2.x, (float)p2.y,
                        (float)p3.x, (float)p3.y, t));
                }
            }
            return result;
        }

        private static Vector2 CatmullRom(float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            float x = 0.5f * ((2f * x1) + (-x0 + x2) * t
                + (2f * x0 - 5f * x1 + 4f * x2 - x3) * t2
                + (-x0 + 3f * x1 - 3f * x2 + x3) * t3);
            float y = 0.5f * ((2f * y1) + (-y0 + y2) * t
                + (2f * y0 - 5f * y1 + 4f * y2 - y3) * t2
                + (-y0 + 3f * y1 - 3f * y2 + y3) * t3);
            return new Vector2(x, y);
        }

        private void DrawDashedCurve(List<Vector2> curve, Color color, float thickness)
        {
            if (curve.Count < 2) return;
            const float dash = 0.35f;
            const float gap = 0.2f;
            float travelled = 0f;
            float dashLeft = dash;

            for (int i = 0; i < curve.Count - 1; i++)
            {
                float x0 = curve[i].x, y0 = curve[i].y;
                float x1 = curve[i + 1].x, y1 = curve[i + 1].y;
                float dx = x1 - x0, dy = y1 - y0;
                float segLen = Mathf.Sqrt(dx * dx + dy * dy);
                if (segLen < 1e-4f) continue;
                float ux = dx / segLen, uy = dy / segLen;
                float pos = 0f;

                while (pos < segLen)
                {
                    if (dashLeft <= 0f)
                    {
                        float skip = Mathf.Min(gap - (-dashLeft), segLen - pos);
                        pos += skip;
                        dashLeft += skip;
                        if (dashLeft >= 0f) { dashLeft = dash; travelled += skip; continue; }
                    }

                    float drawLen = Mathf.Min(dashLeft, segLen - pos);
                    float sx = x0 + ux * pos;
                    float sy = y0 + uy * pos;
                    float ex = x0 + ux * (pos + drawLen);
                    float ey = y0 + uy * (pos + drawLen);
                    DrawSegment(sx, sy, ex, ey, color, thickness);
                    pos += drawLen;
                    dashLeft -= drawLen;
                    travelled += drawLen;
                    if (dashLeft <= 1e-4f) dashLeft = 0f;
                }
            }
        }

        private void DrawChevronsAlongCurve(List<Vector2> curve, Color color, float spacing)
        {
            if (curve.Count < 2) return;
            float distSinceLast = spacing;
            for (int i = 0; i < curve.Count - 1; i++)
            {
                float dx = curve[i + 1].x - curve[i].x;
                float dy = curve[i + 1].y - curve[i].y;
                float segLen = Mathf.Sqrt(dx * dx + dy * dy);
                if (segLen < 1e-4f) continue;
                float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                float pos = 0f;
                while (pos < segLen)
                {
                    float step = spacing - distSinceLast;
                    if (step <= segLen - pos)
                    {
                        pos += step;
                        float t = pos / segLen;
                        float cx = Mathf.Lerp(curve[i].x, curve[i + 1].x, t);
                        float cy = Mathf.Lerp(curve[i].y, curve[i + 1].y, t);
                        DrawArrowHead(cx, cy, angle, color);
                        distSinceLast = 0f;
                    }
                    else
                    {
                        distSinceLast += segLen - pos;
                        break;
                    }
                }
            }
        }

        private void DrawSegment(float x0, float y0, float x1, float y1, Color color, float thickness)
        {
            float dx = x1 - x0, dy = y1 - y0;
            float length = Mathf.Sqrt(dx * dx + dy * dy);
            if (length < 1e-4f) return;
            float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
            var mid = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, OverlayZ);
            _pathOverlays.Add(MakeSprite("PathSeg", VisualAssets.Square, color, mid,
                new Vector3(length + thickness * 0.5f, thickness, 1f), angle, PathOrder));
        }

        private void DrawArrowHead(float x, float y, float angleDeg, Color color)
        {
            // Two short strokes forming a chevron.
            float rad = angleDeg * Mathf.Deg2Rad;
            float back = rad + Mathf.PI;
            float spread = 0.45f;
            float size = 0.35f;
            float x1 = x + Mathf.Cos(back + spread) * size;
            float y1 = y + Mathf.Sin(back + spread) * size;
            float x2 = x + Mathf.Cos(back - spread) * size;
            float y2 = y + Mathf.Sin(back - spread) * size;
            DrawSegment(x, y, x1, y1, color, 0.08f);
            DrawSegment(x, y, x2, y2, color, 0.08f);
        }

        private void ClearPaths()
        {
            foreach (var go in _pathOverlays) Destroy(go);
            _pathOverlays.Clear();
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
            ClearPaths();
            ClearSelectionBox();
            SetAimPreview(null);
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
