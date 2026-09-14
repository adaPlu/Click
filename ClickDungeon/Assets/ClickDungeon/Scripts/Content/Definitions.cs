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
    }

    public sealed class RewardEntry
    {
        public RewardKind Kind;
        public int Amount;
        public int Weight;
    }
}
