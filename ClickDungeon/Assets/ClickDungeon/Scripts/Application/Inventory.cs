using System;
using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;

namespace ClickDungeon.Application
{
    /// <summary>
    /// Equipment between runs (D-028, rules §15). Items are found in runs and banked here; what is worn shapes every run
    /// started afterwards and is never used up. A run never reads the inventory: worn items become starting numbers.
    /// </summary>
    public static class Inventory
    {
        /// <summary>The slots in the order the INVENTORY screen shows them, head to toe.</summary>
        public static readonly ItemSlot[] SlotOrder =
            { ItemSlot.Weapon, ItemSlot.Helmet, ItemSlot.Armor, ItemSlot.Shield, ItemSlot.Boots, ItemSlot.Trinket };

        /// <summary>
        /// Gives the profile one item, however it was come by (a run's find, a shop purchase, a shop chest). A new item joins
        /// the inventory and fills its slot if that slot is empty; one already owned becomes coins. Returns those coins.
        /// </summary>
        public static int Grant(ProfileState profile, ContentCatalog catalog, string itemId)
        {
            var item = catalog.Item(itemId);
            if (profile == null || item == null) return 0;
            if (profile.Items == null) profile.Items = new List<string>();
            if (profile.Equipped == null) profile.Equipped = new Dictionary<string, string>();
            if (profile.Items.Contains(itemId))
            {
                profile.Coins += catalog.DuplicateItemCoins;
                return catalog.DuplicateItemCoins;
            }
            profile.Items.Add(itemId);
            if (Worn(profile, item.Slot) == null) profile.Equipped[item.Slot.ToString()] = itemId;
            return 0;
        }

        public static bool Owns(ProfileState profile, string itemId) => profile?.Items != null && profile.Items.Contains(itemId);

        public static string Worn(ProfileState profile, ItemSlot slot) =>
            profile?.Equipped != null && profile.Equipped.TryGetValue(slot.ToString(), out var id) ? id : null;

        public static bool IsWorn(ProfileState profile, string itemId)
        {
            if (profile?.Equipped == null) return false;
            foreach (var id in profile.Equipped.Values)
                if (id == itemId) return true;
            return false;
        }

        /// <summary>
        /// Adds a finished run's finds. A new item joins the inventory, and fills its slot if that slot is empty, so a
        /// first find helps at once; an item already owned becomes coins instead. Returns the coins that paid for duplicates.
        /// </summary>
        public static int Bank(ProfileState profile, RunState run, ContentCatalog catalog)
        {
            if (profile == null || run?.ItemsFound == null) return 0;
            int coins = 0;
            foreach (var id in run.ItemsFound) coins += Grant(profile, catalog, id);
            return coins;
        }

        /// <summary>Wears an owned item in its slot, replacing whatever was there. False for an item not owned.</summary>
        public static bool Equip(ProfileState profile, ContentCatalog catalog, string itemId)
        {
            var item = catalog.Item(itemId);
            if (item == null || !Owns(profile, itemId)) return false;
            if (profile.Equipped == null) profile.Equipped = new Dictionary<string, string>();
            profile.Equipped[item.Slot.ToString()] = itemId;
            return true;
        }

        public static void Unequip(ProfileState profile, ItemSlot slot) => profile?.Equipped?.Remove(slot.ToString());

        /// <summary>
        /// Makes a hand-edited <see cref="ProfileState.Equipped"/> safe to apply (SEC-04). Only the six real slots survive,
        /// each holding an owned item that belongs in that slot; anything else is taken off. Because every entry has to be
        /// filed under its own item's slot, and a slot appears once, the same item can no longer be worn twice and have its
        /// numbers counted twice.
        /// </summary>
        public static void RepairEquipped(ProfileState profile, ContentCatalog catalog)
        {
            if (profile?.Equipped == null) return;
            var worn = new Dictionary<string, string>();
            foreach (var slot in SlotOrder)
            {
                string key = slot.ToString();
                if (!profile.Equipped.TryGetValue(key, out var id)) continue;
                var item = catalog?.Item(id);
                if (item == null || item.Slot != slot || !Owns(profile, id)) continue;
                worn[key] = id;
            }
            profile.Equipped = worn;
        }

        /// <summary>Adds every worn item's numbers to the new run. A dash cost cut adds to the talent's and never goes under one.</summary>
        public static void Apply(ProfileState profile, RunState run, ContentCatalog catalog)
        {
            if (profile?.Equipped == null || run == null) return;
            var hero = run.Hero;
            foreach (var id in profile.Equipped.Values)
            {
                var item = catalog.Item(id);
                if (item == null || !Owns(profile, id)) continue;
                hero.SlashDamage += item.SlashDamage;
                hero.MaxHp += item.MaxHp;
                hero.Hp += item.MaxHp;
                run.PotionHealBonus += item.PotionHeal;
                hero.MaxMana += item.MaxMana;
                hero.Mana += item.MaxMana;
                run.DashCostCut += item.DashCostCut;
                run.BonusCoinsPerChestReward += item.CoinsPerChestReward;
                run.BonusXpPerFloor += item.XpPerFloor;
            }
        }
    }
}
