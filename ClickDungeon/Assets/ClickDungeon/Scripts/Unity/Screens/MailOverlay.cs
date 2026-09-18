using System;
using System.Collections.Generic;
using ClickDungeon.Application;
using ClickDungeon.Domain;
using ClickDungeon.Unity.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace ClickDungeon.Unity.Screens
{
    /// <summary>
    /// The mail (D-030): letters newest first on the left, the chosen one on the right with its gift and COLLECT. Opening a
    /// letter marks it read; every change is handed back to be saved.
    /// </summary>
    public sealed class MailOverlay
    {
        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
        static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        const int PageSize = 8;
        const float LetterPitch = 70f;

        readonly RectTransform _root;
        readonly RectTransform _list;
        readonly Text _page;
        readonly UiFactory.ButtonParts _prev, _next, _collect, _collectAll;
        readonly Text _from, _subject, _body, _gift;
        ProfileState _profile;
        Action _changed;
        int _selected;
        int _pageIndex;

        public MailOverlay(RectTransform parent)
        {
            _root = UiFactory.Rect(parent, "Mail");
            _root.Stretch();
            var dim = UiFactory.Image(_root, "Dim", new Color(0f, 0f, 0f, 0.72f));
            // Past the stage, so the whole window dims, whatever its shape.
            RefLayout.StretchPastStage(dim.rectTransform);
            dim.raycastTarget = true;

            var panel = UiFactory.Rect(_root, "Panel");
            panel.Place(Center, Center, Vector2.zero, new Vector2(1240f, 900f));
            var back = UiFactory.Image(panel, "Back", Palette.Navy, Shapes.Rounded, true);
            back.rectTransform.Stretch();
            var border = UiFactory.Image(panel, "Border", Palette.Gold, Shapes.Frame, true);
            border.rectTransform.Stretch();
            UiArt.ApplyPanel(back, border, ArtKeys.ModalPanel);

            var title = UiFactory.Text(panel, "Title", "MAIL", 54, Palette.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            title.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(900f, 70f));
            UiFactory.Shadow(title, new Color(0f, 0f, 0f, 0.8f), 3f);

            _list = UiFactory.Rect(panel, "Letters");
            _list.Place(TopLeft, TopLeft, new Vector2(40f, -110f), new Vector2(460f, PageSize * LetterPitch));
            _prev = UiFactory.Button(panel, "Newer", "NEWER", Palette.NavyLight, 22, () => Page(-1));
            _prev.Rect.Place(TopLeft, TopLeft, new Vector2(40f, -110f - PageSize * LetterPitch - 8f), new Vector2(140f, 50f));
            _page = UiFactory.Text(panel, "Page", "", 20, Palette.TextDim, TextAnchor.MiddleCenter);
            _page.rectTransform.Place(TopLeft, TopLeft, new Vector2(180f, -110f - PageSize * LetterPitch - 8f), new Vector2(180f, 50f));
            _next = UiFactory.Button(panel, "Older", "OLDER", Palette.NavyLight, 22, () => Page(1));
            _next.Rect.Place(TopLeft, TopLeft, new Vector2(360f, -110f - PageSize * LetterPitch - 8f), new Vector2(140f, 50f));

            // The open letter.
            var paper = UiFactory.Rect(panel, "Letter");
            paper.Place(TopLeft, TopLeft, new Vector2(530f, -110f), new Vector2(670f, 610f));
            UiFactory.Image(paper, "Back", Palette.Parchment, Shapes.Rounded, true).rectTransform.Stretch();
            UiFactory.Image(paper, "Edge", Palette.GoldDark, Shapes.Frame, true).rectTransform.Stretch();
            _subject = UiFactory.Text(paper, "Subject", "", 32, Palette.Ink, TextAnchor.UpperLeft, FontStyle.Bold);
            _subject.rectTransform.Stretch(30, 24, 30, 540);
            _from = UiFactory.Text(paper, "From", "", 22, Palette.Ink.WithAlpha(0.7f), TextAnchor.UpperLeft, FontStyle.Italic);
            _from.rectTransform.Stretch(30, 74, 30, 500);
            _body = UiFactory.Text(paper, "Body", "", 25, Palette.Ink, TextAnchor.UpperLeft);
            _body.rectTransform.Stretch(30, 124, 30, 110);
            _body.lineSpacing = 1.15f;
            _gift = UiFactory.Text(paper, "Gift", "", 26, Palette.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            _gift.rectTransform.Place(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(30f, 40f), new Vector2(360f, 56f));
            _collect = UiFactory.Button(paper, "Collect", "COLLECT", Palette.PlayGreen, 28, CollectSelected);
            _collect.Rect.Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 30f), new Vector2(230f, 66f));

            _collectAll = UiFactory.Button(panel, "CollectAll", "COLLECT ALL", Palette.PlayGreen, 26, CollectAll);
            _collectAll.Rect.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-180f, 36f), new Vector2(320f, 74f));
            var done = UiFactory.Button(panel, "Done", "DONE", Palette.NavyLight, 30, Hide);
            done.Rect.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(180f, 36f), new Vector2(320f, 74f));

            _root.gameObject.SetActive(false);
        }

        public bool IsOpen => _root.gameObject.activeSelf;

        public void Open(ProfileState profile, Action changed)
        {
            _profile = profile;
            _changed = changed;
            _pageIndex = 0;
            var newest = Newest();
            _selected = newest.Count > 0 ? newest[0].Id : 0;
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            Select(_selected);
        }

        public void Hide() => _root.gameObject.SetActive(false);

        List<MailMessage> Newest()
        {
            var letters = new List<MailMessage>(_profile.Mail ?? new List<MailMessage>());
            letters.Reverse();
            return letters;
        }

        void Page(int step)
        {
            int pages = Math.Max(1, (Newest().Count + PageSize - 1) / PageSize);
            _pageIndex = Math.Max(0, Math.Min(pages - 1, _pageIndex + step));
            Redraw();
        }

        /// <summary>Opening a letter reads it.</summary>
        void Select(int id)
        {
            _selected = id;
            var message = Mailbox.Find(_profile, id);
            if (message != null && !message.Read)
            {
                Mailbox.Open(_profile, id);
                _changed?.Invoke();
            }
            Redraw();
        }

        void CollectSelected()
        {
            if (Mailbox.Collect(_profile, _selected)) _changed?.Invoke();
            Redraw();
        }

        void CollectAll()
        {
            if (Mailbox.CollectAll(_profile) > 0) _changed?.Invoke();
            Redraw();
        }

        void Redraw()
        {
            for (int i = _list.childCount - 1; i >= 0; i--) UiFactory.SafeDestroy(_list.GetChild(i).gameObject);
            var letters = Newest();
            int pages = Math.Max(1, (letters.Count + PageSize - 1) / PageSize);
            for (int i = 0; i < PageSize; i++)
            {
                int index = _pageIndex * PageSize + i;
                if (index >= letters.Count) break;
                Letter(letters[index], -i * LetterPitch);
            }
            _page.text = letters.Count == 0 ? "" : $"{_pageIndex + 1} / {pages}";
            _prev.Rect.gameObject.SetActive(_pageIndex > 0);
            _next.Rect.gameObject.SetActive(_pageIndex < pages - 1);

            bool anyWaiting = letters.Exists(Mailbox.Waiting);
            _collectAll.Button.interactable = anyWaiting;
            _collectAll.Background.color = anyWaiting ? Palette.PlayGreen : Palette.StoneDark;

            var message = Mailbox.Find(_profile, _selected);
            if (message == null)
            {
                _subject.text = "No letters";
                _from.text = "";
                _body.text = "Letters arrive when you level up and when you earn an achievement.";
                _gift.text = "";
                _collect.Rect.gameObject.SetActive(false);
                return;
            }
            _subject.text = message.Subject;
            _from.text = "From " + message.From;
            _body.text = message.Body;
            _gift.text = message.HasGift ? "Gift: " + (message.GiftLabel ?? "a gift") : "";
            _collect.Rect.gameObject.SetActive(message.HasGift);
            bool waiting = Mailbox.Waiting(message);
            _collect.Button.interactable = waiting;
            _collect.Label.text = waiting ? "COLLECT" : "COLLECTED";
            _collect.Background.color = waiting ? Palette.PlayGreen : Palette.StoneDark;
        }

        /// <summary>One line in the list: subject and sender, bold with a red dot while it wants attention.</summary>
        void Letter(MailMessage message, float y)
        {
            bool wants = !message.Read || Mailbox.Waiting(message);
            bool chosen = message.Id == _selected;
            int id = message.Id;
            var parts = UiFactory.Button(_list, "Letter " + id, "", chosen ? Palette.NavyLight : Palette.Navy.Dim(0.8f), 20, () => Select(id));
            parts.Rect.Place(TopLeft, TopLeft, new Vector2(0f, y), new Vector2(460f, LetterPitch - 8f));
            parts.Border.color = chosen ? Palette.Gold : Palette.GoldDark.WithAlpha(0.5f);

            var subject = UiFactory.Text(parts.Rect, "Subject", message.Subject, 22, wants ? Palette.TextLight : Palette.TextDim, TextAnchor.MiddleLeft,
                wants ? FontStyle.Bold : FontStyle.Normal);
            subject.rectTransform.Stretch(46, 4, 16, 26);
            var from = UiFactory.Text(parts.Rect, "From", message.From, 17, Palette.TextDim, TextAnchor.MiddleLeft);
            from.rectTransform.Stretch(46, 34, 16, 2);

            var icon = UiFactory.Rect(parts.Rect, "Icon");
            icon.Place(new Vector2(0f, 0.5f), Center, new Vector2(24f, 0f), new Vector2(30f, 30f));
            if (Mailbox.Waiting(message)) Icons.Shape(icon, Shapes.Rounded, Palette.Gold, Vector2.zero, new Vector2(26f, 22f));
            else Icons.Shape(icon, Shapes.Rounded, Palette.Parchment.WithAlpha(wants ? 1f : 0.4f), Vector2.zero, new Vector2(26f, 18f));
            if (wants && Icons.TryArtImage(parts.Rect, ArtKeys.AlertBadge, 26f, new Vector2(214f, 16f)) == null)
                Icons.Shape(parts.Rect, Shapes.Circle, Palette.Danger, new Vector2(214f, 16f), new Vector2(18f, 18f));
        }
    }
}
