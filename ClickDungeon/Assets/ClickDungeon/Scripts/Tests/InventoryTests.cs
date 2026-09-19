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
    /// <summary>D-028: equipment is found in runs, kept between them, and what is worn shapes every new run.</summary>
    public class InventoryTests
    {
        [Test]
        public void EveryItemHasASlotANameAndAnEffect()
        {
            Assert.That(Catalog.Items.Count, Is.GreaterThanOrEqualTo(5));
            foreach (var item in Catalog.Items)
            {
                Assert.That(item.DisplayName, Is.Not.Empty, item.Id);
                Assert.That(item.Effect, Is.Not.Empty, item.Id);
                int numbers = item.SlashDamage + item.MaxHp + item.PotionHeal + item.MaxMana + item.DashCostCut
                              + item.CoinsPerChestReward + item.XpPerFloor;
                Assert.That(numbers, Is.GreaterThan(0), $"{item.Id} must do something.");
            }
            foreach (ItemSlot slot in System.Enum.GetValues(typeof(ItemSlot)))
                Assert.That(Catalog.Items.Any(i => i.Slot == slot), Is.True, $"Nothing for the {slot} slot.");
        }

        [Test]
        public void LordBlobertAGreatChestAndAPremiumChestEachDropAnItem()
        {
            // Every source at a sure drop; how often each really drops is tested below (D-040).
            using var sure = SureDrops();
            var boss = Run(Catalog.RunFloorCount, 1234UL, ".....", ".....", ".HB..", ".....", "....X");
            var blobert = Enemy(boss, "lord_blobert");
            blobert.Mode = EnemyMode.Normal;
            blobert.Hp = 1;
            DoOk(boss, PlayerCommand.Slash(blobert.Pos));
            Assert.That(boss.ItemsFound.Count, Is.EqualTo(1));

            var vault = Run(".....", ".....", ".HW..", ".....", ".....");
            vault.Floor.IsVault = true;
            OpenChest(vault, P(2, 2));
            Assert.That(vault.ItemsFound.Count, Is.EqualTo(1));

            var premium = Run(".....", ".....", ".HC..", ".....", ".....");
            premium.Floor[P(2, 2)].Premium = true;
            premium.Floor[P(2, 2)].Quality = ChestQuality.Epic;
            premium.Hero.SpecialKeys = 1;
            OpenChest(premium, P(2, 2));
            Assert.That(premium.ItemsFound.Count, Is.EqualTo(1));

            var plain = Run(".....", ".....", ".HC..", ".....", ".....");
            OpenChest(plain, P(2, 2));
            Assert.That(plain.ItemsFound, Is.Empty, "An ordinary chest holds no equipment.");
        }

        [Test]
        public void TheSameRunAlwaysFindsTheSameItem()
        {
            using var sure = SureDrops();
            string Drop()
            {
                var vault = Run(".....", ".....", ".HW..", ".....", ".....");
                vault.Floor.IsVault = true;
                OpenChest(vault, P(2, 2));
                return vault.ItemsFound.Single();
            }
            Assert.That(Drop(), Is.EqualTo(Drop()));
        }

        /// <summary>Sets every item source to a sure drop until disposed.</summary>
        static System.IDisposable SureDrops()
        {
            var t = Catalog.Treasure;
            var (boss, great, premium) = (t.ItemChanceBoss, t.ItemChanceGreatChest, t.ItemChancePremium);
            t.ItemChanceBoss = t.ItemChanceGreatChest = t.ItemChancePremium = 100;
            return new Restore(() => (t.ItemChanceBoss, t.ItemChanceGreatChest, t.ItemChancePremium) = (boss, great, premium));
        }

        sealed class Restore : System.IDisposable
        {
            readonly System.Action _undo;
            public Restore(System.Action undo) => _undo = undo;
            public void Dispose() => _undo();
        }

        [Test]
        public void GearDropsOnlySometimesExceptFromAPremiumChest()
        {
            // D-040: across many vaults about a third of great chests hold gear; a premium chest always does.
            int great = 0, premium = 0;
            for (ulong seed = 1; seed <= 200; seed++)
            {
                var vault = Run(".....", ".....", ".HW..", ".....", ".....");
                vault.RunSeed = seed;
                vault.Floor.IsVault = true;
                OpenChest(vault, P(2, 2));
                great += vault.ItemsFound.Count;

                var paid = Run(".....", ".....", ".HC..", ".....", ".....");
                paid.RunSeed = seed;
                paid.Floor[P(2, 2)].Premium = true;
                paid.Floor[P(2, 2)].Quality = ChestQuality.Epic;
                paid.Hero.SpecialKeys = 1;
                OpenChest(paid, P(2, 2));
                premium += paid.ItemsFound.Count;
            }
            Assert.That(great, Is.InRange(40, 100), "Roughly 35% of great chests hold gear.");
            Assert.That(premium, Is.EqualTo(200), "A premium chest always holds gear.");
        }

        [Test]
        public void ANewFindIsKeptAndWornADuplicateBecomesCoins()
        {
            var profile = new ProfileState();
            var run = new RunState { Hero = new HeroState(), ItemsFound = { "steel_sword" } };
            Assert.That(Inventory.Bank(profile, run, Catalog), Is.Zero);
            Assert.That(Inventory.Owns(profile, "steel_sword"), Is.True);
            Assert.That(Inventory.Worn(profile, ItemSlot.Weapon), Is.EqualTo("steel_sword"), "An empty slot takes a first find.");

            var again = new RunState { Hero = new HeroState(), ItemsFound = { "steel_sword", "lucky_wand" } };
            Assert.That(Inventory.Bank(profile, again, Catalog), Is.EqualTo(Catalog.DuplicateItemCoins));
            Assert.That(profile.Coins, Is.EqualTo(Catalog.DuplicateItemCoins));
            Assert.That(Inventory.Worn(profile, ItemSlot.Weapon), Is.EqualTo("steel_sword"), "A second weapon does not swap the first out.");
        }

        [Test]
        public void OnlyAnOwnedItemCanBeWornAndOnlyInItsOwnSlot()
        {
            var profile = new ProfileState { Items = { "lucky_wand", "steel_sword" } };
            Assert.That(Inventory.Equip(profile, Catalog, "royal_plate"), Is.False, "Not owned.");
            Assert.That(Inventory.Equip(profile, Catalog, "lucky_wand"), Is.True);
            Assert.That(Inventory.Equip(profile, Catalog, "steel_sword"), Is.True);
            Assert.That(Inventory.Worn(profile, ItemSlot.Weapon), Is.EqualTo("steel_sword"), "One item per slot.");
            Inventory.Unequip(profile, ItemSlot.Weapon);
            Assert.That(Inventory.Worn(profile, ItemSlot.Weapon), Is.Null);
        }

        [Test]
        public void WornItemsShapeTheNewRun()
        {
            var profile = new ProfileState { Items = { "steel_sword", "royal_plate", "healing_charm", "swift_boots" } };
            foreach (var id in profile.Items.ToList()) Inventory.Equip(profile, Catalog, id);
            var plain = RunFactory.NewRun(5UL, Catalog, new List<GameEvent>(), ContentCatalog.DefaultHeroId, MovementMode.Free);
            var geared = RunFactory.NewRun(5UL, Catalog, new List<GameEvent>(), ContentCatalog.DefaultHeroId, MovementMode.Free);
            Inventory.Apply(profile, geared, Catalog);

            Assert.That(geared.Hero.SlashDamage, Is.EqualTo(plain.Hero.SlashDamage + 1));
            Assert.That(geared.Hero.MaxHp, Is.EqualTo(plain.Hero.MaxHp + Catalog.Item("royal_plate").MaxHp));
            Assert.That(geared.PotionHealBonus, Is.EqualTo(2));
            Assert.That(geared.DashCostCut, Is.EqualTo(1));
        }

        [Test]
        public void TheHealingCharmAndTheScholarsRingWorkInPlay()
        {
            var run = Run(".....", ".....", "..H..", ".....", ".....");
            run.PotionHealBonus = 2;
            run.Hero.Hp = 1;
            run.Hero.Potions = 1;
            int heal = Catalog.HeroClass("knight").PotionHeal;
            DoOk(run, PlayerCommand.Potion());
            Assert.That(run.Hero.Hp, Is.EqualTo(System.Math.Min(run.Hero.MaxHp, 1 + heal + 2)));

            var stairs = Run(".....", ".....", ".HxK.", ".....", ".....");
            stairs.BonusXpPerFloor = 5;
            DoOk(stairs, PlayerCommand.Move(P(3, 2)));
            DoOk(stairs, PlayerCommand.Move(P(2, 2)));
            Assert.That(stairs.XpEarned, Is.EqualTo(Catalog.Xp.PerFloor + 5));
        }
    }
}
