using System;
using UnityEngine;
using UnityEngine.UI;

namespace ClickDungeon.Unity.Ui
{
    /// <summary>Small helpers for building uGUI hierarchies in code.</summary>
    public static class UiFactory
    {
        static Font _font;

        public static Font Font => _font != null ? _font : _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        /// <summary>Destroy at runtime, DestroyImmediate in edit mode (tests and tools), so neither logs errors.</summary>
        public static void SafeDestroy(GameObject go)
        {
            if (go == null) return;
            if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(go);
            else UnityEngine.Object.DestroyImmediate(go);
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent != null ? parent.gameObject.layer : 5;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static RectTransform Stretch(this RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>Anchors at a normalized point of the parent, positioned in reference pixels.</summary>
        public static RectTransform Place(this RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image Image(Transform parent, string name, Color color, Sprite sprite = null, bool sliced = false)
        {
            var rt = Rect(parent, name);
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.type = sliced ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            if (sliced) image.pixelsPerUnitMultiplier = 2f;
            image.raycastTarget = false;
            return image;
        }

        public static Text Text(Transform parent, string name, string value, int size, Color color,
            TextAnchor alignment = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect(parent, name);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = Font;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.fontStyle = style;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        public static Outline Outline(Graphic graphic, Color color, float distance = 2f)
        {
            var outline = graphic.gameObject.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(distance, -distance);
            return outline;
        }

        public static Shadow Shadow(Graphic graphic, Color color, float distance = 3f)
        {
            var shadow = graphic.gameObject.AddComponent<Shadow>();
            shadow.effectColor = color;
            shadow.effectDistance = new Vector2(distance, -distance);
            return shadow;
        }

        public sealed class ButtonParts
        {
            public Button Button;
            public Image Background;
            public Image Border;
            public Text Label;
            public RectTransform Rect;
        }

        /// <summary>Chunky tactile button: rounded fill, gold frame, bold label.</summary>
        public static ButtonParts Button(Transform parent, string name, string label, Color fill, int fontSize, Action onClick)
        {
            var root = Rect(parent, name);
            var background = root.gameObject.AddComponent<Image>();
            background.sprite = Shapes.Rounded;
            background.type = UnityEngine.UI.Image.Type.Sliced;
            background.pixelsPerUnitMultiplier = 2f;
            background.color = fill;

            var border = Image(root, "Border", Palette.GoldDark, Shapes.Frame, true);
            border.rectTransform.Stretch();

            var text = Text(root, "Label", label, fontSize, Palette.TextLight, TextAnchor.MiddleCenter, FontStyle.Bold);
            text.rectTransform.Stretch(8, 4, 8, 4);
            Shadow(text, new Color(0, 0, 0, 0.8f), 2f);

            var button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 1f);
            button.colors = colors;
            if (onClick != null)
            {
                button.onClick.AddListener(() =>
                {
                    // Keep keyboard Submit from re-clicking the last pressed button.
                    if (UnityEngine.EventSystems.EventSystem.current != null)
                        UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
                    onClick();
                });
            }

            return new ButtonParts { Button = button, Background = background, Border = border, Label = text, Rect = root };
        }
    }
}
