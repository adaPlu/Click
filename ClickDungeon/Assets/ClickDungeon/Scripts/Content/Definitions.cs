using System;
using ClickDungeon.Domain;

namespace ClickDungeon.Content
{
    /// <summary>Gameplay rules shared by every hero of a class.</summary>
    public sealed class HeroClassDefinition
    {
        public string Id;
        public string DisplayName;
        public int MaxHp;
        public int SlashDamage;
        public int StartingPotions;
        public int PotionHeal;
        /// <summary>Mana (D-032): the pool a new floor starts full, and what SHIELD and DASH cost from it.</summary>
        public int MaxMana;
        public int ShieldCost;
        public int DashCost;
        public int DashDistance;
        public int RevealRadius;
        public int SenseRadius;

        /// <summary>How the class plays (D-037), for Hero Select and the talent screen.</summary>
        public string Role;
        public string Playstyle;
        /// <summary>1 (forgiving) to 3 (demanding).</summary>
        public int Difficulty = 1;
        /// <summary>The class's colour on the talent screen, as #RRGGBB.</summary>
        public string Theme = "#F2C14E";
        /// <summary>The talent paths, left to right; the talents themselves are in the catalog.</summary>
        public TalentBranch[] Branches = new TalentBranch[0];
    }

    /// <summary>One path of a class's talent tree (D-037).</summary>
    public sealed class TalentBranch
    {
        public string Id;
        public string Name;
        /// <summary>One line on what the path is for.</summary>
        public string Focus;
        public string Color = "#F2C14E";
    }

    /// <summary>
    /// One class talent (D-037). Tier 1 is open from the start; later tiers need the talent below them in the same path and
    /// enough points spent in the class. Tier 4 is a capstone, and a class may take only one.
    /// </summary>
    public sealed class TalentDefinition
    {
        public string Id;
        public string ClassId;
        public string BranchId;
        public int Tier;
        public int MaxRank = 1;
        public string Name;
        /// <summary>A short description for the node.</summary>
        public string Summary;
        /// <summary>The exact effect of one rank.</summary>
        public string PerRank;
        public TalentEffect Effect;
        /// <summary>What one rank adds to the effect.</summary>
        public int Amount = 1;
        /// <summary>The talent that must be learned first, or null.</summary>
        public string Requires;
        public bool Capstone => Tier >= 4;
    }

    /// <summary>A hero shown on Hero Select as coming soon: art and words only, no class yet (D-037).</summary>
    public sealed class HeroPreview
    {
        public string Id;
        public string DisplayName;
        public string ClassName;
        public string Role;
        public string Blurb;
    }

    /// <summary>Who the hero is (name, personality). Several identities may share a class.</summary>
    public sealed class HeroIdentityDefinition
    {
        public string Id;
        public string DisplayName;
        public string ClassId;
        public string Tagline;
        /// <summary>A title under the name on Hero Select, e.g. "The Brave...ish".</summary>
        public string Title;
        public string Quote;
    }

    /// <summary>Charger and Bomber belong to the first expansion monsters (D-058).</summary>
    public enum EnemyBehavior { Chaser, SlowChaser, Lane, Boss, Charger, Bomber }

    public sealed class EnemyDefinition
    {
        public string Id;
        public string DisplayName;
        public EnemyBehavior Behavior;
        public int MaxHp;
        public int Damage;
        public int Range = 1;
        public bool IsBoss;

        // Boss script values (ignored by normal enemies).
        public int SlamDamage;
        public int PuffTurns;
        public string SummonId;
        /// <summary>Minions a summon brings, each on a free tile next to the boss (D-039).</summary>
        public int SummonCount = 1;
        /// <summary>The slam also shakes the target's whole row and column (D-039).</summary>
        public bool SlamShakesLines;
        /// <summary>The script slams again after summoning: slam, summon, slam, puff up (D-040).</summary>
        public bool DoubleSlam;
        public int DeflatedDamageMultiplier = 2;

        /// <summary>
        /// The first fall is not the last (D-058): the enemy collapses into a pile of bones for
        /// <see cref="ReassembleTurns"/> of its turns, then stands up again at half its hearts. Any hit on the bones ends it.
        /// </summary>
        public bool Reassembles;
        public int ReassembleTurns = 2;
        /// <summary>How far a bomber lobs, counted in king's moves (D-058).</summary>
        public int ThrowRange = 3;
    }

    public sealed class HazardTuning
    {
        public int SpikeDamage = 2;
        public int BombDamage = 4;
        /// <summary>Lava is permanent and never expires, so entering one costs more than spikes.</summary>
        public int LavaDamage = 3;
        public int FountainHeal = 3;
        /// <summary>
        /// Dropping through a pit to the next floor (rules §4). Fixed: unlike spikes, bombs and lava, the difficulty tiers
        /// never change it, so the cost of taking the shortcut reads the same in every run.
        /// </summary>
        public int FallDamage = 3;
        /// <summary>Fuse set on arming. 1 = explodes in the environment step of the following turn.</summary>
        public int BombFuse = 1;
        public int BombRadius = 1;
    }

    /// <summary>Hand-authored 5×5 room shape. Rows are written top row first: '#' wall, 'o' pit, '.' floor.</summary>
    public sealed class TopologyTemplate
    {
        public string Id;
        public bool BossArena;
        public string[] Rows;

        public Terrain TerrainAt(int x, int y)
        {
            char ch = Rows[BoardRules.Size - 1 - y][x];
            switch (ch)
            {
                case '#': return Terrain.Wall;
                case 'o': return Terrain.Pit;
                case '.': return Terrain.Floor;
                default: throw new FormatException($"Template '{Id}' has unknown tile '{ch}'.");
            }
        }
    }

    public sealed class FloorProfile
    {
        public int FloorIndex;
        public string Name;
        public bool IsBoss;
        public string BossId;
        /// <summary>Enemy ids; repeat an id to weight it.</summary>
        public string[] EnemyPool = Array.Empty<string>();
        public int MinEnemies;
        public int MaxEnemies;
        public int MinSpikes;
        public int MaxSpikes;
        public int MinBombs;
        public int MaxBombs;
        public int Chests;
        public int MinPotions;
        public int MaxPotions;
        public int MinExitDistance = 4;
        public int MinLava;
        public int MaxLava;
        /// <summary>Teleport pads are placed as a pair, or not at all.</summary>
        public bool Teleports;
        public int Fountains;
        /// <summary>A vault door plus the pressure plate that opens it (D-018).</summary>
        public bool Vault;
    }

    /// <summary>
    /// A difficulty tier: its name plus adjustments added to the base content when a catalog is built for it.
    /// Results are clamped so nothing drops below 1 damage or 1 HP.
    /// </summary>
    public sealed class DifficultyDefinition
    {
        public Difficulty Id;
        public string DisplayName;
        public string Tagline;
        public int HeroMaxHp;
        public int StartingPotions;
        /// <summary>Added to normal enemies' max HP.</summary>
        public int EnemyHp;
        /// <summary>Added to every enemy's melee and fire damage, including the boss's puffed attack.</summary>
        public int EnemyDamage;
        public int BossHp;
        public int BossSlamDamage;
        /// <summary>Added to spike and bomb damage.</summary>
        public int HazardDamage;
        /// <summary>Added to the enemy count range of normal floors (a minimum of 1 or more stays at least 1; the maximum stays at least 1).</summary>
        public int ExtraEnemies;
        /// <summary>HP restored when the hero arrives on the next floor.</summary>
        public int FloorClearHeal;
    }

    /// <summary>What a vault room holds (D-018): either one great chest or a handful of ordinary ones.</summary>
    /// <summary>
    /// A piece of equipment (D-028, rules §15). What it does is a set of numbers added to the hero a run starts with, so an
    /// item is never read during a run.
    /// </summary>
    public sealed class ItemDefinition
    {
        public string Id;
        public string DisplayName;
        public ItemSlot Slot;
        public ItemRarity Rarity;
        public string Effect;
        public int SlashDamage;
        public int MaxHp;
        public int PotionHeal;
        /// <summary>Mana (D-032): more of it, and a cheaper dash.</summary>
        public int MaxMana;
        public int DashCostCut;
        public int CoinsPerChestReward;
        public int XpPerFloor;
    }

    /// <summary>
    /// A gift to the profile: a day of the daily reward (D-029), an achievement's prize or a letter's attachment (D-030).
    /// Everything it gives is something the shop sells or a run carries out, so a gift is never a currency of its own.
    /// </summary>
    public sealed class RewardBundle
    {
        public string Label;
        public int Coins;
        public int Gems;
        public int PotionRations;
        public int HeartTokens;
        public int SpecialKeys;
    }

    /// <summary>What an achievement counts, over every run banked so far (D-030).</summary>
    public enum AchievementStat { RunsFinished, RunsWon, MonstersSlain, ChestsOpened, DeepestFloor, CoinsEarned, Level, ItemsOwned }

    /// <summary>One of the crown's goals (D-030, rules §17): earned once when its count reaches the target.</summary>
    public sealed class AchievementDefinition
    {
        public string Id;
        public string Title;
        public string Description;
        public AchievementStat Stat;
        public int Target;
        public RewardBundle Reward;
    }

    /// <summary>
    /// Renown (D-040): the deep floors answer a hero's level and worn gear, so a built-up profile still meets a fight.
    /// Renown is (level - 1) + items worn; every <see cref="RenownPerThreat"/> of it is one point of threat, up to
    /// <see cref="MaxThreat"/>. From <see cref="FirstFloor"/> on, each monster gets threat extra hearts (Lord Blobert
    /// <see cref="BossHpPerThreat"/> per point) and hits one harder per <see cref="ThreatPerExtraDamage"/> threat.
    /// </summary>
    public sealed class RenownTuning
    {
        public int RenownPerThreat = 2;
        public int MaxThreat = 3;
        public int FirstFloor = 3;
        public int HpPerThreat = 1;
        public int BossHpPerThreat = 2;
        /// <summary>Monsters on those floors hit one harder per this much threat.</summary>
        public int ThreatPerExtraDamage = 2;
        /// <summary>Spikes, lava and bombs on those floors hurt one more per this much threat (D-041).</summary>
        public int ThreatPerExtraTrapDamage = 2;
    }

    /// <summary>Experience a run earns (D-027, rules §14).</summary>
    public sealed class XpTuning
    {
        public int PerMonster = 3;
        public int ForTheBoss = 30;
        public int PerFloor = 10;
        public int ForAWin = 20;
    }

    /// <summary>What a run pays out into the profile (D-025, rules §13).</summary>
    public sealed class TreasureTuning
    {
        /// <summary>Per reward drawn from a chest.</summary>
        public int CoinsPerChestReward = 4;
        /// <summary>For walking down the stairs off a floor.</summary>
        public int CoinsPerFloor = 10;
        /// <summary>Lord Blobert's hoard (D-026): gems price the special key.</summary>
        public int GemsForTheBoss = 5;
        /// <summary>
        /// Chances in percent that an item drops (D-040): gear is a find, not a given. A premium chest, opened with a bought
        /// special key, always holds one.
        /// </summary>
        public int ItemChanceBoss = 50;
        public int ItemChanceGreatChest = 35;
        public int ItemChancePremium = 100;
        /// <summary>A vault's great chest also holds a few gems.</summary>
        public int GemsPerGreatChest = 1;
        /// <summary>Rewards in a premium chest, opened with a special key.</summary>
        public int PremiumChestRewards = 5;
        /// <summary>The earliest and latest floor a premium chest is placed on (never the boss floor).</summary>
        public int PremiumFirstFloor = 2;
        /// <summary>The last floor a premium chest may appear on: every floor but Blobert's (D-059 widened it from 4).</summary>
        public int PremiumLastFloor = 6;
        /// <summary>What one bought potion ration is worth on the next run.</summary>
        public int PotionRationPotions = 1;
        /// <summary>What one bought heart token is worth on the next run.</summary>
        public int HeartTokenHearts = 2;
        /// <summary>The shop's other boosts for the next run (D-036), per one bought.</summary>
        public int ManaTonicMana = 2;
        public int StrengthElixirSlash = 1;
        public int FortuneScrollCoins = 3;
        public int WisdomScrollXp = 10;
    }

    public sealed class VaultTuning
    {
        public int MinEnemies = 2;
        public int MaxEnemies = 3;
        /// <summary>Rewards granted at once by a great chest.</summary>
        public int GreatChestRewards = 5;
        public int MinChests = 2;
        public int MaxChests = 3;
    }

    public sealed class RewardEntry
    {
        public RewardKind Kind;
        public int Amount;
        public int Weight;
    }
}
