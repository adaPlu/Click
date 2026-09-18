using System;

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

        /// <summary>Runs finished, so the shop can say something true about a first-time player.</summary>
        public int RunsFinished;
        public int RunsWon;

        public ProfileState Copy() => new ProfileState
        {
            SchemaVersion = SchemaVersion, Coins = Coins, Gems = Gems,
            PotionRations = PotionRations, HeartTokens = HeartTokens,
            RunsFinished = RunsFinished, RunsWon = RunsWon,
        };
    }
}
