using System;
using System.IO;
using System.Text;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using Newtonsoft.Json;

namespace ClickDungeon.Application
{
    /// <summary>The shop's stock (D-025, rules §13). Prices live here so the rules doc and the menu cannot drift apart.</summary>
    public enum ShopItem { PotionRation, HeartToken, SpecialKey }

    public static class Shop
    {
        public const int PotionRationCoins = 60;
        public const int HeartTokenCoins = 120;
        /// <summary>Priced in gems, as on the store card (D-026).</summary>
        public const int SpecialKeyGems = 150;

        public static readonly ShopItem[] Stock = { ShopItem.PotionRation, ShopItem.HeartToken, ShopItem.SpecialKey };

        public static int Price(ShopItem item)
        {
            switch (item)
            {
                case ShopItem.HeartToken: return HeartTokenCoins;
                case ShopItem.SpecialKey: return SpecialKeyGems;
                default: return PotionRationCoins;
            }
        }

        /// <summary>The special key is the one item bought with gems.</summary>
        public static bool PricedInGems(ShopItem item) => item == ShopItem.SpecialKey;

        public static string Currency(ShopItem item) => PricedInGems(item) ? "GEMS" : "COINS";

        public static string DisplayName(ShopItem item)
        {
            switch (item)
            {
                case ShopItem.HeartToken: return "HEART TOKEN";
                case ShopItem.SpecialKey: return "SPECIAL KEY";
                default: return "POTION RATION";
            }
        }

        /// <summary>What the item does, in the player's words.</summary>
        public static string Describe(ShopItem item, ContentCatalog catalog)
        {
            switch (item)
            {
                case ShopItem.HeartToken: return $"+{catalog.Treasure.HeartTokenHearts} max hearts on your next run.";
                case ShopItem.SpecialKey:
                    return $"Your next run hides a premium chest on a floor from {catalog.Treasure.PremiumFirstFloor} to " +
                           $"{catalog.Treasure.PremiumLastFloor}; the key opens it for {catalog.Treasure.PremiumChestRewards} rewards.";
                default: return $"+{catalog.Treasure.PotionRationPotions} potion on your next run.";
            }
        }

        public static bool CanAfford(ProfileState profile, ShopItem item) =>
            profile != null && (PricedInGems(item) ? profile.Gems : profile.Coins) >= Price(item);

        /// <summary>Buys one, or returns false and changes nothing when the coins or gems are not there.</summary>
        public static bool TryBuy(ProfileState profile, ShopItem item)
        {
            if (!CanAfford(profile, item)) return false;
            if (PricedInGems(item)) profile.Gems -= Price(item);
            else profile.Coins -= Price(item);
            switch (item)
            {
                case ShopItem.HeartToken: profile.HeartTokens++; break;
                case ShopItem.SpecialKey: profile.SpecialKeys++; break;
                default: profile.PotionRations++; break;
            }
            return true;
        }
    }

    /// <summary>
    /// The profile between runs: banking what a run found, and spending it. Kept apart from the run save so a corrupt or
    /// abandoned run never costs the player their coins.
    /// </summary>
    public static class ProfileSystem
    {
        /// <summary>Adds what the run carried out and counts the run. Called once, when a run ends.</summary>
        public static void Bank(ProfileState profile, RunState run)
        {
            if (profile == null || run == null) return;
            Progression.BankXp(profile, run);
            profile.Coins += Math.Max(0, run.CoinsFound);
            profile.Gems += Math.Max(0, run.GemsFound);
            // A key whose chest was never reached is not lost: it goes back in the pocket for the next run.
            if (run.Hero != null) profile.SpecialKeys += Math.Max(0, run.Hero.SpecialKeys);
            profile.RunsFinished++;
            if (run.Status == RunStatus.Won) profile.RunsWon++;
            profile.MonstersSlain += Math.Max(0, run.MonstersSlain);
            profile.ChestsOpened += Math.Max(0, run.ChestsOpened);
            profile.CoinsEarned += Math.Max(0, run.CoinsFound);
            if (run.Floor != null) profile.DeepestFloor = Math.Max(profile.DeepestFloor, run.Floor.FloorIndex);
        }

        /// <summary>
        /// Hands the bought provisions to a new run and spends them. A provision is consumed when the run starts, so quitting
        /// to the title does not get it back.
        /// </summary>
        public static void Provision(ProfileState profile, RunState run, ContentCatalog catalog)
        {
            if (profile == null || run == null) return;
            if (profile.HeartTokens > 0)
            {
                int hearts = profile.HeartTokens * catalog.Treasure.HeartTokenHearts;
                run.Hero.MaxHp += hearts;
                run.Hero.Hp += hearts;
                profile.HeartTokens = 0;
            }
            if (profile.PotionRations > 0)
            {
                run.Hero.Potions += profile.PotionRations * catalog.Treasure.PotionRationPotions;
                profile.PotionRations = 0;
            }
            if (profile.SpecialKeys > 0)
            {
                // One premium chest per key, on the floors that can hold one; any key beyond that stays in the pocket.
                int floors = Math.Max(0, catalog.Treasure.PremiumLastFloor - catalog.Treasure.PremiumFirstFloor + 1);
                int carried = Math.Min(profile.SpecialKeys, floors);
                run.Hero.SpecialKeys += carried;
                run.PremiumChestsToPlace += carried;
                profile.SpecialKeys -= carried;
            }
        }
    }

    public interface IProfileStore
    {
        ProfileState Load();
        void Save(ProfileState profile);
    }

    /// <summary>A profile that only lives as long as the process: automation runs and tests never touch a real one.</summary>
    public sealed class MemoryProfileStore : IProfileStore
    {
        ProfileState _profile = new ProfileState();

        public ProfileState Load() => _profile;
        public void Save(ProfileState profile) => _profile = profile;
    }

    /// <summary>Atomic local profile: temp → verify → replace, like the run save.</summary>
    public sealed class FileProfileStore : IProfileStore
    {
        public readonly string MainPath;
        public readonly string TempPath;

        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            MissingMemberHandling = MissingMemberHandling.Ignore,
        };

        public FileProfileStore(string directory, string fileName = "profile.json")
        {
            Directory.CreateDirectory(directory);
            MainPath = Path.Combine(directory, fileName);
            TempPath = MainPath + ".tmp";
        }

        /// <summary>A missing or unreadable profile is an empty one: a broken file must never stop the game from starting.</summary>
        public ProfileState Load()
        {
            try
            {
                if (!File.Exists(MainPath)) return new ProfileState();
                var profile = JsonConvert.DeserializeObject<ProfileState>(File.ReadAllText(MainPath), Settings);
                if (profile == null || profile.SchemaVersion != Versions.ProfileSchema) return new ProfileState();
                profile.Coins = Math.Max(0, profile.Coins);
                profile.Gems = Math.Max(0, profile.Gems);
                profile.PotionRations = Math.Max(0, profile.PotionRations);
                profile.HeartTokens = Math.Max(0, profile.HeartTokens);
                profile.SpecialKeys = Math.Max(0, profile.SpecialKeys);
                profile.Xp = Math.Max(0, profile.Xp);
                profile.DailyStreak = Math.Max(0, profile.DailyStreak);
                if (profile.Talents == null) profile.Talents = new System.Collections.Generic.Dictionary<string, int>();
                if (profile.Items == null) profile.Items = new System.Collections.Generic.List<string>();
                if (profile.Equipped == null) profile.Equipped = new System.Collections.Generic.Dictionary<string, string>();
                if (profile.Achievements == null) profile.Achievements = new System.Collections.Generic.List<string>();
                if (profile.Mail == null) profile.Mail = new System.Collections.Generic.List<MailMessage>();
                profile.Mail.RemoveAll(m => m == null);
                return profile;
            }
            catch (Exception)
            {
                return new ProfileState();
            }
        }

        public void Save(ProfileState profile)
        {
            if (profile == null) return;
            File.WriteAllText(TempPath, JsonConvert.SerializeObject(profile, Settings), new UTF8Encoding(false));
            if (File.Exists(MainPath)) File.Delete(MainPath);
            File.Move(TempPath, MainPath);
        }
    }
}
