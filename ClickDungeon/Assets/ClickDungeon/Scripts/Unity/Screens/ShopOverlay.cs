using System;
using System.Collections.Generic;
using System.Globalization;
using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Unity.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace ClickDungeon.Unity.Screens
{
    /// <summary>
    /// The SHOP screen (D-036): tabs of cards, each with the item's picture, name, what it does and a price button.
    /// BOOSTS outfit the next run, GEAR is the day's stock by rarity, CHESTS hold a random piece of gear, EXCHANGE trades
    /// coins and gems. Every purchase changes only the profile and is handed back to be saved at once.
    /// </summary>
    public sealed class ShopOverlay
    {
        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
        static readonly Vector2 TopCenter = new Vector2(0.5f, 1f);
        const float CardWidth = 290f, CardHeight = 300f, CardPitchX = 312f, CardPitchY = 318f;

        static readonly string[] TabNames = { "BOOSTS", "GEAR", "CHESTS", "EXCHANGE" };
        static readonly string[] TabNotes =
        {
            "Boosts are spent into your next run's starting numbers.",
            "Today's gear. Rarer pieces cost more; a new stock arrives tomorrow.",
            "A chest holds one random piece of gear. One you already own becomes coins.",
            "Trade coins and gems. A round trip loses half, so trade only what you need.",
        };

        readonly RectTransform _root;
        readonly RectTransform _cards;
        readonly Text _coins, _gems, _note, _status;
        readonly UiFactory.ButtonParts[] _tabs = new UiFactory.ButtonParts[TabNames.Length];
        ContentCatalog _catalog;
        ProfileState _profile;
        Func<DateTime> _today;
        Action _changed;
        string _extraNote;
        ShopTab _tab;

        public ShopOverlay(RectTransform parent)
        {
            _root = UiFactory.Rect(parent, "Shop");
            _root.Stretch();
            var dim = UiFactory.Image(_root, "Dim", new Color(0f, 0f, 0f, 0.72f));
            RefLayout.StretchPastStage(dim.rectTransform);
            dim.raycastTarget = true;

            var panel = UiFactory.Rect(_root, "Panel");
            panel.Place(Center, Center, Vector2.zero, new Vector2(1340f, 1000f));
            var back = UiFactory.Image(panel, "Back", Palette.Navy, Shapes.Rounded, true);
            back.rectTransform.Stretch();
            var border = UiFactory.Image(panel, "Border", Palette.Gold, Shapes.Frame, true);
            border.rectTransform.Stretch();
            UiArt.ApplyPanel(back, border, ArtKeys.ModalPanel);

            var title = UiFactory.Text(panel, "Title", "SHOP", 56, Palette.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            title.rectTransform.Place(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(48f, -18f), new Vector2(400f, 72f));
            UiFactory.Shadow(title, new Color(0f, 0f, 0f, 0.8f), 3f);

            _coins = PurseCount(panel, new Vector2(-300f, -30f), ArtKeys.CoinIcon, Palette.Gold);
            _gems = PurseCount(panel, new Vector2(-110f, -30f), ArtKeys.GemIcon, Palette.Summon);

            for (int i = 0; i < TabNames.Length; i++)
            {
                var tab = (ShopTab)i;
                var parts = UiFactory.Button(panel, "Tab " + TabNames[i], TabNames[i], Palette.NavyLight, 28, () => Show(tab));
                parts.Rect.Place(TopCenter, TopCenter, new Vector2((i - 1.5f) * 300f, -104f), new Vector2(284f, 62f));
                _tabs[i] = parts;
            }

            _note = UiFactory.Text(panel, "Note", "", 22, Palette.TextDim, TextAnchor.MiddleCenter);
            _note.rectTransform.Place(TopCenter, TopCenter, new Vector2(0f, -176f), new Vector2(1240f, 32f));

            _cards = UiFactory.Rect(panel, "Cards");
            _cards.Place(TopCenter, TopCenter, new Vector2(0f, -214f), new Vector2(1240f, 640f));

            _status = UiFactory.Text(panel, "Status", "", 24, Palette.TextLight, TextAnchor.MiddleLeft);
            _status.rectTransform.Place(new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(48f, 30f), new Vector2(940f, 80f));
            _status.resizeTextForBestFit = true;
            _status.resizeTextMinSize = 16;
            _status.resizeTextMaxSize = 24;

            var done = UiFactory.Button(panel, "Done", "DONE", Palette.NavyLight, 30, Hide);
            done.Rect.Place(new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 34f), new Vector2(260f, 72f));

            _root.gameObject.SetActive(false);
        }

        public bool IsOpen => _root.gameObject.activeSelf;

        /// <summary>
        /// Opens on a tab. <paramref name="today"/> sets the gear stock (the device's date in play, a fixed one in tests);
        /// <paramref name="note"/> is added to every tab's line, for the game screen's "outfits your next run".
        /// </summary>
        public void Open(ContentCatalog catalog, ProfileState profile, Func<DateTime> today, Action changed, ShopTab tab = ShopTab.Boosts,
            string note = null)
        {
            _catalog = catalog;
            _profile = profile;
            _today = today ?? (() => DateTime.Now);
            _changed = changed;
            _extraNote = note;
            _status.text = "Everything here waits for your next run, or joins your inventory.";
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            Show(tab);
        }

        public void Hide() => _root.gameObject.SetActive(false);

        public void Show(ShopTab tab)
        {
            _tab = tab;
            Redraw();
        }

        // ------------------------------------------------------------------ drawing

        void Redraw()
        {
            for (int i = _cards.childCount - 1; i >= 0; i--) UiFactory.SafeDestroy(_cards.GetChild(i).gameObject);
            for (int i = 0; i < _tabs.Length; i++)
            {
                bool on = (int)_tab == i;
                _tabs[i].Background.color = on ? Palette.GoldDark : Palette.NavyLight;
                _tabs[i].Border.color = on ? Palette.Gold : Palette.GoldDark.WithAlpha(0.6f);
                _tabs[i].Label.color = on ? Color.white : Palette.TextDim;
            }
            _note.text = TabNotes[(int)_tab] + (string.IsNullOrEmpty(_extraNote) ? "" : "  " + _extraNote);
            _coins.text = _profile.Coins.ToString("N0", CultureInfo.InvariantCulture);
            _gems.text = _profile.Gems.ToString("N0", CultureInfo.InvariantCulture);

            switch (_tab)
            {
                case ShopTab.Gear:
                    var stock = Shop.GearStock(_catalog, _today());
                    for (int i = 0; i < stock.Count; i++) GearCard(stock[i], Slot(i, stock.Count));
                    break;
                case ShopTab.Chests:
                    for (int i = 0; i < Shop.Chests.Length; i++) ItemCard(Shop.Chests[i], Slot(i, Shop.Chests.Length));
                    break;
                case ShopTab.Exchange:
                    for (int i = 0; i < Shop.Exchanges.Length; i++) ItemCard(Shop.Exchanges[i], Slot(i, Shop.Exchanges.Length));
                    break;
                default:
                    for (int i = 0; i < Shop.Stock.Length; i++) ItemCard(Shop.Stock[i], Slot(i, Shop.Stock.Length));
                    break;
            }
        }

        /// <summary>Cards four to a row, each row centred.</summary>
        static Vector2 Slot(int index, int count)
        {
            const int columns = 4;
            int row = index / columns, column = index % columns;
            int inRow = Math.Min(columns, count - row * columns);
            return new Vector2((column - (inRow - 1) * 0.5f) * CardPitchX, -row * CardPitchY);
        }

        void ItemCard(ShopItem item, Vector2 pos)
        {
            var card = Card("Card " + item, pos, null);
            Picture(card, ArtKeys.ShopIcon(item), null);
            Label(card, Shop.DisplayName(item), Palette.Gold);
            Body(card, Shop.Describe(item, _catalog));
            int waiting = Shop.Waiting(_profile, item);
            if (waiting > 0) Badge(card, $"x{waiting}");
            Price(card, item.ToString(), Shop.Price(item), Shop.PricedInGems(item), Shop.CanAfford(_profile, item), null, () => Buy(item));
        }

        void GearCard(ItemDefinition item, Vector2 pos)
        {
            bool owned = Inventory.Owns(_profile, item.Id);
            var card = Card("Gear " + item.Id, pos, item.Rarity);
            Picture(card, ArtKeys.ItemIcon(item.Id), item.Rarity);
            Label(card, item.DisplayName.ToUpperInvariant(), RarityColor(item.Rarity));
            Body(card, $"{item.Rarity} {item.Slot.ToString().ToLowerInvariant()}. {item.Effect}.");
            bool afford = (Shop.GearPricedInGems(item) ? _profile.Gems : _profile.Coins) >= Shop.GearPrice(item);
            Price(card, item.Id, Shop.GearPrice(item), Shop.GearPricedInGems(item), afford && !owned, owned ? "OWNED" : null,
                () => BuyGear(item));
        }

        RectTransform Card(string name, Vector2 pos, ItemRarity? rarity)
        {
            var card = UiFactory.Rect(_cards, name);
            card.Place(TopCenter, TopCenter, pos, new Vector2(CardWidth, CardHeight));
            UiFactory.Image(card, "Back", Palette.NavyLight.WithAlpha(0.55f), Shapes.Rounded, true).rectTransform.Stretch();
            UiFactory.Image(card, "Edge", rarity.HasValue ? RarityColor(rarity.Value) : Palette.GoldDark, Shapes.Frame, true).rectTransform.Stretch();
            return card;
        }

        static void Picture(RectTransform card, string key, ItemRarity? rarity)
        {
            var holder = UiFactory.Rect(card, "Picture");
            holder.Place(TopCenter, TopCenter, new Vector2(0f, -14f), new Vector2(118f, 118f));
            if (rarity.HasValue)
            {
                var frame = UiFactory.Image(holder, "Frame", RarityColor(rarity.Value), Shapes.Frame, true);
                frame.rectTransform.Stretch();
                UiArt.Apply(frame, ArtKeys.RarityFrame(rarity.Value));
            }
            if (Icons.TryArtImage(holder, key, rarity.HasValue ? 96f : 114f) == null)
                Icons.Shape(holder, Shapes.Rounded, Palette.Stone, Vector2.zero, new Vector2(90f, 90f));
        }

        static void Label(RectTransform card, string text, Color color)
        {
            var label = UiFactory.Text(card, "Name", text, 23, color, TextAnchor.MiddleCenter, FontStyle.Bold);
            label.rectTransform.Place(TopCenter, TopCenter, new Vector2(0f, -136f), new Vector2(CardWidth - 16f, 30f));
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 14;
            label.resizeTextMaxSize = 23;
            UiFactory.Shadow(label, Color.black, 2f);
        }

        static void Body(RectTransform card, string text)
        {
            var body = UiFactory.Text(card, "Effect", text, 17, Palette.TextLight, TextAnchor.UpperCenter);
            body.rectTransform.Place(TopCenter, TopCenter, new Vector2(0f, -168f), new Vector2(CardWidth - 22f, 62f));
            body.resizeTextForBestFit = true;
            body.resizeTextMinSize = 12;
            body.resizeTextMaxSize = 17;
        }

        static void Badge(RectTransform card, string text)
        {
            var back = UiFactory.Image(card, "Waiting", Palette.PlayGreen, Shapes.Rounded, true);
            back.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-10f, -10f), new Vector2(64f, 34f));
            var label = UiFactory.Text(back.rectTransform, "Text", text, 20, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            label.rectTransform.Stretch();
        }

        /// <summary>The price button: the currency's icon and amount, dimmed when the purse cannot pay, or a word instead.</summary>
        static void Price(RectTransform card, string id, int amount, bool gems, bool enabled, string word, Action buy)
        {
            var parts = UiFactory.Button(card, "Buy " + id, "", enabled ? Palette.PlayGreen : Palette.StoneDark, 24, buy);
            parts.Rect.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(CardWidth - 40f, 50f));
            parts.Button.interactable = enabled;
            if (word != null)
            {
                parts.Label.text = word;
                parts.Label.color = Palette.TextDim;
                return;
            }
            parts.Label.text = amount.ToString("N0", CultureInfo.InvariantCulture);
            parts.Label.rectTransform.Stretch(44, 4, 8, 4);
            var icon = UiFactory.Rect(parts.Rect, "Icon");
            icon.Place(new Vector2(0.5f, 0.5f), Center, new Vector2(-38f - amount.ToString().Length * 6f, 0f), new Vector2(34f, 34f));
            if (Icons.TryArtImage(icon, gems ? ArtKeys.GemIcon : ArtKeys.CoinIcon, 34f) == null)
                Icons.Shape(icon, Shapes.Circle, gems ? Palette.Summon : Palette.Gold, Vector2.zero, new Vector2(26f, 26f));
        }

        static Text PurseCount(RectTransform panel, Vector2 pos, string iconKey, Color fallback)
        {
            var slot = UiFactory.Rect(panel, "Purse " + iconKey);
            slot.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), pos, new Vector2(180f, 52f));
            var icon = UiFactory.Rect(slot, "Icon");
            icon.Place(new Vector2(0f, 0.5f), Center, new Vector2(24f, 0f), new Vector2(44f, 44f));
            if (Icons.TryArtImage(icon, iconKey, 44f) == null) Icons.Shape(icon, Shapes.Circle, fallback, Vector2.zero, new Vector2(34f, 34f));
            var text = UiFactory.Text(slot, "Amount", "0", 30, Palette.TextLight, TextAnchor.MiddleLeft, FontStyle.Bold);
            text.rectTransform.Stretch(54, 0, 0, 0);
            UiFactory.Shadow(text, Color.black, 2f);
            return text;
        }

        public static Color RarityColor(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Uncommon: return new Color(0.45f, 0.8f, 0.3f);
                case ItemRarity.Rare: return new Color(0.3f, 0.6f, 1f);
                case ItemRarity.Epic: return new Color(0.72f, 0.4f, 1f);
                case ItemRarity.Legendary: return new Color(1f, 0.66f, 0.2f);
                default: return new Color(0.72f, 0.72f, 0.72f);
            }
        }

        // ------------------------------------------------------------------ buying

        void Buy(ShopItem item)
        {
            bool chest = item == ShopItem.GearChest || item == ShopItem.RoyalChest;
            var owned = new HashSet<string>(_profile.Items ?? new List<string>());
            if (!Shop.TryBuy(_profile, item, _catalog, out var found))
            {
                _status.text = "Not enough " + (Shop.PricedInGems(item) ? "gems." : "coins.");
                return;
            }
            if (chest)
            {
                var piece = _catalog.Item(found);
                _status.text = owned.Contains(found)
                    ? $"The chest held {piece.DisplayName} ({piece.Rarity}), which you already own: +{_catalog.DuplicateItemCoins} coins."
                    : $"The chest held <color=#{ColorUtility.ToHtmlStringRGB(RarityColor(piece.Rarity))}>{piece.DisplayName}</color> " +
                      $"({piece.Rarity} {piece.Slot.ToString().ToLowerInvariant()})! {piece.Effect}.";
            }
            else if (Shop.Waiting(_profile, item) > 0)
                _status.text = $"Bought: {Shop.DisplayName(item).ToLowerInvariant()}. It goes with you into your next run.";
            else
                _status.text = $"Traded: +{Shop.DisplayName(item).ToLowerInvariant()}.";
            _changed?.Invoke();
            Redraw();
        }

        void BuyGear(ItemDefinition item)
        {
            if (!Shop.TryBuyGear(_profile, _catalog, item.Id, _today())) return;
            bool worn = Inventory.IsWorn(_profile, item.Id);
            _status.text = $"Bought {item.DisplayName}. " + (worn ? "You are wearing it." : "It is in your INVENTORY.");
            _changed?.Invoke();
            Redraw();
        }
    }
}
