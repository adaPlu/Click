using UnityEngine;
using UnityEngine.UI;

namespace ClickDungeon.Unity.Ui
{
    /// <summary>
    /// Applies frame, panel and button art to images the code-built UI already created. When no key has art the
    /// image is left exactly as built, so the procedural placeholder keeps working.
    /// </summary>
    public static class UiArt
    {
        /// <summary>Art is authored at 2× display size (art brief §3), so 9-slice borders draw at half their pixel size.</summary>
        const float SlicedPixelsPerUnitMultiplier = 2f;

        /// <summary>Uses the first key that has art. Returns false and leaves the image untouched when none does.</summary>
        public static bool Apply(Image image, params string[] keys)
        {
            if (image == null || keys == null) return false;
            foreach (var key in keys)
            {
                if (!Art.TryGetSprite(key, out var sprite)) continue;
                image.sprite = sprite;
                image.color = Color.white;
                image.preserveAspect = false;
                bool nineSlice = sprite.border != Vector4.zero;
                image.type = nineSlice ? Image.Type.Sliced : Image.Type.Simple;
                image.pixelsPerUnitMultiplier = nineSlice ? SlicedPixelsPerUnitMultiplier : 1f;
                return true;
            }
            return false;
        }

        /// <summary>Panel art replaces both the procedural fill and its separate border image.</summary>
        public static bool ApplyPanel(Image back, Image border, params string[] keys)
        {
            if (!Apply(back, keys)) return false;
            if (border != null) border.enabled = false;
            return true;
        }
    }
}
