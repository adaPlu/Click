using System;
using System.Collections.Generic;

namespace ClickDungeon.Domain
{
    // Runtime state is plain [Serializable] classes with public fields only.
    // The save system serializes fields, so do not add auto-properties holding data.

    [Serializable]
    public sealed class CellState
    {
        public Terrain Terrain;
        public bool IsExit;
        public HazardKind Hazard;
        /// <summary>-1 = bomb not armed.</summary>
        public int BombFuse = -1;
        public ContentKind Content;
        public bool ChestOpened;
        /// <summary>Vault chest: grants three rewards when it finally opens (D-018).</summary>
        public bool GreatChest;
        /// <summary>How many taps this chest takes to open (D-022).</summary>
        public ChestQuality Quality;
        /// <summary>Taps already spent on this chest. Each one is a full player action.</summary>
        public int ChestTaps;
        /// <summary>A premium chest: placed by a special key the hero carried in, and opened only with one (D-026).</summary>
        public bool Premium;
        /// <summary>Set once a door has been opened by a pressure plate, or a fountain or teleport pad has been used.</summary>
        public bool Used;
        public Knowledge Knowledge;

        public bool BombArmed => Hazard == HazardKind.Bomb && BombFuse >= 0;
        public bool IsClosedChest => Content == ContentKind.Chest && !ChestOpened;
        public bool IsOpenDoor => Terrain == Terrain.Door && Used;
        public bool IsLockedDoor => Terrain == Terrain.Door && !Used;
    }

    [Serializable]
    public sealed class EnemyState
    {
        public int Id;
        public string DefId;
        public GridPos Pos;
        public int Hp;
        public int MaxHp;
        public bool Awake;
        /// <summary>Woke or was summoned this turn: declares but does not act (first contact).</summary>
        public bool JustWoken;
        public bool Staggered;
        public Intent Intent;
        /// <summary>Behaviour-specific step counter (slime rest cycle, boss script index).</summary>
        public int ActionCounter;
        public EnemyMode Mode;
        public int ModeTurns;
    }

    [Serializable]
    public sealed class HeroState
    {
        public string IdentityId;
        public string ClassId;
        public GridPos Pos;
        public int Hp;
        public int MaxHp;
        public int SlashDamage;
        public int Potions;
        public bool HasKey;
        /// <summary>Special keys carried in from the profile (D-026). Each opens one premium chest; unused ones go back.</summary>
        public int SpecialKeys;
        public bool Guard;
        public int ShieldCooldown;
        public int DashCooldown;
    }

    [Serializable]
    public sealed class FloorState
    {
        public int FloorIndex;
        public bool IsBossFloor;
        /// <summary>A vault room behind a door: its exit leads back to the floor the hero came from (D-018).</summary>
        public bool IsVault;
        public string TemplateId;
        public int Transform;
        public int AttemptIndex;
        public GridPos Start;
        public GridPos Exit;
        public bool ExitUnlocked;
        public CellState[] Cells;
        public List<EnemyState> Enemies = new List<EnemyState>();
        public int NextActorId = 1;

        public CellState this[GridPos p] => Cells[p.Index];

        public EnemyState EnemyAt(GridPos p)
        {
            for (int i = 0; i < Enemies.Count; i++)
                if (Enemies[i].Pos == p) return Enemies[i];
            return null;
        }

        public EnemyState EnemyById(int id)
        {
            for (int i = 0; i < Enemies.Count; i++)
                if (Enemies[i].Id == id) return Enemies[i];
            return null;
        }

        public static FloorState CreateEmpty()
        {
            var floor = new FloorState { Cells = new CellState[BoardRules.CellCount], Start = GridPos.Invalid, Exit = GridPos.Invalid };
            for (int i = 0; i < floor.Cells.Length; i++) floor.Cells[i] = new CellState();
            return floor;
        }
    }

    [Serializable]
    public sealed class RewardRecord
    {
        public string TransactionId;
        public RewardKind Kind;
        public int Amount;
        public int FloorIndex;
        public int Turn;
    }

    [Serializable]
    public sealed class RunState
    {
        public int SaveSchemaVersion = Versions.SaveSchema;
        public int RulesetVersion = Versions.Ruleset;
        public int ContentCatalogVersion;
        public int GenerationVersion = Versions.Generation;
        public ulong RunSeed;
        public int FloorCount;
        public Difficulty Difficulty;
        public MovementMode Movement;
        public int Turn;
        public RunStatus Status;
        public HeroState Hero;
        public FloorState Floor;
        /// <summary>While inside a vault: the floor to return to, kept exactly as it was left (D-018).</summary>
        public FloorState OuterFloor;
        /// <summary>The door cell the hero stepped through, where they come back out.</summary>
        public GridPos ReturnPos = GridPos.Invalid;
        public List<RewardRecord> Rewards = new List<RewardRecord>();
        /// <summary>Treasure carried out of the dungeon (D-025). Banked into the profile when the run ends.</summary>
        public int CoinsFound;
        public int GemsFound;
        /// <summary>Experience earned this run (D-027). Banked into the profile, like coins, when the run ends.</summary>
        public int XpEarned;
        /// <summary>Extra coins per chest reward, from the Lucky talent. Set when the run starts and never changed.</summary>
        public int BonusCoinsPerChestReward;
        /// <summary>Items picked up this run (D-028), banked into the inventory when the run ends. They do not change this run.</summary>
        public List<string> ItemsFound = new List<string>();
        /// <summary>From equipment: extra healing per potion, and extra XP per floor walked down.</summary>
        public int PotionHealBonus;
        public int BonusXpPerFloor;
        /// <summary>Turns taken off the class's shield and dash cooldowns by talents (never below one turn).</summary>
        public int ShieldCooldownCut;
        public int DashCooldownCut;
        /// <summary>Counted for the crown's achievements (D-030) and banked with the rest when the run ends.</summary>
        public int MonstersSlain;
        public int ChestsOpened;
        /// <summary>Premium chests still to be placed on the floors ahead (D-026), one per special key carried in.</summary>
        public int PremiumChestsToPlace;

        public bool HasReward(string transactionId)
        {
            for (int i = 0; i < Rewards.Count; i++)
                if (Rewards[i].TransactionId == transactionId) return true;
            return false;
        }
    }
}
