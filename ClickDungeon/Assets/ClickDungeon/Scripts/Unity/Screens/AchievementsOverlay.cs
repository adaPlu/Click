using System;
using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Unity.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace ClickDungeon.Unity.Screens
{
    /// <summary>
    /// The crown's screen (D-030): every achievement with its goal, how far along it is and its gift. Earned ones are gold;
    /// their gifts have already gone to the mail.
    /// </summary>
    public sealed class AchievementsOverlay
    {
        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
        static readonly Vector2 TopCenter = new Vector2(0.5f, 1f);
        const float RowHeight = 56f, RowPitch = 62f, RowWidth = 900f;

        readonly RectTransform _root;
        readonly RectTransform _list;
        readonly Text _count;

        public AchievementsOverlay(RectTransform parent)
        {
            _root = UiFactory.Rect(parent, "Achievements");
            _root.Stretch();
            var dim = UiFactory.Image(_root, "Dim", new Color(0f, 0f, 0f, 0.72f));
            // Past the stage, so the whole window dims, whatever its shape.
            RefLayout.StretchPastStage(dim.rectTransform);
            dim.raycastTarget = true;

            var panel = UiFactory.Rect(_root, "Panel");
            panel.Place(Center, Center, Vector2.zero, new Vector2(1000f, 960f));
            var back = UiFactory.Image(panel, "Back", Palette.Navy, Shapes.Rounded, true);
            back.rectTransform.Stretch();
            var border = UiFactory.Image(panel, "Border", Palette.Gold, Shapes.Frame, true);
            border.rectTransform.Stretch();
            UiArt.ApplyPanel(back, border, ArtKeys.ModalPanel);

            var title = UiFactory.Text(panel, "Title", "ACHIEVEMENTS", 54, Palette.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            title.rectTransform.Place(TopCenter, TopCenter, new Vector2(0f, -20f), new Vector2(900f, 70f));
            UiFactory.Shadow(title, new Color(0f, 0f, 0f, 0.8f), 3f);
            _count = UiFactory.Text(panel, "Count", "", 24, Palette.TextDim, TextAnchor.MiddleCenter);
            _count.rectTransform.Place(TopCenter, TopCenter, new Vector2(0f, -90f), new Vector2(900f, 32f));

            _list = UiFactory.Rect(panel, "List");
            _list.Place(TopCenter, TopCenter, new Vector2(0f, -132f), new Vector2(RowWidth, 700f));

            var done = UiFactory.Button(panel, "Done", "DONE", Palette.NavyLight, 30, Hide);
            done.Rect.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(320f, 74f));

            _root.gameObject.SetActive(false);
        }

        public bool IsOpen => _root.gameObject.activeSelf;

        public void Open(ContentCatalog catalog, ProfileState profile)
        {
            for (int i = _list.childCount - 1; i >= 0; i--) UiFactory.SafeDestroy(_list.GetChild(i).gameObject);
            _count.text = $"{Achievements.EarnedCount(profile, catalog)} of {catalog.Achievements.Count} earned. Each one mails you a gift.";
            for (int i = 0; i < catalog.Achievements.Count; i++)
                Row(catalog.Achievements[i], profile, -i * RowPitch);
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
        }

        public void Hide() => _root.gameObject.SetActive(false);

        void Row(AchievementDefinition a, ProfileState profile, float y)
        {
            bool earned = Achievements.Earned(profile, a.Id);
            int progress = Math.Min(a.Target, Achievements.Progress(profile, a.Stat));

            var row = UiFactory.Rect(_list, a.Id);
            row.Place(TopCenter, TopCenter, new Vector2(0f, y), new Vector2(RowWidth, RowHeight));
            UiFactory.Image(row, "Back", earned ? Palette.GoldDark.WithAlpha(0.35f) : Palette.NavyLight.WithAlpha(0.55f), Shapes.Rounded, true)
                .rectTransform.Stretch();
            UiFactory.Image(row, "Edge", earned ? Palette.Gold : Palette.GoldDark.WithAlpha(0.5f), Shapes.Frame, true).rectTransform.Stretch();

            // The crown for an earned goal, a dim one for a goal still ahead.
            var mark = UiFactory.Rect(row, "Mark");
            mark.Place(new Vector2(0f, 0.5f), Center, new Vector2(34f, 0f), new Vector2(44f, 44f));
            if (Icons.TryArtImage(mark, ArtKeys.CrownButton, 44f) is Image crown)
                crown.color = earned ? Color.white : new Color(0.45f, 0.45f, 0.5f, 0.8f);
            else
                Icons.Crown(mark, Vector2.zero, 0.7f);

            var name = UiFactory.Text(row, "Title", a.Title.ToUpperInvariant(), 24, earned ? Palette.Gold : Palette.TextLight, TextAnchor.MiddleLeft, FontStyle.Bold);
            name.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(68f, 10f), new Vector2(330f, 30f));
            var goal = UiFactory.Text(row, "Goal", a.Description, 19, Palette.TextDim, TextAnchor.MiddleLeft);
            goal.rectTransform.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(68f, -14f), new Vector2(330f, 26f));

            // How far along: a bar and the count, or DONE once earned.
            var bar = UiFactory.Image(row, "Bar", Palette.StoneDark, Shapes.Rounded, true);
            bar.rectTransform.Place(Center, Center, new Vector2(70f, 0f), new Vector2(220f, 18f));
            var fill = UiFactory.Image(bar.rectTransform, "Fill", earned ? Palette.Gold : Palette.PlayGreen, Shapes.Rounded, true);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(a.Target == 0 ? 1f : (float)progress / a.Target, 1f);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            var count = UiFactory.Text(row, "Count", earned ? "DONE" : $"{progress} / {a.Target}", 20, earned ? Palette.Gold : Palette.TextLight, TextAnchor.MiddleLeft, FontStyle.Bold);
            count.rectTransform.Place(Center, Center, new Vector2(240f, 0f), new Vector2(110f, 30f));

            var gift = UiFactory.Text(row, "Gift", a.Reward.Label, 20, earned ? Palette.TextDim : Palette.Parchment, TextAnchor.MiddleRight);
            gift.rectTransform.Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-18f, 0f), new Vector2(150f, 30f));
        }
    }
}
