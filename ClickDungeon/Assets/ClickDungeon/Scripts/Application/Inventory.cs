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
            if (profile.Items == null) profile.Items = new List<string>();
            if (profile.Equipped == null) profile.Equipped = new Dictionary<string, string>();
            int coins = 0;
            foreach (var id in run.ItemsFound)
            {
                var item = catalog.Item(id);
                if (item == null) continue;
                if (profile.Items.Contains(id))
                {
                    coins += catalog.DuplicateItemCoins;
                    continue;
                }
                profile.Items.Add(id);
                if (Worn(profile, item.Slot) == null) profile.Equipped[item.Slot.ToString()] = id;
            }
            profile.Coins += coins;
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

        /// <summary>Adds every worn item's numbers to the new run. Cooldown cuts add to talents' and never go under one turn.</summary>
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
                run.ShieldCooldownCut += item.ShieldCooldownCut;
                run.DashCooldownCut += item.DashCooldownCut;
                run.BonusCoinsPerChestReward += item.CoinsPerChestReward;
                run.BonusXpPerFloor += item.XpPerFloor;
            }
        }
    }
}
