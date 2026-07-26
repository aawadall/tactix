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
                    // Topographic shading: higher ground renders brighter.
                    color *= 0.65f + 0.13f * state.ElevationAt(x, y);
                    color.a = 1f;
                    var tile = MakeSprite($"Tile {x},{y}", VisualAssets.Square, color,
                        new Vector3(x, y, TileZ), new Vector3(0.95f, 0.95f, 1f), sortingOrder: 0);
                    _tiles.Add(tile);
                }
            }
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
                    new Vector3(unit.X, unit.Y - 0.06f, UnitZ), Vector3.one, sortingOrder: 2);

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
                textRenderer.sortingOrder = 3;

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
                new Vector3(x, y, HighlightZ), new Vector3(0.95f, 0.95f, 1f), sortingOrder: 1);
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
