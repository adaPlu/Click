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
        public int ShieldCooldown;
        public int DashCooldown;
        public int DashDistance;
        public int RevealRadius;
        public int SenseRadius;
    }

    /// <summary>Who the hero is (name, personality). Several identities may share a class.</summary>
    public sealed class HeroIdentityDefinition
    {
        public string Id;
        public string DisplayName;
        public string ClassId;
        public string Tagline;
    }

    public enum EnemyBehavior { Chaser, SlowChaser, Lane, Boss }

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
        public int DeflatedDamageMultiplier = 2;
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
