using System;
using System.Collections.Generic;
using System.Linq;
using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using NUnit.Framework;
using static ClickDungeon.Tests.Scenario;

namespace ClickDungeon.Tests
{
    /// <summary>D-036: the richer shop — boosts for the next run, gear on sale by the day, shop chests, rarity.</summary>
    public class ShopTests
    {
        static readonly DateTime Day = new DateTime(2026, 9, 18);

        [Test]
        public void EveryRarityAndSlotHasGearAndRarerIsStronger()
        {
            foreach (ItemSlot slot in Enum.GetValues(typeof(ItemSlot)))
                Assert.That(Catalog.Items.Any(i => i.Slot == slot), Is.True, slot.ToString());
            foreach (ItemRarity rarity in Enum.GetValues(typeof(ItemRarity)))
                Assert.That(Catalog.Items.Any(i => i.Rarity == rarity), Is.True, rarity.ToString());
            int Power(ItemDefinition i) => i.SlashDamage * 3 + i.MaxHp * 2 + i.MaxMana * 2 + i.PotionHeal + i.DashCostCut * 3
                                          + i.CoinsPerChestReward + i.XpPerFloor / 3;
            double Average(ItemRarity r) => Catalog.Items.Where(i => i.Rarity == r).Average(Power);
            Assert.That(Average(ItemRarity.Legendary), Is.GreaterThan(Average(ItemRarity.Rare)));
            Assert.That(Average(ItemRarity.Rare), Is.GreaterThan(Average(ItemRarity.Common)));
            Assert.That(Catalog.Items.Select(i => i.Id).Distinct().Count(), Is.EqualTo(Catalog.Items.Count));
        }

        [Test]
        public void DropsFavourCommonGearOverLegendary()
        {
            var counts = new Dictionary<ItemRarity, int>();
            for (ulong roll = 0; roll < 20000; roll++)
            {
                var item = Catalog.PickItem(Hash.Of(roll, 7UL));
                counts[item.Rarity] = counts.TryGetValue(item.Rarity, out int n) ? n + 1 : 1;
            }
            double PerItem(ItemRarity r) => counts[r] / (double)Catalog.Items.Count(i => i.Rarity == r);
            Assert.That(PerItem(ItemRarity.Common), Is.GreaterThan(PerItem(ItemRarity.Legendary) * 4));
            Assert.That(Enumerable.Range(0, 200).Select(r => Catalog.PickItem((ulong)r, ItemRarity.Rare).Rarity),
                NUnit.Framework.Has.All.GreaterThanOrEqualTo(ItemRarity.Rare));
        }

        [Test]
        public void BoostsWaitForTheNextRunAndAreSpentWhenItStarts()
        {
            var profile = new ProfileState { Coins = 10000 };
            foreach (var boost in new[] { ShopItem.ManaTonic, ShopItem.StrengthElixir, ShopItem.FortuneScroll, ShopItem.WisdomScroll })
                Assert.That(Shop.TryBuy(profile, boost), Is.True, boost.ToString());
            Assert.That(profile.Coins, Is.EqualTo(10000 - Shop.ManaTonicCoins - Shop.StrengthElixirCoins - Shop.FortuneScrollCoins - Shop.WisdomScrollCoins));

            var plain = RunFactory.NewRun(9UL, Catalog, new List<GameEvent>());
            var boosted = RunFactory.NewRun(9UL, Catalog, new List<GameEvent>());
            ProfileSystem.Provision(profile, boosted, Catalog);
            var t = Catalog.Treasure;
            Assert.That(boosted.Hero.MaxMana, Is.EqualTo(plain.Hero.MaxMana + t.ManaTonicMana));
            Assert.That(boosted.Hero.Mana, Is.EqualTo(boosted.Hero.MaxMana));
            Assert.That(boosted.Hero.SlashDamage, Is.EqualTo(plain.Hero.SlashDamage + t.StrengthElixirSlash));
            Assert.That(boosted.BonusCoinsPerChestReward, Is.EqualTo(t.FortuneScrollCoins));
            Assert.That(boosted.BonusXpPerFloor, Is.EqualTo(t.WisdomScrollXp));
            Assert.That(profile.ManaTonics + profile.StrengthElixirs + profile.FortuneScrolls + profile.WisdomScrolls, Is.Zero, "Spent.");
        }

        [Test]
        public void TalentsAndBoostsAddUpRatherThanReplaceEachOther()
        {
            var profile = new ProfileState { Xp = Progression.XpForLevel(3), FortuneScrolls = 1 };
            profile.Talents["k_light_step"] = 1;
            profile.Talents["k_treasure_sense"] = 1;
            profile.Talents["k_fortune"] = 1;
            var run = RunFactory.NewRun(9UL, Catalog, new List<GameEvent>());
            ProfileSystem.Provision(profile, run, Catalog);
            Progression.Apply(profile, run, Catalog);
            Assert.That(run.BonusCoinsPerChestReward, Is.EqualTo(Catalog.Treasure.FortuneScrollCoins + Catalog.Talent("k_fortune").Amount));
        }

        [Test]
        public void TheGearStockIsTheSameAllDayAndChangesTomorrow()
        {
            var today = Shop.GearStock(Catalog, Day.AddHours(9));
            Assert.That(today.Count, Is.EqualTo(Shop.GearStockSize));
            Assert.That(today.Distinct().Count(), Is.EqualTo(today.Count));
            Assert.That(Shop.GearStock(Catalog, Day.AddHours(22)), Is.EqualTo(today));
            bool changes = Enumerable.Range(1, 5).Any(d => !Shop.GearStock(Catalog, Day.AddDays(d)).SequenceEqual(today));
            Assert.That(changes, Is.True);
        }

        /// <summary>
        /// Rarer gear costs more, counted in one currency. The shop sells gems as well as gear, so a gem price is only
        /// dearer than a coin price if it is dearer at the exchange the same screen offers.
        /// </summary>
        [Test]
        public void GearPricesRiseWithRarityInASingleCurrency()
        {
            var rarities = (ItemRarity[])Enum.GetValues(typeof(ItemRarity));
            int previous = 0;
            foreach (var rarity in rarities)
            {
                var item = Catalog.Items.FirstOrDefault(i => i.Rarity == rarity);
                Assert.That(item, Is.Not.Null, $"Test setup: the catalog has {rarity} gear.");
                int coins = Shop.GearPriceInCoins(item);
                Assert.That(coins, Is.GreaterThan(previous),
                    $"{rarity} gear costs {coins} coins, no more than the rarity below it.");
                previous = coins;
            }
        }

        [Test]
        public void BuyingGearPaysItsRarityPriceAndWearsItInAnEmptySlot()
        {
            var item = Shop.GearStock(Catalog, Day).First();
            var profile = new ProfileState { Coins = 5000, Gems = 100 };
            Assert.That(Shop.TryBuyGear(profile, Catalog, item.Id, Day), Is.True);
            Assert.That(Inventory.Owns(profile, item.Id), Is.True);
            Assert.That(Inventory.Worn(profile, item.Slot), Is.EqualTo(item.Id));
            Assert.That(Shop.GearPricedInGems(item) ? profile.Gems : profile.Coins,
                Is.EqualTo((Shop.GearPricedInGems(item) ? 100 : 5000) - Shop.GearPrice(item)));
            Assert.That(Shop.TryBuyGear(profile, Catalog, item.Id, Day), Is.False, "Owned: not for sale twice.");

            var notToday = Catalog.Items.First(i => !Shop.GearStock(Catalog, Day).Contains(i));
            Assert.That(Shop.TryBuyGear(profile, Catalog, notToday.Id, Day), Is.False, "Only the day's stock is for sale.");
            Assert.That(Shop.TryBuyGear(new ProfileState(), Catalog, item.Id, Day), Is.False, "Nothing to pay with.");
        }

        [Test]
        public void AShopChestGrantsGearAndTheRoyalOneIsRareOrBetter()
        {
            var profile = new ProfileState { Coins = Shop.GearChestCoins, Gems = Shop.RoyalChestGems * 5 };
            Assert.That(Shop.TryBuy(profile, ShopItem.GearChest, Catalog, out var found), Is.True);
            Assert.That(Inventory.Owns(profile, found), Is.True);
            for (int i = 0; i < 5; i++)
            {
                Assert.That(Shop.TryBuy(profile, ShopItem.RoyalChest, Catalog, out var royal), Is.True);
                Assert.That(Catalog.Item(royal).Rarity, Is.GreaterThanOrEqualTo(ItemRarity.Rare));
            }
            Assert.That(profile.ShopRolls, Is.EqualTo(6));
            Assert.That(Shop.TryBuy(profile, ShopItem.RoyalChest, Catalog, out _), Is.False, "Out of gems.");
            Assert.That(Shop.TryBuy(new ProfileState { Coins = 9999 }, ShopItem.GearChest, null, out _), Is.False, "A chest needs the catalog.");
        }

        [Test]
        public void TheSameProfileOpensTheSameChests()
        {
            string Open()
            {
                var profile = new ProfileState { Coins = Shop.GearChestCoins };
                Shop.TryBuy(profile, ShopItem.GearChest, Catalog, out var found);
                return found;
            }
            Assert.That(Open(), Is.EqualTo(Open()));
        }
    }
}
