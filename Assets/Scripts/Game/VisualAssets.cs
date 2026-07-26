using System.Collections.Generic;
using Tactix.Core;
using UnityEngine;

namespace Tactix.Game
{
    /// <summary>
    /// Runtime-generated placeholder art: solid sprites, shared colors, and
    /// NATO APP-6-style unit symbols (rectangular frame, specialization glyph,
    /// echelon mark above). No asset files — everything is procedural.
    /// </summary>
    public static class VisualAssets
    {
        public static readonly Color OpenColor = new Color(0.84f, 0.81f, 0.70f);
        public static readonly Color ForestColor = new Color(0.23f, 0.51f, 0.26f);
        public static readonly Color ImpassableColor = new Color(0.26f, 0.26f, 0.30f);
        public static readonly Color Player0Color = new Color(0.22f, 0.45f, 0.92f);
        public static readonly Color Player1Color = new Color(0.90f, 0.30f, 0.24f);
        public static readonly Color SelectedTint = new Color(1f, 0.92f, 0.25f, 0.95f);
        public static readonly Color MoveTint = new Color(0.25f, 0.85f, 0.95f, 0.38f);
        public static readonly Color MoveTintEdge = new Color(0.25f, 0.85f, 0.95f, 0.16f);
        public static readonly Color AttackTint = new Color(1f, 0.20f, 0.15f, 0.95f);
        public static readonly Color HealTint = new Color(0.30f, 0.95f, 0.45f, 0.95f);
        public static readonly Color ExhaustedMul = new Color(0.55f, 0.55f, 0.55f);
        public static readonly Color ContourColor = new Color(0.36f, 0.24f, 0.11f);
        public static readonly Color ElevationDigitColor = new Color(0.30f, 0.19f, 0.07f, 0.85f);

        /// <summary>The branch name alone, without a size ("Infantry", "Medical").</summary>
        public static string UnitTypeName(UnitType type)
        {
            switch (type)
            {
                case UnitType.Infantry: return "Infantry";
                case UnitType.MechInfantry: return "Mechanized";
                case UnitType.Armor: return "Armor";
                case UnitType.Artillery: return "Artillery";
                case UnitType.Recon: return "Recon";
                case UnitType.Medic: return "Medical";
                case UnitType.Service: return "Service";
                default: return type.ToString();
            }
        }

        /// <summary>Full formation name as shown in the UI, e.g. "Armor Brigade".</summary>
        public static string UnitDisplayName(UnitType type, Echelon echelon = Echelon.Company) =>
            $"{UnitTypeName(type)} {EchelonScale.DisplayName(echelon)}";

        private static Sprite _square;
        private static Sprite _ring;
        private static Material _shapeMaterial;
        private static readonly Dictionary<(UnitType, int, Echelon), Sprite> _symbols =
            new Dictionary<(UnitType, int, Echelon), Sprite>();

        /// <summary>Unlit transparent material for procedural meshes (move region).</summary>
        public static Material ShapeMaterial
        {
            get
            {
                if (_shapeMaterial == null)
                    _shapeMaterial = new Material(Shader.Find("Sprites/Default"));
                return _shapeMaterial;
            }
        }

        /// <summary>Hollow circle used to mark attackable enemies.</summary>
        public static Sprite Ring
        {
            get
            {
                if (_ring == null)
                {
                    const int size = 96;
                    var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                    var px = new Color32[size * size];
                    float outer = size / 2f - 1f;
                    float inner = outer - 6f;
                    for (int y = 0; y < size; y++)
                        for (int x = 0; x < size; x++)
                        {
                            float dx = x - size / 2f + 0.5f, dy = y - size / 2f + 0.5f;
                            float d = Mathf.Sqrt(dx * dx + dy * dy);
                            bool onRing = d <= outer && d >= inner;
                            px[y * size + x] = onRing
                                ? new Color32(255, 255, 255, 255)
                                : new Color32(255, 255, 255, 0);
                        }
                    tex.SetPixels32(px);
                    tex.Apply();
                    _ring = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
                }
                return _ring;
            }
        }

        public static Sprite Square
        {
            get
            {
                if (_square == null)
                {
                    var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                    var pixels = new Color32[16];
                    for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
                    tex.SetPixels32(pixels);
                    tex.Apply();
                    _square = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
                }
                return _square;
            }
        }

        /// <summary>
        /// APP-6-style symbol: player-colored frame fill, white border, white
        /// specialization glyph (infantry: crossed diagonals; artillery: filled
        /// circle), company echelon bar above the frame.
        /// 120x100 px at 150 ppu -> 0.8 x 0.67 world units.
        /// </summary>
        public static Sprite UnitSymbol(UnitType type, int player, Echelon echelon = Echelon.Company)
        {
            if (_symbols.TryGetValue((type, player, echelon), out var cached)) return cached;

            const int w = 120, h = 100;
            var px = new Color32[w * h];
            Color32 white = new Color32(255, 255, 255, 255);
            Color32 fill = player == 0 ? (Color32)Player0Color : (Color32)Player1Color;

            // Frame: outer rect x[6,114) y[6,72), border thickness 5.
            FillRect(px, w, 6, 6, 114, 72, fill);
            FillRect(px, w, 6, 67, 114, 72, white);  // top border
            FillRect(px, w, 6, 6, 114, 11, white);   // bottom border
            FillRect(px, w, 6, 6, 11, 72, white);    // left border
            FillRect(px, w, 109, 6, 114, 72, white); // right border

            switch (type)
            {
                case UnitType.Infantry:
                    // Crossed diagonals corner to corner.
                    DrawThickLine(px, w, h, 11, 11, 108, 66, 3, white);
                    DrawThickLine(px, w, h, 11, 66, 108, 11, 3, white);
                    break;
                case UnitType.MechInfantry:
                    // Infantry cross + track ellipse superimposed.
                    DrawThickLine(px, w, h, 11, 11, 108, 66, 3, white);
                    DrawThickLine(px, w, h, 11, 66, 108, 11, 3, white);
                    DrawEllipseOutline(px, w, h, 60, 39, 30, 15, 3, white);
                    break;
                case UnitType.Armor:
                    // Track ellipse alone.
                    DrawEllipseOutline(px, w, h, 60, 39, 34, 17, 3, white);
                    break;
                case UnitType.Artillery:
                    // Field artillery: filled circle.
                    FillCircle(px, w, h, 60, 39, 13, white);
                    break;
                case UnitType.Recon:
                    // Cavalry/recon slash, lower-left to upper-right.
                    DrawThickLine(px, w, h, 11, 11, 108, 66, 3, white);
                    break;
                case UnitType.Medic:
                    // Medical: upright Geneva cross.
                    FillRect(px, w, 53, 21, 67, 57, white);
                    FillRect(px, w, 42, 32, 78, 46, white);
                    break;
                case UnitType.Service:
                    // Maintenance: open-ended wrench.
                    DrawThickLine(px, w, h, 42, 26, 78, 52, 4, white);
                    FillCircle(px, w, h, 38, 23, 11, white);
                    FillCircle(px, w, h, 38, 23, 5, fill);   // jaw opening
                    FillRect(px, w, 30, 15, 44, 24, fill);   // open the jaw downward
                    FillCircle(px, w, h, 82, 55, 10, white);
                    break;
            }

            DrawEchelonMarking(px, w, h, echelon, white, fill);

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 150f);
            _symbols[(type, player, echelon)] = sprite;
            return sprite;
        }

        /// <summary>
        /// The NATO echelon marking above the frame: a ring for a fire team, one
        /// to three dots up to platoon, one to three bars up to regiment, then one
        /// to six crosses from brigade to theatre.
        /// </summary>
        private static void DrawEchelonMarking(Color32[] px, int w, int h, Echelon echelon, Color32 ink, Color32 fill)
        {
            var (mark, count) = EchelonScale.Marking(echelon);
            const int centreY = 86;

            int glyphWidth, gap;
            switch (mark)
            {
                case EchelonMark.Ring: glyphWidth = 18; gap = 0; break;
                case EchelonMark.Dot: glyphWidth = 12; gap = 6; break;
                case EchelonMark.Bar: glyphWidth = 6; gap = 9; break;
                default: glyphWidth = 14; gap = 3; break; // Cross
            }

            int totalWidth = count * glyphWidth + (count - 1) * gap;
            int left = (w - totalWidth) / 2;

            for (int i = 0; i < count; i++)
            {
                int centreX = left + i * (glyphWidth + gap) + glyphWidth / 2;
                switch (mark)
                {
                    case EchelonMark.Ring:
                        FillCircle(px, w, h, centreX, centreY, 9, ink);
                        FillCircle(px, w, h, centreX, centreY, 5, fill);
                        break;
                    case EchelonMark.Dot:
                        FillCircle(px, w, h, centreX, centreY, 6, ink);
                        break;
                    case EchelonMark.Bar:
                        FillRect(px, w, centreX - 3, centreY - 10, centreX + 3, centreY + 10, ink);
                        break;
                    case EchelonMark.Cross:
                        DrawThickLine(px, w, h, centreX - 6, centreY - 8, centreX + 6, centreY + 8, 2, ink);
                        DrawThickLine(px, w, h, centreX - 6, centreY + 8, centreX + 6, centreY - 8, 2, ink);
                        break;
                }
            }
        }

        public static Font UiFont => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // ---------- pixel drawing helpers (bottom-left origin, [x0,x1) ranges) ----------

        private static void FillRect(Color32[] px, int texW, int x0, int y0, int x1, int y1, Color32 c)
        {
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                    px[y * texW + x] = c;
        }

        private static void FillCircle(Color32[] px, int texW, int texH, int cx, int cy, int r, Color32 c)
        {
            for (int y = Mathf.Max(0, cy - r); y <= Mathf.Min(texH - 1, cy + r); y++)
                for (int x = Mathf.Max(0, cx - r); x <= Mathf.Min(texW - 1, cx + r); x++)
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r)
                        px[y * texW + x] = c;
        }

        private static void DrawEllipseOutline(Color32[] px, int texW, int texH, int cx, int cy, int rx, int ry, int radius, Color32 c)
        {
            const int steps = 160;
            for (int i = 0; i < steps; i++)
            {
                float a = i * 2f * Mathf.PI / steps;
                int x = Mathf.RoundToInt(cx + rx * Mathf.Cos(a));
                int y = Mathf.RoundToInt(cy + ry * Mathf.Sin(a));
                FillCircle(px, texW, texH, x, y, radius, c);
            }
        }

        private static void DrawThickLine(Color32[] px, int texW, int texH, int x0, int y0, int x1, int y1, int radius, Color32 c)
        {
            int steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
            for (int i = 0; i <= steps; i++)
            {
                float t = steps == 0 ? 0f : (float)i / steps;
                int x = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t));
                int y = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t));
                FillCircle(px, texW, texH, x, y, radius, c);
            }
        }
    }
}
