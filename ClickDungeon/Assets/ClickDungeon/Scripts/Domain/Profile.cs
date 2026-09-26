using System;
using System.Collections.Generic;

namespace ClickDungeon.Domain
{
    /// <summary>
    /// What the player keeps between runs (D-025): the treasure carried out, and the provisions bought with it that the next
    /// run starts with. A run itself never reads this — provisions are turned into hero numbers when the run is created, so
    /// the simulation stays a function of its seed, tier, hero and provisions.
    /// </summary>
    [Serializable]
    public sealed class ProfileState
    {
        public int SchemaVersion = Versions.ProfileSchema;
        public int Coins;
        public int Gems;

        /// <summary>Bought and not yet spent: extra potions the next run starts with.</summary>
        public int PotionRations;
        /// <summary>Bought and not yet spent: extra hearts the next run starts with.</summary>
        public int HeartTokens;

        /// <summary>The shop's boosts for the next run (D-036), bought and not yet spent.</summary>
        public int ManaTonics;
        public int StrengthElixirs;
        public int FortuneScrolls;
        public int WisdomScrolls;
        /// <summary>Shop chests opened so far: each chest's roll draws on it, so the same profile opens the same chests.</summary>
        public int ShopRolls;

        /// <summary>Special keys owned and not yet carried into a run (D-026).</summary>
        public int SpecialKeys;

        /// <summary>Experience banked from every run (D-027). The level follows from it.</summary>
        public int Xp;
        /// <summary>Talent ranks learned, by talent id. Points come from levels; resetting refunds them.</summary>
        public Dictionary<string, int> Talents = new Dictionary<string, int>();

        /// <summary>
        /// The usable skills carried into a run, by class id, in slot order (D-075). At most ContentCatalog.SkillSlots
        /// a class, and each has to be one the learned talents unlock - Progression.EquippedSkills is what enforces
        /// that, because a profile is a file on disk and anything may be written into it.
        /// </summary>
        public Dictionary<string, List<string>> Skills = new Dictionary<string, List<string>>();

        /// <summary>Items owned (D-028), by id, each at most once.</summary>
        public List<string> Items = new List<string>();
        /// <summary>What is worn, by slot name. A worn item is one of <see cref="Items"/>.</summary>
        public Dictionary<string, string> Equipped = new Dictionary<string, string>();

        /// <summary>The last day the daily reward was claimed (D-029), as yyyy-MM-dd on the player's own calendar; null for never.</summary>
        public string LastDailyClaim;
        /// <summary>Days claimed in a row, so the next claim knows which day of the week it is.</summary>
        public int DailyStreak;

        /// <summary>Counted over every run banked (D-030), for the crown's achievements.</summary>
        public int MonstersSlain;
        public int ChestsOpened;
        public int DeepestFloor;
        public int CoinsEarned;
        /// <summary>Achievements earned, by id.</summary>
        public List<string> Achievements = new List<string>();

        /// <summary>Letters, oldest first (D-030). Ids count up from 1 and are never reused; 0 means no letter was ever sent.</summary>
        public List<MailMessage> Mail = new List<MailMessage>();
        public int NextMailId;

        /// <summary>Runs finished, so the shop can say something true about a first-time player.</summary>
        public int RunsFinished;
        public int RunsWon;

        public ProfileState Copy() => new ProfileState
        {
            SchemaVersion = SchemaVersion, Coins = Coins, Gems = Gems,
            PotionRations = PotionRations, HeartTokens = HeartTokens, SpecialKeys = SpecialKeys,
            ManaTonics = ManaTonics, StrengthElixirs = StrengthElixirs, FortuneScrolls = FortuneScrolls, WisdomScrolls = WisdomScrolls,
            ShopRolls = ShopRolls,
            RunsFinished = RunsFinished, RunsWon = RunsWon, Xp = Xp,
            LastDailyClaim = LastDailyClaim, DailyStreak = DailyStreak,
            MonstersSlain = MonstersSlain, ChestsOpened = ChestsOpened, DeepestFloor = DeepestFloor, CoinsEarned = CoinsEarned,
            Achievements = Achievements == null ? new List<string>() : new List<string>(Achievements),
            Mail = Mail == null ? new List<MailMessage>() : Mail.ConvertAll(m => m.Copy()),
            NextMailId = NextMailId,
            Talents = Talents == null ? new Dictionary<string, int>() : new Dictionary<string, int>(Talents),
            Items = Items == null ? new List<string>() : new List<string>(Items),
            Equipped = Equipped == null ? new Dictionary<string, string>() : new Dictionary<string, string>(Equipped),
        };
    }

    /// <summary>One letter (D-030). The gift is kept in the letter until it is collected.</summary>
    [Serializable]
    public sealed class MailMessage
    {
        public int Id;
        public string From;
        public string Subject;
        public string Body;
        public int Coins;
        public int Gems;
        public int PotionRations;
        public int HeartTokens;
        public int SpecialKeys;
        /// <summary>The gift in words, as its letter shows it.</summary>
        public string GiftLabel;
        public bool Read;
        public bool Collected;

        public bool HasGift => Coins + Gems + PotionRations + HeartTokens + SpecialKeys > 0;

        public MailMessage Copy() => (MailMessage)MemberwiseClone();
    }
}
