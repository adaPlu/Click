using System;
using UnityEngine;
using UnityEngine.UI;

namespace ClickDungeon.Unity.Ui
{
    /// <summary>
    /// Placing UI in the reference screens' own pixels (D-033, D-034). The title and gameplay references are 1672 × 941;
    /// the canvas is 1920 × 1080. When a screen's background is the reference itself, anything laid at the reference's
    /// coordinates lands exactly on what the background shows there.
    /// </summary>
    public static class RefLayout
    {
        public const float Scale = 1920f / 1672f;
        static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        static readonly Vector2 Middle = new Vector2(0.5f, 0.5f);

        /// <summary>
        /// True while the screen is taller than it is wide (a phone held upright). Screens are then built on a 1080 × 1920
        /// stage with their portrait layout; the app rebuilds them when the device turns.
        /// </summary>
        public static bool Portrait;
        public const float PortraitWidth = 1080f, PortraitHeight = 1920f;

        /// <summary>
        /// Makes a screen's root the fixed stage its layout is designed on, centred in the window: 16:9 in landscape, 9:16 in
        /// portrait. The canvas grows to fit a wider or taller window, but the background art keeps its own shape; laying
        /// everything out on the stage keeps every button and panel on the background's own. What lies outside the stage is
        /// filled by the backdrop's bleed.
        /// </summary>
        public static void Stage(RectTransform root)
        {
            root.Stretch();
            var fitter = root.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = Portrait ? PortraitWidth / PortraitHeight : 1920f / 1080f;
        }

        /// <summary>
        /// Where one menu or pop-up goes. They are laid out on the 16:9 stage, so in portrait each gets a 16:9 stage of its
        /// own, centred and scaled so its panel (<paramref name="panelWidth"/> wide) fills the screen's width: a small menu
        /// grows to stay readable (up to 1.3×), a wide one shrinks to fit.
        /// </summary>
        public static RectTransform OverlayStage(RectTransform root, float panelWidth)
        {
            if (!Portrait) return root;
            var stage = UiFactory.Rect(root, "OverlayStage");
            stage.Place(Middle, Middle, Vector2.zero, new Vector2(1920f, 1080f));
            stage.localScale = Vector3.one * Mathf.Min(1.3f, (PortraitWidth - 20f) / panelWidth);
            return stage;
        }

        /// <summary>Puts a direct child of the stage at a rectangle in stage units, from the stage's top-left corner.</summary>
        public static RectTransform Top(RectTransform stage, string name, float x, float y, float w, float h, int index = 0)
        {
            int seen = 0;
            foreach (Transform child in stage)
            {
                if (child.name != name || seen++ != index) continue;
                var rt = (RectTransform)child;
                rt.Place(TopLeft, TopLeft, new Vector2(x, -y), new Vector2(w, h));
                return rt;
            }
            return null;
        }

        /// <summary>How far a full-screen layer reaches past the stage, so it still covers a window of any shape.</summary>
        public const float Bleed = 1600f;

        /// <summary>Stretches a full-screen layer (a dimmer, a backdrop) past the stage to cover the whole window.</summary>
        public static void StretchPastStage(RectTransform rt) => rt.Stretch(-Bleed, -Bleed, -Bleed, -Bleed);

        /// <summary>A rectangle given in the reference's pixels, from its parent's top-left corner.</summary>
        public static RectTransform AtRef(Transform parent, string name, float x, float y, float w, float h)
        {
            var rt = UiFactory.Rect(parent, name);
            Place(rt, x, y, w, h);
            return rt;
        }

        public static void Place(RectTransform rt, float x, float y, float w, float h) =>
            rt.Place(TopLeft, TopLeft, new Vector2(x * Scale, -y * Scale), new Vector2(w * Scale, h * Scale));

        /// <summary>A cleaned piece of the reference, laid exactly where it was cut from.</summary>
        public static Image PatchAt(Transform parent, string key, float x, float y, float w, float h)
        {
            var image = UiFactory.Image(AtRef(parent, key, x, y, w, h), "Art", Color.white);
            image.rectTransform.Stretch();
            image.raycastTarget = false;
            UiArt.Apply(image, key);
            return image;
        }

        /// <summary>An invisible button over a button the background already shows.</summary>
        public static Button HotspotAt(Transform parent, string name, float x, float y, float w, float h, Action action)
        {
            var parts = UiFactory.Button(parent, name, "", Color.clear, 10, action);
            Place(parts.Rect, x, y, w, h);
            parts.Background.color = Color.clear;
            parts.Border.enabled = false;
            parts.Label.enabled = false;
            return parts.Button;
        }

        /// <summary>Live text filling a rectangle, shadowed like the reference's lettering.</summary>
        public static Text TextIn(RectTransform rt, string name, int size, Color color, TextAnchor anchor, FontStyle style = FontStyle.Bold)
        {
            var text = UiFactory.Text(rt, name, "", size, color, anchor, style);
            text.rectTransform.Stretch();
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiFactory.Shadow(text, Color.black, 2f);
            return text;
        }
    }
}
