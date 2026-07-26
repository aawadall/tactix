using System.Collections.Generic;
using Tactix.Core;
using UnityEngine;

namespace Tactix.Game
{
    /// <summary>
    /// Renders terrain tiles, selection/move/attack highlights, and units with HP
    /// labels. Grid coordinates map 1:1 to world XY.
    /// </summary>
    public sealed class BoardRenderer : MonoBehaviour
    {
        private const float TileZ = 0f;
        private const float HighlightZ = -0.1f;
        private const float UnitZ = -0.2f;
        private const float TextZ = -0.3f;

        // Sprite sorting: tiles < contours/elevation digits < highlights < units < labels.
        private const int TileOrder = 0;
        private const int ContourOrder = 1;
        private const int HighlightOrder = 2;
        private const int UnitOrder = 3;
        private const int LabelOrder = 4;

        private readonly List<GameObject> _tiles = new List<GameObject>();
        private readonly List<GameObject> _highlights = new List<GameObject>();
        private readonly List<GameObject> _unitObjects = new List<GameObject>();

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
                    // Full-size tiles: the terrain reads as one continuous map, so
                    // contour lines are the dominant linework.
                    var tile = MakeSprite($"Tile {x},{y}", VisualAssets.Square, color,
                        new Vector3(x, y, TileZ), Vector3.one, TileOrder);
                    _tiles.Add(tile);

                    int elev = state.ElevationAt(x, y);
                    if (elev > 0) _tiles.Add(MakeElevationDigit(x, y, elev));
                }
            }
            BuildGridLines(state);
            BuildContourLines(state);
        }

        /// <summary>A faint reference grid so tile boundaries stay readable.</summary>
        private void BuildGridLines(GameState state)
        {
            var color = new Color(0f, 0f, 0f, 0.14f);
            float cx = (state.Width - 1) / 2f;
            float cy = (state.Height - 1) / 2f;
            for (int x = 0; x <= state.Width; x++)
                _tiles.Add(MakeSprite($"GridV {x}", VisualAssets.Square, color,
                    new Vector3(x - 0.5f, cy, HighlightZ), new Vector3(0.02f, state.Height, 1f), TileOrder));
            for (int y = 0; y <= state.Height; y++)
                _tiles.Add(MakeSprite($"GridH {y}", VisualAssets.Square, color,
                    new Vector3(cx, y - 0.5f, HighlightZ), new Vector3(state.Width, 0.02f, 1f), TileOrder));
        }

        /// <summary>
        /// Topographic contour lines: a segment along every tile edge where the
        /// elevation changes, drawn thicker where the step is 2+ (a cliff).
        /// </summary>
        private void BuildContourLines(GameState state)
        {
            for (int y = 0; y < state.Height; y++)
            {
                for (int x = 0; x < state.Width; x++)
                {
                    int here = state.ElevationAt(x, y);
                    if (x + 1 < state.Width)
                    {
                        int diff = Mathf.Abs(state.ElevationAt(x + 1, y) - here);
                        if (diff > 0) AddContourSegment(x + 0.5f, y, vertical: true, cliff: diff >= 2);
                    }
                    if (y + 1 < state.Height)
                    {
                        int diff = Mathf.Abs(state.ElevationAt(x, y + 1) - here);
                        if (diff > 0) AddContourSegment(x, y + 0.5f, vertical: false, cliff: diff >= 2);
                    }
                }
            }
        }

        private void AddContourSegment(float x, float y, bool vertical, bool cliff)
        {
            float thickness = cliff ? 0.20f : 0.09f;
            var scale = vertical
                ? new Vector3(thickness, 1.02f, 1f)
                : new Vector3(1.02f, thickness, 1f);
            _tiles.Add(MakeSprite($"Contour {x},{y}", VisualAssets.Square, VisualAssets.ContourColor,
                new Vector3(x, y, HighlightZ), scale, ContourOrder));
        }

        private GameObject MakeElevationDigit(int x, int y, int elev)
        {
            var go = new GameObject($"Elev {x},{y}");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(x - 0.30f, y + 0.30f, HighlightZ);
            var text = go.AddComponent<TextMesh>();
            text.text = elev.ToString();
            text.font = VisualAssets.UiFont;
            text.fontSize = 48;
            text.characterSize = 0.052f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = VisualAssets.ElevationDigitColor;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = VisualAssets.UiFont.material;
            renderer.sortingOrder = ContourOrder;
            return go;
        }

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
                    new Vector3(unit.X, unit.Y - 0.06f, UnitZ), Vector3.one, UnitOrder);

                var textGo = new GameObject("Hp");
                textGo.transform.SetParent(go.transform, false);
                textGo.transform.localPosition = new Vector3(0.33f, -0.20f, TextZ - UnitZ);
                var text = textGo.AddComponent<TextMesh>();
                text.text = unit.Hp.ToString();
                text.font = VisualAssets.UiFont;
                text.fontSize = 48;
                text.characterSize = 0.075f;
                text.anchor = TextAnchor.MiddleCenter;
                text.alignment = TextAlignment.Center;
                text.color = Color.white;
                var textRenderer = textGo.GetComponent<MeshRenderer>();
                textRenderer.sharedMaterial = VisualAssets.UiFont.material;
                textRenderer.sortingOrder = LabelOrder;

                _unitObjects.Add(go);
            }
        }

        public void SetHighlights(
            (int x, int y)? selected,
            IEnumerable<(int x, int y)> moveTargets,
            IEnumerable<(int x, int y)> attackTargets)
        {
            ClearHighlights();
            if (selected.HasValue)
                AddHighlight(selected.Value.x, selected.Value.y, VisualAssets.SelectedTint);
            foreach (var (x, y) in moveTargets) AddHighlight(x, y, VisualAssets.MoveTint);
            foreach (var (x, y) in attackTargets) AddHighlight(x, y, VisualAssets.AttackTint);
        }

        public void ClearHighlights()
        {
            foreach (var go in _highlights) Destroy(go);
            _highlights.Clear();
        }

        public void Clear()
        {
            ClearHighlights();
            foreach (var go in _unitObjects) Destroy(go);
            _unitObjects.Clear();
            foreach (var go in _tiles) Destroy(go);
            _tiles.Clear();
        }

        private void AddHighlight(int x, int y, Color color)
        {
            var go = MakeSprite($"Highlight {x},{y}", VisualAssets.Square, color,
                new Vector3(x, y, HighlightZ), new Vector3(0.95f, 0.95f, 1f), HighlightOrder);
            _highlights.Add(go);
        }

        private GameObject MakeSprite(string name, Sprite sprite, Color color, Vector3 pos, Vector3 scale, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            return go;
        }
    }
}
