using System;
using System.IO;
using System.Text;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using Newtonsoft.Json;

namespace ClickDungeon.Application
{
    /// <summary>The shop's stock (D-025, rules §13). Prices live here so the rules doc and the menu cannot drift apart.</summary>
    public enum ShopItem { PotionRation, HeartToken }

    public static class Shop
    {
        public const int PotionRationCoins = 60;
        public const int HeartTokenCoins = 120;

        public static int Price(ShopItem item) => item == ShopItem.HeartToken ? HeartTokenCoins : PotionRationCoins;

        public static string DisplayName(ShopItem item) =>
            item == ShopItem.HeartToken ? "HEART TOKEN" : "POTION RATION";

        /// <summary>What the item does, in the player's words.</summary>
        public static string Describe(ShopItem item, ContentCatalog catalog) =>
            item == ShopItem.HeartToken
                ? $"+{catalog.Treasure.HeartTokenHearts} max hearts on your next run."
                : $"+{catalog.Treasure.PotionRationPotions} potion on your next run.";

        public static bool CanAfford(ProfileState profile, ShopItem item) => profile != null && profile.Coins >= Price(item);

        /// <summary>Buys one, or returns false and changes nothing when the coins are not there.</summary>
        public static bool TryBuy(ProfileState profile, ShopItem item)
        {
            if (!CanAfford(profile, item)) return false;
            profile.Coins -= Price(item);
            if (item == ShopItem.HeartToken) profile.HeartTokens++;
            else profile.PotionRations++;
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
            profile.Coins += Math.Max(0, run.CoinsFound);
            profile.Gems += Math.Max(0, run.GemsFound);
            profile.RunsFinished++;
            if (run.Status == RunStatus.Won) profile.RunsWon++;
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
