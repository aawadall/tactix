using UnityEngine;

namespace Tactix.Game
{
    /// <summary>
    /// Runtime-generated placeholder art: solid sprites and shared colors.
    /// No asset files — everything is procedural.
    /// </summary>
    public static class VisualAssets
    {
        public static readonly Color OpenColor = new Color(0.84f, 0.81f, 0.70f);
        public static readonly Color ForestColor = new Color(0.23f, 0.51f, 0.26f);
        public static readonly Color ImpassableColor = new Color(0.26f, 0.26f, 0.30f);
        public static readonly Color Player0Color = new Color(0.22f, 0.45f, 0.92f);
        public static readonly Color Player1Color = new Color(0.90f, 0.30f, 0.24f);
        public static readonly Color SelectedTint = new Color(1f, 0.92f, 0.25f, 0.55f);
        public static readonly Color MoveTint = new Color(0.25f, 0.85f, 0.95f, 0.45f);
        public static readonly Color AttackTint = new Color(1f, 0.15f, 0.10f, 0.50f);
        public static readonly Color ExhaustedMul = new Color(0.55f, 0.55f, 0.55f);

        private static Sprite _square;
        private static Sprite _circle;

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

        public static Sprite Circle
        {
            get
            {
                if (_circle == null)
                {
                    const int size = 64;
                    var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                    var pixels = new Color32[size * size];
                    float r = size / 2f - 1f;
                    for (int y = 0; y < size; y++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            float dx = x - size / 2f + 0.5f;
                            float dy = y - size / 2f + 0.5f;
                            bool inside = dx * dx + dy * dy <= r * r;
                            pixels[y * size + x] = inside
                                ? new Color32(255, 255, 255, 255)
                                : new Color32(255, 255, 255, 0);
                        }
                    }
                    tex.SetPixels32(pixels);
                    tex.Apply();
                    _circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
                }
                return _circle;
            }
        }

        public static Font UiFont => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }
}
