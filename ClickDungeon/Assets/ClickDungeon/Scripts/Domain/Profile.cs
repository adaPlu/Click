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

        /// <summary>Special keys owned and not yet carried into a run (D-026).</summary>
        public int SpecialKeys;

        /// <summary>Experience banked from every run (D-027). The level follows from it.</summary>
        public int Xp;
        /// <summary>Talent ranks learned, by talent id. Points come from levels; resetting refunds them.</summary>
        public Dictionary<string, int> Talents = new Dictionary<string, int>();

        /// <summary>Items owned (D-028), by id, each at most once.</summary>
        public List<string> Items = new List<string>();
        /// <summary>What is worn, by slot name. A worn item is one of <see cref="Items"/>.</summary>
        public Dictionary<string, string> Equipped = new Dictionary<string, string>();

        /// <summary>Runs finished, so the shop can say something true about a first-time player.</summary>
        public int RunsFinished;
        public int RunsWon;

        public ProfileState Copy() => new ProfileState
        {
            SchemaVersion = SchemaVersion, Coins = Coins, Gems = Gems,
            PotionRations = PotionRations, HeartTokens = HeartTokens, SpecialKeys = SpecialKeys,
            RunsFinished = RunsFinished, RunsWon = RunsWon, Xp = Xp,
            Talents = Talents == null ? new Dictionary<string, int>() : new Dictionary<string, int>(Talents),
            Items = Items == null ? new List<string>() : new List<string>(Items),
            Equipped = Equipped == null ? new Dictionary<string, string>() : new Dictionary<string, string>(Equipped),
        };
    }
}
