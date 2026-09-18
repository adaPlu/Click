using System;

namespace ClickDungeon.Domain
{
    public static class BoardRules
    {
        public const int Size = 5;
        public const int CellCount = Size * Size;
    }

    public static class Versions
    {
        public const int SaveSchema = 1;
        // Bumped for chest quality: opening a chest now costs several turns instead of one (D-022).
        public const int Ruleset = 5;
        // Bumped for the tile-set features (lava, teleports, fountains, doors and vaults): floors from seed N now differ.
        public const int Generation = 2;
        /// <summary>The between-runs profile: coins, gems and the provisions bought with them (D-025).</summary>
        public const int ProfileSchema = 1;
    }

    /// <summary>Door blocks movement until it is opened; an open door leads into a vault room (D-018).</summary>
    public enum Terrain { Floor = 0, Wall = 1, Pit = 2, Door = 3 }

    public enum HazardKind { None = 0, Spikes = 1, Bomb = 2, Lava = 3 }

    /// <summary>Things standing on a floor tile (rules §11).</summary>
    public enum ContentKind { None = 0, Key = 1, Chest = 2, Potion = 3, Fountain = 4, Teleport = 5, PressurePlate = 6 }

    /// <summary>Player knowledge of a cell. Never goes backwards within a floor.</summary>
    public enum Knowledge { Unseen = 0, Sensed = 1, Revealed = 2 }

    [Flags]
    public enum Clue
    {
        None = 0,
        Enemy = 1,
        Danger = 2,
        /// <summary>The floor's key.</summary>
        Objective = 4,
        Treasure = 8,
        Safe = 16,
        /// <summary>The exit (the stairs down).</summary>
        Exit = 32,
        /// <summary>Something to use: a vault door, a pressure plate or a teleport pad.</summary>
        Feature = 64,
    }

    public enum IntentKind { None = 0, Attack, Move, Fire, Rest, Recover, Summon, Slam, PuffUp }

    public enum EnemyMode { Normal = 0, Puffed, Deflated }

    public enum RunStatus { InProgress = 0, Won, Lost }

    /// <summary>Run difficulty tier. Medium is 0 so saves made before tiers existed load unchanged.</summary>
    public enum Difficulty { Medium = 0, Easy = 1, Hardcore = 2 }

    /// <summary>
    /// How many taps a chest takes to open (D-022): Common 2, Rare 3, Epic 4. Common is 0 so saves made before
    /// quality existed load as Common.
    /// </summary>
    public enum ChestQuality { Common = 0, Rare = 1, Epic = 2 }

    /// <summary>
    /// How the hero moves (D-021). Free Roam is the default: click any open tile, with no hints about unrevealed ones.
    /// Step by Step restricts movement to the eight neighbouring tiles and hints at nearby tiles. Enemies behave the same
    /// in both: melee monsters attack from a neighbouring tile, ranged monsters and the boss from a distance.
    /// </summary>
    public enum MovementMode { Free = 0, Step = 1 }

    public enum CommandKind { Move = 0, Wait, Slash, Shield, Dash, Potion, Interact }

    public enum RewardKind { Potion = 0, MaxHp, SlashDamage }
}
