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
    /// The INVENTORY screen (D-028): the five slots with what is worn, every item in the game as a tile (found ones with
    /// their art, the rest as unknowns), and what the chosen item does. Tapping a found item wears it; tapping a worn slot
    /// takes it off. It changes only the profile, so it only opens between runs.
    /// </summary>
    public sealed class InventoryOverlay
    {
        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
        const float SlotSize = 132f, TileSize = 112f;

        readonly RectTransform _root;
        readonly RectTransform _slots;
        readonly RectTransform _grid;
        readonly Text _detail;
        ContentCatalog _catalog;
        ProfileState _profile;
        Action _changed;

        public InventoryOverlay(RectTransform parent)
        {
            _root = UiFactory.Rect(parent, "Inventory");
            _root.Stretch();
            var dim = UiFactory.Image(_root, "Dim", new Color(0f, 0f, 0f, 0.72f));
            dim.rectTransform.Stretch();
            dim.raycastTarget = true;

            var panel = UiFactory.Rect(_root, "Panel");
            panel.Place(Center, Center, Vector2.zero, new Vector2(1000f, 860f));
            var back = UiFactory.Image(panel, "Back", Palette.Navy, Shapes.Rounded, true);
            back.rectTransform.Stretch();
            var border = UiFactory.Image(panel, "Border", Palette.Gold, Shapes.Frame, true);
            border.rectTransform.Stretch();
            UiArt.ApplyPanel(back, border, ArtKeys.ModalPanel);

            var title = UiFactory.Text(panel, "Title", "INVENTORY", 54, Palette.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            title.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(900f, 70f));
            UiFactory.Shadow(title, new Color(0f, 0f, 0f, 0.8f), 3f);

            var worn = UiFactory.Text(panel, "WornLabel", "WORN — tap a slot to take it off", 22, Palette.TextDim, TextAnchor.MiddleCenter);
            worn.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -96f), new Vector2(900f, 30f));
            _slots = UiFactory.Rect(panel, "Slots");
            _slots.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -130f), new Vector2(900f, 190f));

            var found = UiFactory.Text(panel, "FoundLabel", "FOUND — tap one to wear it", 22, Palette.TextDim, TextAnchor.MiddleCenter);
            found.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -336f), new Vector2(900f, 30f));
            _grid = UiFactory.Rect(panel, "Grid");
            _grid.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -372f), new Vector2(900f, 280f));

            _detail = UiFactory.Text(panel, "Detail", "", 26, Palette.TextLight, TextAnchor.MiddleCenter);
            _detail.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 136f), new Vector2(900f, 64f));

            var done = UiFactory.Button(panel, "Done", "DONE", Palette.NavyLight, 30, Hide);
            done.Rect.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 48f), new Vector2(320f, 74f));

            _root.gameObject.SetActive(false);
        }

        public bool IsOpen => _root.gameObject.activeSelf;

        public void Open(ContentCatalog catalog, ProfileState profile, Action changed)
        {
            _catalog = catalog;
            _profile = profile;
            _changed = changed;
            _detail.text = "Items come from Lord Blobert, vault great chests and premium chests.\nWhat you wear shapes every run you start.";
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            Redraw();
        }

        public void Hide() => _root.gameObject.SetActive(false);

        void Redraw()
        {
            Clear(_slots);
            Clear(_grid);

            var slots = (ItemSlot[])Enum.GetValues(typeof(ItemSlot));
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                var id = Inventory.Worn(_profile, slot);
                var item = id == null ? null : _catalog.Item(id);
                var pos = new Vector2((i - (slots.Length - 1) * 0.5f) * (SlotSize + 30f), 12f);
                var cell = Tile(_slots, pos, SlotSize, item, true, item != null, () =>
                {
                    if (item == null) return;
                    Inventory.Unequip(_profile, slot);
                    _detail.text = $"Took off the {item.DisplayName}.";
                    Changed();
                });
                var label = UiFactory.Text(_slots, "SlotName", slot.ToString().ToUpperInvariant(), 20, Palette.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
                label.rectTransform.Place(Center, Center, pos + new Vector2(0f, -SlotSize * 0.5f - 18f), new Vector2(SlotSize + 20f, 28f));
            }

            const int columns = 5;
            for (int i = 0; i < _catalog.Items.Count; i++)
            {
                var item = _catalog.Items[i];
                bool owned = Inventory.Owns(_profile, item.Id);
                int row = i / columns, column = i % columns;
                int inRow = Math.Min(columns, _catalog.Items.Count - row * columns);
                var pos = new Vector2((column - (inRow - 1) * 0.5f) * (TileSize + 24f), 64f - row * (TileSize + 24f));
                Tile(_grid, pos, TileSize, owned ? item : null, owned, Inventory.IsWorn(_profile, item.Id), () =>
                {
                    if (!owned)
                    {
                        _detail.text = "Not found yet.";
                        return;
                    }
                    Inventory.Equip(_profile, _catalog, item.Id);
                    _detail.text = $"{item.DisplayName} ({item.Slot}): {item.Effect}.";
                    Changed();
                });
            }
        }

        void Changed()
        {
            _changed?.Invoke();
            Redraw();
        }

        /// <summary>One square: the item's art (or a question mark), gold-framed when worn.</summary>
        static RectTransform Tile(RectTransform parent, Vector2 pos, float size, ItemDefinition item, bool known, bool worn, Action click)
        {
            var parts = UiFactory.Button(parent, item?.Id ?? "Unknown", "", known ? Palette.NavyLight : Palette.StoneDark, 20, click);
            parts.Rect.Place(Center, Center, pos, new Vector2(size, size));
            parts.Border.color = worn ? Palette.Gold : Palette.GoldDark.WithAlpha(0.6f);
            if (item != null)
            {
                if (Icons.TryArtImage(parts.Rect, ArtKeys.ItemIcon(item.Id), size - 18f) == null)
                    parts.Label.text = item.DisplayName.ToUpperInvariant();
                parts.Label.fontSize = 16;
            }
            else if (known)
            {
                parts.Label.text = "EMPTY";
                parts.Label.color = Palette.TextDim;
            }
            else
            {
                parts.Label.text = "?";
                parts.Label.fontSize = 48;
                parts.Label.color = Palette.TextDim;
            }
            return parts.Rect;
        }

        static void Clear(RectTransform rt)
        {
            for (int i = rt.childCount - 1; i >= 0; i--) UiFactory.SafeDestroy(rt.GetChild(i).gameObject);
        }
    }
}
