using System;
using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;

namespace ClickDungeon.Application
{
    /// <summary>
    /// Levels and class talents (D-027, D-037). Experience banked from runs sets the level; each level past the first is a
    /// talent point for every class, so each hero builds its own tree from the same level. Talents change the hero a new
    /// run starts with, and a run never reads the profile: learned talents become starting numbers and perks.
    /// </summary>
    public static class Progression
    {
        /// <summary>Points spent in a class before each tier opens: tier 1 at once, then 2, 4 and 7.</summary>
        public static readonly int[] TierPoints = { 0, 0, 2, 4, 7 };

        /// <summary>
        /// The highest level the game counts to (DATA-17). Without a ceiling the level search walks up one level at a time
        /// for as long as the curve keeps rising, and the curve cannot rise past 2^30 in <c>int</c>, so a profile carrying a
        /// billion experience would never stop climbing. The cap is far beyond any real run; experience past it is kept but
        /// buys no more levels.
        /// </summary>
        public const int MaxLevel = 500;

        /// <summary>Experience needed to reach a level: 0, 50, 150, 300, 500... (50 × the triangle numbers), flat past <see cref="MaxLevel"/>.</summary>
        public static int XpForLevel(int level)
        {
            if (level <= 1) return 0;
            if (level > MaxLevel) level = MaxLevel;
            // Widened so the product cannot wrap; at MaxLevel the result is still a few million.
            return (int)(50L * (level - 1) * level / 2L);
        }

        public static int Level(int xp)
        {
            int level = 1;
            while (level < MaxLevel && xp >= XpForLevel(level + 1)) level++;
            return level;
        }

        public static int Level(ProfileState profile) => Level(profile?.Xp ?? 0);

        /// <summary>Renown (D-040): levels past the first plus items worn.</summary>
        public static int Renown(ProfileState profile, ContentCatalog catalog)
        {
            if (profile == null) return 0;
            int worn = 0;
            if (profile.Equipped != null)
                foreach (var id in profile.Equipped.Values)
                    if (catalog.Item(id) != null && Inventory.Owns(profile, id)) worn++;
            return Level(profile) - 1 + worn;
        }

        /// <summary>The threat a run starts with (D-040): one point per few renown, capped.</summary>
        public static int Threat(ProfileState profile, ContentCatalog catalog) =>
            Math.Min(catalog.Renown.MaxThreat, Renown(profile, catalog) / Math.Max(1, catalog.Renown.RenownPerThreat));

        /// <summary>Points every class has to spend: one per level past the first.</summary>
        public static int PointsEarned(ProfileState profile) => Level(profile) - 1;

        public static int Rank(ProfileState profile, string talentId) =>
            profile?.Talents != null && profile.Talents.TryGetValue(talentId, out int rank) ? Math.Max(0, rank) : 0;

        /// <summary>Points spent in one class's tree. Ranks of talents no class has any more are ignored, so they come back.</summary>
        public static int PointsSpent(ProfileState profile, ContentCatalog catalog, string classId)
        {
            int spent = 0;
            foreach (var talent in catalog.TalentsOf(classId)) spent += Math.Min(talent.MaxRank, Rank(profile, talent.Id));
            return spent;
        }

        public static int PointsFree(ProfileState profile, ContentCatalog catalog, string classId) =>
            Math.Max(0, PointsEarned(profile) - PointsSpent(profile, catalog, classId));

        public static string ClassOf(ContentCatalog catalog, string heroId) =>
            catalog.HeroIdentities.TryGetValue(heroId ?? "", out var identity) ? identity.ClassId : catalog.HeroIdentity(ContentCatalog.DefaultHeroId).ClassId;

        /// <summary>Why a talent cannot be learned right now, or null when it can.</summary>
        public static string Locked(ProfileState profile, ContentCatalog catalog, string talentId)
        {
            var talent = catalog.Talent(talentId);
            if (talent == null || profile == null) return "Unknown talent.";
            if (Rank(profile, talent.Id) >= talent.MaxRank) return "Fully learned.";
            if (talent.Tier < TierPoints.Length && PointsSpent(profile, catalog, talent.ClassId) < TierPoints[talent.Tier])
                return $"Needs {TierPoints[talent.Tier]} points spent in this tree.";
            if (talent.Requires != null && Rank(profile, talent.Requires) <= 0)
                return $"Needs {catalog.Talent(talent.Requires).Name} first.";
            if (talent.Capstone)
                foreach (var other in catalog.TalentsOf(talent.ClassId))
                    if (other.Capstone && other.Id != talent.Id && Rank(profile, other.Id) > 0)
                        return $"Only one capstone: {other.Name} is chosen. Reset to change it.";
            if (PointsFree(profile, catalog, talent.ClassId) <= 0) return "No talent points. Each level gives one.";
            return null;
        }

        public static bool CanLearn(ProfileState profile, ContentCatalog catalog, string talentId) => Locked(profile, catalog, talentId) == null;

        /// <summary>Learns one rank, or returns false and changes nothing.</summary>
        public static bool TryLearn(ProfileState profile, ContentCatalog catalog, string talentId)
        {
            if (!CanLearn(profile, catalog, talentId)) return false;
            if (profile.Talents == null) profile.Talents = new Dictionary<string, int>();
            profile.Talents[talentId] = Rank(profile, talentId) + 1;
            return true;
        }

        /// <summary>Unlearns one class's talents and refunds their points: trying a build costs nothing.</summary>
        public static void Reset(ProfileState profile, ContentCatalog catalog, string classId)
        {
            if (profile?.Talents == null) return;
            foreach (var talent in catalog.TalentsOf(classId)) profile.Talents.Remove(talent.Id);
        }

        /// <summary>Adds what the run earned. Called with the rest of the banking, once, when a run ends.</summary>
        public static void BankXp(ProfileState profile, RunState run)
        {
            if (profile == null || run == null) return;
            // The same ceiling the store clamps to on load (D-054). Without it a profile already carrying an absurd
            // total — only a hand-edited one gets there — wraps negative on the next win, and stays that way until it
            // is next read back from disk.
            profile.Xp = Math.Min(XpForLevel(MaxLevel), profile.Xp + Math.Max(0, run.XpEarned));
        }

        /// <summary>
        /// Turns the playing class's learned talents into the new run's starting numbers and perks. Talents stay learned:
        /// unlike shop boosts they are not spent, so every run of that class starts with them.
        /// </summary>
        /// <summary>
        /// The skills this class has unlocked: one for each talent it has learned that names one (D-075). In tree order,
        /// so the same profile always reports the same list.
        /// </summary>
        public static List<string> UnlockedSkills(ProfileState profile, ContentCatalog catalog, string classId)
        {
            var list = new List<string>();
            if (profile == null || classId == null) return list;
            foreach (var talent in catalog.TalentsOf(classId))
                if (talent.SkillId != null && Rank(profile, talent.Id) > 0 && catalog.SkillOrNull(talent.SkillId) != null)
                    list.Add(talent.SkillId);
            return list;
        }

        /// <summary>
        /// What the hero actually carries, in slot order (D-075). A profile is a file on disk, so nothing in it is
        /// trusted: an id that is not a skill, not this class's, not unlocked, or listed twice is dropped, and the list
        /// is cut to the number of slots. Empty slots are filled from what is unlocked, in tree order - with exactly
        /// three skills a class today the loadout is not yet a choice, and a player should not have to make it before
        /// it is one (SEC-04's rule for worn items, applied to skills).
        /// </summary>
        public static List<string> EquippedSkills(ProfileState profile, ContentCatalog catalog, string classId)
        {
            var unlocked = UnlockedSkills(profile, catalog, classId);
            var equipped = new List<string>();
            if (profile?.Skills != null && classId != null && profile.Skills.TryGetValue(classId, out var chosen) && chosen != null)
                foreach (var id in chosen)
                    if (unlocked.Contains(id) && !equipped.Contains(id) && equipped.Count < ContentCatalog.SkillSlots)
                        equipped.Add(id);
            foreach (var id in unlocked)
                if (equipped.Count >= ContentCatalog.SkillSlots) break;
                else if (!equipped.Contains(id)) equipped.Add(id);
            return equipped;
        }

        public static void Apply(ProfileState profile, RunState run, ContentCatalog catalog)
        {
            if (profile != null && run?.Hero != null)
                run.Skills = EquippedSkills(profile, catalog, run.Hero.ClassId);
            if (profile == null || run == null) return;
            var hero = run.Hero;
            if (run.Perks == null) run.Perks = new Dictionary<string, int>();
            foreach (var talent in catalog.TalentsOf(hero.ClassId))
            {
                int rank = Math.Min(talent.MaxRank, Rank(profile, talent.Id));
                if (rank <= 0) continue;
                int value = rank * talent.Amount;
                switch (talent.Effect)
                {
                    case TalentEffect.MaxHearts:
                        hero.MaxHp += value;
                        hero.Hp += value;
                        break;
                    case TalentEffect.DashCostCut: run.DashCostCut += value; break;
                    case TalentEffect.CoinsPerChestReward: run.BonusCoinsPerChestReward += value; break;
                    case TalentEffect.PotionHeal: run.PotionHealBonus += value; break;
                    case TalentEffect.MaxMana:
                        hero.MaxMana += value;
                        hero.Mana += value;
                        break;
                    default:
                        var key = talent.Effect.ToString();
                        run.Perks[key] = (run.Perks.TryGetValue(key, out int had) ? had : 0) + value;
                        break;
                }
            }
        }
    }
}
