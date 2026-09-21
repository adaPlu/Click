using System;
using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;

namespace ClickDungeon.Application
{
    /// <summary>
    /// Everything the shop sells for one price (D-025, D-026, D-036): boosts carried into the next run, the special key, the
    /// two shop chests and the purse's exchange. Gear on sale is priced by rarity instead (<see cref="Shop.GearPrice"/>).
    /// </summary>
    public enum ShopItem
    {
        PotionRation, HeartToken, SpecialKey, CoinPouch, GemPouch,
        ManaTonic, StrengthElixir, FortuneScroll, WisdomScroll, GearChest, RoyalChest,
    }

    /// <summary>What the shop tabs hold (D-036).</summary>
    public enum ShopTab { Boosts, Gear, Chests, Exchange }

    /// <summary>
    /// The shop (D-025, D-036, rules §13). Prices live here so the rules doc and the shop screen cannot drift apart. It only
    /// ever changes the profile: boosts wait for the next run, gear and chest finds join the inventory.
    /// </summary>
    public static class Shop
    {
        public const int PotionRationCoins = 60;
        public const int HeartTokenCoins = 120;
        public const int ManaTonicCoins = 90;
        public const int StrengthElixirCoins = 150;
        public const int FortuneScrollCoins = 100;
        public const int WisdomScrollCoins = 100;
        /// <summary>Priced in gems, as on the store card (D-026).</summary>
        public const int SpecialKeyGems = 25;
        /// <summary>A chest with one random piece of gear, weighted by rarity; the royal one holds only rare or better.</summary>
        public const int GearChestCoins = 400;
        public const int RoyalChestGems = 20;

        /// <summary>
        /// The purse's "+" (D-033): trading one currency for the other. A round trip loses half, so neither is a way to
        /// make more of either; gems still come mainly from Lord Blobert and vaults.
        /// </summary>
        public const int CoinPouchGems = 1, CoinPouchCoins = 15;
        public const int GemPouchCoins = 30, GemPouchGems = 1;

        /// <summary>Gear on sale each day (D-036).</summary>
        public const int GearStockSize = 4;

        public static readonly ShopItem[] Stock =
        {
            ShopItem.PotionRation, ShopItem.HeartToken, ShopItem.ManaTonic, ShopItem.StrengthElixir,
            ShopItem.FortuneScroll, ShopItem.WisdomScroll, ShopItem.SpecialKey,
        };
        public static readonly ShopItem[] Chests = { ShopItem.GearChest, ShopItem.RoyalChest };
        public static readonly ShopItem[] Exchanges = { ShopItem.CoinPouch, ShopItem.GemPouch };

        const ulong ChestSalt = 0x53484F505F434845UL, StockSalt = 0x53484F505F535443UL;

        public static int Price(ShopItem item)
        {
            switch (item)
            {
                case ShopItem.HeartToken: return HeartTokenCoins;
                case ShopItem.SpecialKey: return SpecialKeyGems;
                case ShopItem.CoinPouch: return CoinPouchGems;
                case ShopItem.GemPouch: return GemPouchCoins;
                case ShopItem.ManaTonic: return ManaTonicCoins;
                case ShopItem.StrengthElixir: return StrengthElixirCoins;
                case ShopItem.FortuneScroll: return FortuneScrollCoins;
                case ShopItem.WisdomScroll: return WisdomScrollCoins;
                case ShopItem.GearChest: return GearChestCoins;
                case ShopItem.RoyalChest: return RoyalChestGems;
                default: return PotionRationCoins;
            }
        }

        /// <summary>The special key, the royal chest and the coin pouch are bought with gems; everything else with coins.</summary>
        public static bool PricedInGems(ShopItem item) =>
            item == ShopItem.SpecialKey || item == ShopItem.CoinPouch || item == ShopItem.RoyalChest;

        public static string Currency(ShopItem item) => (PricedInGems(item) ? "GEM" : "COIN") + (Price(item) == 1 ? "" : "S");

        public static string DisplayName(ShopItem item)
        {
            switch (item)
            {
                case ShopItem.HeartToken: return "HEART TOKEN";
                case ShopItem.SpecialKey: return "SPECIAL KEY";
                case ShopItem.CoinPouch: return $"{CoinPouchCoins} COINS";
                case ShopItem.GemPouch: return GemPouchGems == 1 ? "1 GEM" : $"{GemPouchGems} GEMS";
                case ShopItem.ManaTonic: return "MANA TONIC";
                case ShopItem.StrengthElixir: return "STRENGTH ELIXIR";
                case ShopItem.FortuneScroll: return "FORTUNE SCROLL";
                case ShopItem.WisdomScroll: return "WISDOM SCROLL";
                case ShopItem.GearChest: return "GEAR CHEST";
                case ShopItem.RoyalChest: return "ROYAL CHEST";
                default: return "POTION RATION";
            }
        }

        /// <summary>What the item does, in the player's words.</summary>
        public static string Describe(ShopItem item, ContentCatalog catalog)
        {
            var t = catalog.Treasure;
            switch (item)
            {
                case ShopItem.HeartToken: return $"+{t.HeartTokenHearts} max hearts on your next run.";
                case ShopItem.SpecialKey:
                    return $"Your next run hides a premium chest on a floor from {t.PremiumFirstFloor} to {t.PremiumLastFloor}; " +
                           $"the key opens it for {t.PremiumChestRewards} rewards.";
                case ShopItem.CoinPouch: return $"Trade {CoinPouchGems} gem{(CoinPouchGems == 1 ? "" : "s")} for {CoinPouchCoins} coins.";
                case ShopItem.GemPouch: return $"Trade {GemPouchCoins} coins for {GemPouchGems} gem{(GemPouchGems == 1 ? "" : "s")}.";
                case ShopItem.ManaTonic: return $"+{t.ManaTonicMana} max mana on your next run.";
                case ShopItem.StrengthElixir: return $"+{t.StrengthElixirSlash} slash damage on your next run.";
                case ShopItem.FortuneScroll: return $"+{t.FortuneScrollCoins} coins for every chest reward on your next run.";
                case ShopItem.WisdomScroll: return $"+{t.WisdomScrollXp} XP for every floor walked down on your next run.";
                case ShopItem.GearChest: return "One random piece of gear. Rarer gear is rarer. One you own becomes coins.";
                case ShopItem.RoyalChest: return "One random piece of rare, epic or legendary gear.";
                default: return $"+{t.PotionRationPotions} potion on your next run.";
            }
        }

        /// <summary>How many of a boost are waiting for the next run, or -1 for things that are not kept.</summary>
        public static int Waiting(ProfileState profile, ShopItem item)
        {
            switch (item)
            {
                case ShopItem.PotionRation: return profile.PotionRations;
                case ShopItem.HeartToken: return profile.HeartTokens;
                case ShopItem.SpecialKey: return profile.SpecialKeys;
                case ShopItem.ManaTonic: return profile.ManaTonics;
                case ShopItem.StrengthElixir: return profile.StrengthElixirs;
                case ShopItem.FortuneScroll: return profile.FortuneScrolls;
                case ShopItem.WisdomScroll: return profile.WisdomScrolls;
                default: return -1;
            }
        }

        public static bool CanAfford(ProfileState profile, ShopItem item) =>
            profile != null && (PricedInGems(item) ? profile.Gems : profile.Coins) >= Price(item);

        /// <summary>Buys one, or returns false and changes nothing when the coins or gems are not there.</summary>
        public static bool TryBuy(ProfileState profile, ShopItem item) => TryBuy(profile, item, null, out _);

        /// <summary>
        /// Buys one. A chest needs the catalog and reports what it held in <paramref name="found"/> (an item id), which has
        /// already joined the inventory or, if owned, become coins.
        /// </summary>
        public static bool TryBuy(ProfileState profile, ShopItem item, ContentCatalog catalog, out string found)
        {
            found = null;
            bool chest = item == ShopItem.GearChest || item == ShopItem.RoyalChest;
            if (!CanAfford(profile, item) || chest && (catalog == null || catalog.Items.Count == 0)) return false;
            if (PricedInGems(item)) profile.Gems -= Price(item);
            else profile.Coins -= Price(item);
            switch (item)
            {
                case ShopItem.HeartToken: profile.HeartTokens++; break;
                case ShopItem.SpecialKey: profile.SpecialKeys++; break;
                case ShopItem.CoinPouch: profile.Coins += CoinPouchCoins; break;
                case ShopItem.GemPouch: profile.Gems += GemPouchGems; break;
                case ShopItem.ManaTonic: profile.ManaTonics++; break;
                case ShopItem.StrengthElixir: profile.StrengthElixirs++; break;
                case ShopItem.FortuneScroll: profile.FortuneScrolls++; break;
                case ShopItem.WisdomScroll: profile.WisdomScrolls++; break;
                case ShopItem.GearChest:
                case ShopItem.RoyalChest:
                    // Each chest draws on the profile's own count, so reopening the game cannot re-roll one.
                    var roll = Hash.Of(ChestSalt, (ulong)profile.ShopRolls++, (ulong)item);
                    var piece = catalog.PickItem(roll, item == ShopItem.RoyalChest ? ItemRarity.Rare : ItemRarity.Common);
                    found = piece.Id;
                    Inventory.Grant(profile, catalog, piece.Id);
                    break;
                default: profile.PotionRations++; break;
            }
            return true;
        }

        // ------------------------------------------------------------------ gear on sale

        /// <summary>
        /// Gear is priced by rarity: coins up to rare, gems for epic and legendary. The two currencies have to be read
        /// against the same exchange the shop itself sells, <see cref="GemPouchCoins"/> per gem, or the ladder stops
        /// climbing where it changes currency: at 15 gems an epic piece cost 450 coins, less than a 600-coin rare one.
        /// </summary>
        public static int GearPrice(ItemDefinition item)
        {
            switch (item.Rarity)
            {
                case ItemRarity.Common: return 150;
                case ItemRarity.Uncommon: return 300;
                case ItemRarity.Rare: return 600;
                case ItemRarity.Epic: return 21;
                default: return 30;
            }
        }

        /// <summary>What a piece of gear costs with both currencies read in coins, so the rarities can be compared.</summary>
        public static int GearPriceInCoins(ItemDefinition item) =>
            GearPricedInGems(item) ? GearPrice(item) * GemPouchCoins / GemPouchGems : GearPrice(item);

        public static bool GearPricedInGems(ItemDefinition item) => item.Rarity >= ItemRarity.Epic;

        public static string GearCurrency(ItemDefinition item) => GearPricedInGems(item) ? "GEMS" : "COINS";

        /// <summary>
        /// Today's gear on sale: <see cref="GearStockSize"/> different pieces chosen by rarity weight from the calendar day, so
        /// every player sees the same stock all day and a new one tomorrow. Owning a piece does not change the stock.
        /// </summary>
        public static List<ItemDefinition> GearStock(ContentCatalog catalog, DateTime today)
        {
            var stock = new List<ItemDefinition>();
            var rng = new DeterministicRng(Hash.Of(StockSalt, (ulong)today.Date.Ticks));
            for (int tries = 0; stock.Count < Math.Min(GearStockSize, catalog.Items.Count) && tries < 200; tries++)
            {
                var item = catalog.PickItem(rng.NextULong());
                if (item != null && !stock.Contains(item)) stock.Add(item);
            }
            return stock;
        }

        public static bool CanBuyGear(ProfileState profile, ContentCatalog catalog, ItemDefinition item, DateTime today) =>
            profile != null && item != null && !Inventory.Owns(profile, item.Id) && GearStock(catalog, today).Contains(item)
            && (GearPricedInGems(item) ? profile.Gems : profile.Coins) >= GearPrice(item);

        /// <summary>Buys a piece from today's stock: it joins the inventory and fills its slot if that slot is empty.</summary>
        public static bool TryBuyGear(ProfileState profile, ContentCatalog catalog, string itemId, DateTime today)
        {
            var item = catalog.Item(itemId);
            if (!CanBuyGear(profile, catalog, item, today)) return false;
            if (GearPricedInGems(item)) profile.Gems -= GearPrice(item);
            else profile.Coins -= GearPrice(item);
            Inventory.Grant(profile, catalog, item.Id);
            return true;
        }
    }
}
