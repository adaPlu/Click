using System.Collections;
using ClickDungeon.Unity.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace ClickDungeon.Unity.Screens
{
    /// <summary>
    /// Short "FLOOR N / NAME" plate shown when a run starts, resumes or reaches a new floor. It never blocks input and
    /// hides itself; the floor plaque keeps the same information permanently.
    /// </summary>
    public sealed class FloorBanner
    {
        public const float HoldSeconds = 1.1f;
        const float FadeSeconds = 0.2f;
        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        readonly MonoBehaviour _host;
        readonly RectTransform _root;
        readonly CanvasGroup _group;
        readonly Image _back;
        readonly Image _border;
        readonly Text _title;
        readonly Text _name;
        Coroutine _running;

        public FloorBanner(RectTransform parent, MonoBehaviour host, Vector2 position)
        {
            _host = host;
            _root = UiFactory.Rect(parent, "FloorBanner");
            _root.Place(Center, Center, position, new Vector2(760f, 116f));
            _group = _root.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;

            _back = UiFactory.Image(_root, "Back", Palette.Navy.WithAlpha(0.95f), Shapes.Rounded, true);
            _back.rectTransform.Stretch();
            _border = UiFactory.Image(_root, "Border", Palette.Gold, Shapes.Frame, true);
            _border.rectTransform.Stretch();

            _title = UiFactory.Text(_root, "Title", "", 50, Palette.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            _title.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(720f, 62f));
            _title.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiFactory.Shadow(_title, new Color(0f, 0f, 0f, 0.85f), 4f);

            _name = UiFactory.Text(_root, "Name", "", 26, Palette.TextLight, TextAnchor.MiddleCenter, FontStyle.Bold);
            _name.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(720f, 34f));
            _name.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiFactory.Shadow(_name, new Color(0f, 0f, 0f, 0.85f), 2f);

            _root.gameObject.SetActive(false);
        }

        public bool IsShowing => _root.gameObject.activeSelf;

        public void Show(int floorIndex, string floorName, bool boss)
        {
            _title.text = $"FLOOR {floorIndex}";
            _name.text = (floorName ?? "").ToUpperInvariant();
            ApplyArt(boss);

            if (_running != null) _host.StopCoroutine(_running);
            _root.SetAsLastSibling();
            _root.gameObject.SetActive(true);
            _group.alpha = 1f;
            _root.localScale = Vector3.one;
            _running = _host.StartCoroutine(Run());
        }

        public void Hide()
        {
            if (_running != null) _host.StopCoroutine(_running);
            _running = null;
            _root.gameObject.SetActive(false);
        }

        IEnumerator Run()
        {
            // The coroutine runs on the app, which outlives the game screen: stop quietly once the banner is destroyed.
            // Reduced Motion: no fade or scale, just show and hide.
            bool reduced = UserPrefs.ReducedMotion;
            if (!reduced)
            {
                for (float t = 0f; t < FadeSeconds; t += Time.unscaledDeltaTime)
                {
                    if (_root == null) yield break;
                    float k = t / FadeSeconds;
                    _group.alpha = k;
                    _root.localScale = Vector3.one * Mathf.Lerp(1.08f, 1f, k);
                    yield return null;
                }
            }
            if (_root == null) yield break;
            _group.alpha = 1f;
            _root.localScale = Vector3.one;

            yield return new WaitForSecondsRealtime(HoldSeconds);

            if (!reduced)
            {
                for (float t = 0f; t < FadeSeconds; t += Time.unscaledDeltaTime)
                {
                    if (_root == null) yield break;
                    _group.alpha = 1f - t / FadeSeconds;
                    yield return null;
                }
            }
            if (_root == null) yield break;
            _root.gameObject.SetActive(false);
            _running = null;
        }

        /// <summary>The plate is reused, so a floor without art restores the procedural look.</summary>
        void ApplyArt(bool boss)
        {
            var keys = boss ? new[] { ArtKeys.FloorBannerBoss, ArtKeys.FloorBanner } : new[] { ArtKeys.FloorBanner };
            _title.color = boss ? Palette.Danger : Palette.Gold;
            if (UiArt.ApplyPanel(_back, _border, keys)) return;
            _back.sprite = Shapes.Rounded;
            _back.type = Image.Type.Sliced;
            _back.pixelsPerUnitMultiplier = 2f;
            _back.color = Palette.Navy.WithAlpha(0.95f);
            _border.enabled = true;
            _border.color = boss ? Palette.Danger : Palette.Gold;
        }
    }
}
