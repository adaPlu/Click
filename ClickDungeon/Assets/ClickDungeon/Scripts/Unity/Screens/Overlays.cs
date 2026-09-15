using System;
using System.Collections.Generic;
using ClickDungeon.Domain;
using ClickDungeon.Unity.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace ClickDungeon.Unity.Screens
{
    /// <summary>Modal panel variants; each can have its own panel art (ui_modal_panel_victory / _defeat).</summary>
    public enum ModalStyle { Default, Victory, Defeat }

    /// <summary>Full-screen dimmed modal with a title, body and a column of buttons.</summary>
    public sealed class ModalOverlay
    {
        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        readonly RectTransform _root;
        readonly RectTransform _panel;
        readonly Text _title;
        readonly Text _body;
        readonly RectTransform _buttons;
        readonly Image _panelBack;
        readonly Image _panelBorder;
        readonly MonoBehaviour _host;
        Action _back;

        public ModalOverlay(RectTransform parent, MonoBehaviour host)
        {
            _host = host;
            _root = UiFactory.Rect(parent, "Modal");
            _root.Stretch();
            var dim = UiFactory.Image(_root, "Dim", new Color(0f, 0f, 0f, 0.72f));
            dim.rectTransform.Stretch();
            dim.raycastTarget = true;

            _panel = UiFactory.Rect(_root, "Panel");
            _panel.Place(Center, Center, Vector2.zero, new Vector2(760f, 640f));
            _panel.gameObject.AddComponent<CanvasGroup>();
            _panelBack = UiFactory.Image(_panel, "Back", Palette.Navy, Shapes.Rounded, true);
            _panelBack.rectTransform.Stretch();
            _panelBorder = UiFactory.Image(_panel, "Border", Palette.Gold, Shapes.Frame, true);
            _panelBorder.rectTransform.Stretch();

            _title = UiFactory.Text(_panel, "Title", "", 54, Palette.Gold, TextAnchor.UpperCenter, FontStyle.Bold);
            _title.rectTransform.Stretch(30, 30, 30, 0);
            _title.rectTransform.anchorMin = new Vector2(0f, 1f);
            _title.rectTransform.sizeDelta = new Vector2(-60f, 70f);
            _title.rectTransform.anchoredPosition = new Vector2(0f, -65f);
            UiFactory.Shadow(_title, new Color(0f, 0f, 0f, 0.8f), 3f);

            _body = UiFactory.Text(_panel, "Body", "", 26, Palette.TextLight, TextAnchor.UpperLeft);
            _body.lineSpacing = 1.1f;

            _buttons = UiFactory.Rect(_panel, "Buttons");

            _root.gameObject.SetActive(false);
        }

        public bool IsOpen => _root.gameObject.activeSelf;

        public void Show(string title, string body, Action back, params (string label, Color color, Action action)[] buttons) =>
            Show(ModalStyle.Default, title, body, back, buttons);

        public void Show(ModalStyle style, string title, string body, Action back, params (string label, Color color, Action action)[] buttons)
        {
            ApplyPanelArt(style);
            _back = back;
            _title.text = title;
            _body.text = body;
            foreach (Transform child in _buttons) UnityEngine.Object.Destroy(child.gameObject);

            const float buttonHeight = 78f;
            const float spacing = 14f;
            float buttonsHeight = buttons.Length * buttonHeight + Mathf.Max(0, buttons.Length - 1) * spacing;

            // Measure body text at panel width.
            _body.rectTransform.anchorMin = new Vector2(0f, 1f);
            _body.rectTransform.anchorMax = new Vector2(1f, 1f);
            _body.rectTransform.pivot = new Vector2(0.5f, 1f);
            _body.rectTransform.sizeDelta = new Vector2(-90f, 10f);
            _body.rectTransform.anchoredPosition = new Vector2(0f, -120f);
            var settings = _body.GetGenerationSettings(new Vector2(760f - 90f, 0f));
            float bodyHeight = string.IsNullOrEmpty(body) ? 0f : _body.cachedTextGeneratorForLayout.GetPreferredHeight(body, settings) / _body.pixelsPerUnit;
            _body.rectTransform.sizeDelta = new Vector2(-90f, bodyHeight + 10f);

            float panelHeight = 130f + bodyHeight + (bodyHeight > 0 ? 40f : 0f) + buttonsHeight + 50f;
            _panel.sizeDelta = new Vector2(760f, Mathf.Clamp(panelHeight, 300f, 1040f));

            _buttons.anchorMin = new Vector2(0.5f, 0f);
            _buttons.anchorMax = new Vector2(0.5f, 0f);
            _buttons.pivot = new Vector2(0.5f, 0f);
            _buttons.sizeDelta = new Vector2(520f, buttonsHeight);
            _buttons.anchoredPosition = new Vector2(0f, 46f);

            for (int i = 0; i < buttons.Length; i++)
            {
                var (label, color, action) = buttons[i];
                var parts = UiFactory.Button(_buttons, label, label, color, 32, action);
                UiArt.ApplyPanel(parts.Background, parts.Border, ArtKeys.ModalButton(color));
                parts.Rect.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -i * (buttonHeight + spacing)), new Vector2(520f, buttonHeight));
            }

            _root.SetAsLastSibling();
            _root.gameObject.SetActive(true);
            _host.StartCoroutine(Tween.FadeScale(_panel, _panel.GetComponent<CanvasGroup>(), 0.92f, 0.16f));
        }

        /// <summary>The panel is reused across modals, so a style without art must restore the procedural look.</summary>
        void ApplyPanelArt(ModalStyle style)
        {
            var keys = style == ModalStyle.Default
                ? new[] { ArtKeys.ModalPanel }
                : new[] { ArtKeys.ModalPanelStyle(style.ToString()), ArtKeys.ModalPanel };
            if (UiArt.ApplyPanel(_panelBack, _panelBorder, keys)) return;
            _panelBack.sprite = Shapes.Rounded;
            _panelBack.type = Image.Type.Sliced;
            _panelBack.pixelsPerUnitMultiplier = 2f;
            _panelBack.color = Palette.Navy;
            _panelBorder.enabled = true;
        }

        public void Hide() => _root.gameObject.SetActive(false);

        public void Back()
        {
            if (_back != null) _back();
            else Hide();
        }
    }

    /// <summary>
    /// The tactile chest ritual. The reward was already committed by the Interact command (decision D-005);
    /// taps here cost no turns and can never grant anything.
    /// </summary>
    public sealed class ChestOverlay
    {
        const int RequiredTaps = 3;
        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        readonly MonoBehaviour _host;
        readonly RectTransform _root;
        readonly RectTransform _chest;
        readonly RectTransform _progressFill;
        readonly RectTransform _progressBack;
        readonly Text _prompt;
        readonly RectTransform _rewardCard;
        readonly Text _rewardText;
        readonly RectTransform _sparkles;
        readonly Image _rays;
        readonly RectTransform _reaction;
        readonly Image _reactionImage;
        readonly Image _reactionFrame;
        readonly Image _rewardIcon;
        Coroutine _sequence;
        int _taps;
        bool _burst;
        Action _onClosed;
        RewardRecord _reward;

        public ChestOverlay(RectTransform parent, MonoBehaviour host)
        {
            _host = host;
            _root = UiFactory.Rect(parent, "ChestOverlay");
            _root.Stretch();
            var dim = UiFactory.Image(_root, "Dim", new Color(0f, 0f, 0f, 0.78f));
            dim.rectTransform.Stretch();
            dim.raycastTarget = true;
            var button = dim.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(Tap);

            var glow = UiFactory.Image(_root, "Glow", Palette.Gold.WithAlpha(0.12f), Shapes.Circle);
            glow.rectTransform.Place(Center, Center, new Vector2(0f, 60f), new Vector2(620f, 620f));
            UiArt.Apply(glow, ArtKeys.ChestGlow);

            _rays = UiFactory.Image(_root, "Rays", Color.white, null);
            _rays.rectTransform.Place(Center, Center, new Vector2(0f, 60f), new Vector2(760f, 760f));
            _rays.gameObject.SetActive(false);

            _sparkles = UiFactory.Rect(_root, "Sparkles");
            _sparkles.Place(Center, Center, new Vector2(0f, 60f), new Vector2(10f, 10f));

            _chest = UiFactory.Rect(_root, "Chest");
            _chest.Place(Center, Center, new Vector2(0f, 60f), new Vector2(10f, 10f));

            // Sir Clickington reacts beside the chest (art brief §9); a framed portrait stands in for missing poses.
            _reaction = UiFactory.Rect(_root, "Reaction");
            _reaction.Place(Center, Center, new Vector2(400f, 70f), new Vector2(260f, 260f));
            _reactionImage = UiFactory.Image(_reaction, "Pose", Color.white, null);
            _reactionImage.rectTransform.Stretch();
            _reactionFrame = UiFactory.Image(_reaction, "Frame", Palette.Gold, Shapes.Frame, true);
            _reactionFrame.rectTransform.Stretch(-6, -6, -6, -6);
            _reaction.gameObject.SetActive(false);

            var progressBack = UiFactory.Image(_root, "ProgressBack", Palette.StoneDark, Shapes.Rounded, true);
            UiArt.Apply(progressBack, ArtKeys.ChestProgressBack);
            _progressBack = progressBack.rectTransform;
            _progressBack.Place(Center, Center, new Vector2(0f, -190f), new Vector2(420f, 34f));
            var fill = UiFactory.Image(_progressBack, "Fill", Palette.Gold, Shapes.Rounded, true);
            UiArt.Apply(fill, ArtKeys.ChestProgressFill);
            _progressFill = fill.rectTransform;
            _progressFill.anchorMin = Vector2.zero;
            _progressFill.anchorMax = new Vector2(0f, 1f);
            _progressFill.offsetMin = new Vector2(4f, 4f);
            _progressFill.offsetMax = new Vector2(-4f, -4f);

            _rewardCard = UiFactory.Rect(_root, "RewardCard");
            _rewardCard.Place(Center, Center, new Vector2(0f, -200f), new Vector2(620f, 120f));
            _rewardCard.gameObject.AddComponent<CanvasGroup>();
            var cardBack = UiFactory.Image(_rewardCard, "Back", Palette.Navy, Shapes.Rounded, true);
            cardBack.rectTransform.Stretch();
            var cardBorder = UiFactory.Image(_rewardCard, "Border", Palette.Gold, Shapes.Frame, true);
            cardBorder.rectTransform.Stretch();
            UiArt.ApplyPanel(cardBack, cardBorder, ArtKeys.ChestRewardCard);
            _rewardText = UiFactory.Text(_rewardCard, "Text", "", 52, Palette.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            _rewardText.rectTransform.Stretch();
            UiFactory.Shadow(_rewardText, new Color(0f, 0f, 0f, 0.8f), 3f);
            _rewardIcon = UiFactory.Image(_rewardCard, "Icon", Color.white, null);
            _rewardIcon.rectTransform.Place(new Vector2(0f, 0.5f), Center, new Vector2(70f, 0f), new Vector2(96f, 96f));
            _rewardIcon.gameObject.SetActive(false);

            _prompt = UiFactory.Text(_root, "Prompt", "", 38, Palette.TextLight, TextAnchor.MiddleCenter, FontStyle.Bold);
            _prompt.rectTransform.Place(Center, Center, new Vector2(0f, -320f), new Vector2(900f, 60f));
            UiFactory.Outline(_prompt, new Color(0f, 0f, 0f, 0.8f), 2f);

            _root.gameObject.SetActive(false);
        }

        public bool IsOpen => _root.gameObject.activeSelf;

        public void Open(RewardRecord reward, Action onClosed)
        {
            _reward = reward;
            _onClosed = onClosed;
            _taps = 0;
            _burst = false;
            DrawChest(false);
            _progressBack.gameObject.SetActive(true);
            _progressFill.anchorMax = new Vector2(0f, 1f);
            _rewardCard.gameObject.SetActive(false);
            _rays.gameObject.SetActive(false);
            StopSequence();
            ShowReaction("anticipation");
            _prompt.text = $"TAP TO OPEN  (0/{RequiredTaps})";
            _root.SetAsLastSibling();
            _root.gameObject.SetActive(true);
        }

        public void Tap()
        {
            if (!IsOpen) return;
            if (_burst)
            {
                StopSequence();
                _root.gameObject.SetActive(false);
                var closed = _onClosed;
                _onClosed = null;
                closed?.Invoke();
                return;
            }

            _taps++;
            _progressFill.anchorMax = new Vector2(_taps / (float)RequiredTaps, 1f);
            _host.StartCoroutine(Tween.Punch(_chest, 0.18f, 0.18f));
            _prompt.text = $"TAP TO OPEN  ({_taps}/{RequiredTaps})";
            if (_taps >= RequiredTaps) Burst();
        }

        void Burst()
        {
            _burst = true;
            DrawChest(true);
            _progressBack.gameObject.SetActive(false);
            _rewardText.text = Lines.RewardText(_reward);
            bool hasIcon = TryRewardIcon(_reward, out var rewardIcon);
            _rewardIcon.gameObject.SetActive(hasIcon);
            if (hasIcon) SetSprite(_rewardIcon, rewardIcon);
            _rewardText.rectTransform.Stretch(hasIcon ? 120f : 0f, 0f, 0f, 0f);
            _rewardCard.gameObject.SetActive(true);
            _host.StartCoroutine(Tween.FadeScale(_rewardCard, _rewardCard.GetComponent<CanvasGroup>(), 0.6f, 0.22f));
            _prompt.text = "TAP TO COLLECT";

            if (Art.TryGetSprite(ArtKeys.ChestRays, out var rays))
            {
                SetSprite(_rays, rays);
                _rays.gameObject.SetActive(true);
            }

            if (UserPrefs.ReducedMotion)
            {
                ShowReaction("triumph");
                return;
            }
            ShowReaction("reveal");
            _sequence = _host.StartCoroutine(RewardReactions());
            var rng = new System.Random();
            for (int i = 0; i < 14; i++)
            {
                float angle = i / 14f * Mathf.PI * 2f;
                bool gem = i % 3 == 0;
                var sprite = gem ? Shapes.Diamond : Shapes.Circle;
                var color = i % 4 == 0 ? Palette.Summon : Palette.Gold;
                var spark = Icons.Shape(_sparkles, sprite, color, Vector2.zero, new Vector2(26f, 26f));
                if (Art.TryGetSprite(gem ? ArtKeys.ChestGem : ArtKeys.ChestCoin, out var particle))
                {
                    SetSprite(spark, particle);
                    spark.rectTransform.sizeDelta = new Vector2(40f, 40f);
                }
                var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (220f + (float)rng.NextDouble() * 120f);
                _host.StartCoroutine(Tween.FlyOut(spark.rectTransform, offset, 0.7f));
            }
        }

        System.Collections.IEnumerator RewardReactions()
        {
            yield return new WaitForSecondsRealtime(0.45f);
            if (!IsOpen || !_burst) yield break;
            ShowReaction("heavy");
            yield return new WaitForSecondsRealtime(0.45f);
            if (!IsOpen || !_burst) yield break;
            ShowReaction("triumph");
            _sequence = null;
        }

        void StopSequence()
        {
            if (_sequence == null) return;
            _host.StopCoroutine(_sequence);
            _sequence = null;
        }

        void ShowReaction(string step)
        {
            bool pose = Art.TryGetSprite(ArtKeys.ChestReaction(step), out var sprite);
            bool portrait = !pose && Art.TryGetSprite(ArtKeys.Portrait(ArtKeys.HeroId, ArtKeys.ChestReactionFallback(step)), out sprite);
            _reaction.gameObject.SetActive(pose || portrait);
            if (!pose && !portrait) return;
            SetSprite(_reactionImage, sprite);
            _reactionFrame.enabled = portrait;
        }

        static bool TryRewardIcon(RewardRecord reward, out Sprite icon)
        {
            icon = null;
            if (reward == null) return false;
            return Art.TryGetSprite(ArtKeys.RewardIcon(reward.Kind), out icon)
                   || Art.TryGetSprite(ArtKeys.RewardIconFallback(reward.Kind), out icon);
        }

        static void SetSprite(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.color = Color.white;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
        }

        void DrawChest(bool opened)
        {
            for (int i = _chest.childCount - 1; i >= 0; i--)
            {
                var child = _chest.GetChild(i).gameObject;
                // DestroyImmediate outside play mode keeps edit-mode tests and tools from logging errors.
                if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(child);
                else UnityEngine.Object.DestroyImmediate(child);
            }
            if (!Icons.TryArt(_chest, opened ? ArtKeys.ChestLargeOpen : ArtKeys.ChestLargeClosed, Icons.TileSize * 3.2f))
                Icons.Chest(_chest, opened, 3.2f);
        }
    }
}
