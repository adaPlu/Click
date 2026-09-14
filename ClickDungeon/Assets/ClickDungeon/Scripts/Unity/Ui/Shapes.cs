using System;
using UnityEngine;

namespace ClickDungeon.Unity.Ui
{
    /// <summary>
    /// Procedural programmer-art sprites (Gate 1 needs no production art). Anti-aliased signed-distance shapes.
    /// </summary>
    public static class Shapes
    {
        const int Size = 128;

        static Sprite _rounded, _frame, _circle, _ring, _triangle, _diamond, _square;

        /// <summary>9-sliced rounded panel.</summary>
        public static Sprite Rounded => _rounded != null ? _rounded : _rounded = Build("Rounded", RoundedRect(0.3f), 40);

        /// <summary>9-sliced rounded outline.</summary>
        public static Sprite Frame => _frame != null ? _frame : _frame = Build("Frame", p => Mathf.Abs(RoundedRect(0.3f)(p) + 0.06f) - 0.06f, 40);

        public static Sprite Circle => _circle != null ? _circle : _circle = Build("Circle", p => p.magnitude - 0.95f);
        public static Sprite Ring => _ring != null ? _ring : _ring = Build("Ring", p => Mathf.Abs(p.magnitude - 0.78f) - 0.16f);
        public static Sprite Diamond => _diamond != null ? _diamond : _diamond = Build("Diamond", p => (Mathf.Abs(p.x) + Mathf.Abs(p.y) - 0.95f) * 0.7071f);
        public static Sprite Square => _square != null ? _square : _square = Build("Square", p => Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) - 0.98f);

        public static Sprite Triangle => _triangle != null ? _triangle : _triangle = Build("Triangle", p =>
        {
            // Upward triangle: base at y=-0.8, apex at y=0.9.
            float bottom = -0.8f - p.y;
            float left = (p.y - 0.9f) * 0.5f + (-p.x) * 0.866f - 0.45f;
            float right = (p.y - 0.9f) * 0.5f + p.x * 0.866f - 0.45f;
            return Mathf.Max(bottom, Mathf.Max(left, right));
        });

        static Func<Vector2, float> RoundedRect(float radius) => p =>
        {
            var q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - new Vector2(1f - radius, 1f - radius);
            return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
        };

        static Sprite Build(string name, Func<Vector2, float> sdf, int border = 0)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "Shape_" + name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color32[Size * Size];
            float pixel = 2f / Size;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                var p = new Vector2((x + 0.5f) / Size * 2f - 1f, (y + 0.5f) / Size * 2f - 1f);
                float alpha = Mathf.Clamp01(0.5f - sdf(p) / pixel);
                pixels[y * Size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            sprite.name = name;
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }
    }
}
