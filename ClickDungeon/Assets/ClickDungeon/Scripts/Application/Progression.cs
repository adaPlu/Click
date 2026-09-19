using System;
using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;

namespace ClickDungeon.Application
{
    /// <summary>One talent: what it is called, what a rank does, and how many ranks it has (D-027, rules §14).</summary>
    public sealed class TalentDefinition
    {
        public string Id;
        public string DisplayName;
        public string PerRank;
        public int MaxRank;
    }

    /// <summary>
    /// Levels and talents (D-027). Experience banked from runs sets the level; each level past the first is a talent point;
    /// talents change the hero a new run starts with. A run never reads the profile: talents become starting numbers.
    /// </summary>
    public static class Progression
    {
        public const string Tough = "tough", Stocked = "stocked", QuickShield = "quick_shield", Fleet = "fleet", Lucky = "lucky";

        public static readonly TalentDefinition[] Talents =
        {
            new TalentDefinition { Id = Tough, DisplayName = "TOUGH", PerRank = "+1 max heart", MaxRank = 3 },
            new TalentDefinition { Id = Stocked, DisplayName = "STOCKED", PerRank = "+1 starting potion", MaxRank = 2 },
            // Kept under its old id so a point learned before mana (D-032) stays learned; it now deepens the pool.
            new TalentDefinition { Id = QuickShield, DisplayName = "FOCUS", PerRank = "+1 max mana", MaxRank = 2 },
            new TalentDefinition { Id = Fleet, DisplayName = "FLEET", PerRank = "Dash costs 1 less mana", MaxRank = 1 },
            new TalentDefinition { Id = Lucky, DisplayName = "LUCKY", PerRank = "+2 coins for every chest reward", MaxRank = 2 },
        };

        /// <summary>Bonus coins per chest reward for each rank of Lucky.</summary>
        public const int LuckyCoins = 2;

        /// <summary>Experience needed to reach a level: 0, 50, 150, 300, 500... (50 × the triangle numbers).</summary>
        public static int XpForLevel(int level) => level <= 1 ? 0 : 50 * (level - 1) * level / 2;

        public static int Level(int xp)
        {
            int level = 1;
            while (xp >= XpForLevel(level + 1)) level++;
            return level;
        }

        public static int Level(ProfileState profile) => Level(profile?.Xp ?? 0);

        public static int PointsEarned(ProfileState profile) => Level(profile) - 1;

        public static int PointsSpent(ProfileState profile)
        {
            int spent = 0;
            if (profile?.Talents == null) return 0;
            foreach (var rank in profile.Talents.Values) spent += Math.Max(0, rank);
            return spent;
        }

        public static int PointsFree(ProfileState profile) => Math.Max(0, PointsEarned(profile) - PointsSpent(profile));

        public static int Rank(ProfileState profile, string talentId) =>
            profile?.Talents != null && profile.Talents.TryGetValue(talentId, out int rank) ? Math.Max(0, rank) : 0;

        public static TalentDefinition Talent(string id)
        {
            foreach (var talent in Talents)
                if (talent.Id == id) return talent;
            return null;
        }

        public static bool CanLearn(ProfileState profile, string talentId)
        {
            var talent = Talent(talentId);
            return talent != null && PointsFree(profile) > 0 && Rank(profile, talentId) < talent.MaxRank;
        }

        /// <summary>Learns one rank, or returns false and changes nothing (no free point, unknown talent, or at its top rank).</summary>
        public static bool TryLearn(ProfileState profile, string talentId)
        {
            if (!CanLearn(profile, talentId)) return false;
            if (profile.Talents == null) profile.Talents = new Dictionary<string, int>();
            profile.Talents[talentId] = Rank(profile, talentId) + 1;
            return true;
        }

        /// <summary>Unlearns everything and refunds every point: trying a build costs nothing.</summary>
        public static void Reset(ProfileState profile)
        {
            if (profile == null) return;
            profile.Talents = new Dictionary<string, int>();
        }

        /// <summary>Adds what the run earned. Called with the rest of the banking, once, when a run ends.</summary>
        public static void BankXp(ProfileState profile, RunState run)
        {
            if (profile == null || run == null) return;
            profile.Xp += Math.Max(0, run.XpEarned);
        }

        /// <summary>
        /// Turns learned talents into the new run's starting numbers. Talents stay learned: unlike shop provisions they are not
        /// spent, so every run starts with them. The dash never costs less than one mana.
        /// </summary>
        public static void Apply(ProfileState profile, RunState run, ContentCatalog catalog)
        {
            if (profile == null || run == null) return;
            var hero = run.Hero;
            int tough = Rank(profile, Tough);
            hero.MaxHp += tough;
            hero.Hp += tough;
            hero.Potions += Rank(profile, Stocked);
            run.BonusCoinsPerChestReward += Rank(profile, Lucky) * LuckyCoins;
            int focus = Rank(profile, QuickShield);
            hero.MaxMana += focus;
            hero.Mana += focus;
            run.DashCostCut += Rank(profile, Fleet);
        }
    }
}
