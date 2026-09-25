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
        // Bumped for renown's threat, rarer gear and Blobert's second slam (D-040); 9 for the first expansion monsters (D-058);
        // 10 for the second wave, the act bosses and the act-clear heal (D-061, D-062); 11 for the six new classes and
        // their rules (D-063); 12 for the vault room being nine tiles around the door it was entered by (D-064); 13 for
        // renown's threat arriving with the depth instead of landing whole on floor 3 (D-067).
        public const int Ruleset = 13;
        // Bumped for the tile-set features (lava, teleports, fountains, doors and vaults): floors from seed N now differ.
        // 3: the first expansion monsters join the floors' enemy pools (D-058), so floors from seed N differ again.
        // 4: twenty floors in acts, the second wave in the pools and the key warden holding keys (D-062). A seven-floor save
        // is refused rather than resumed into a dungeon that no longer matches it.
        public const int Generation = 4;
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

    /// <summary>Charge, Throw and Reassemble belong to the first expansion monsters (D-058): boar, bomber and skeleton.</summary>
    public enum IntentKind { None = 0, Attack, Move, Fire, Rest, Recover, Summon, Slam, PuffUp, Charge, Throw, Reassemble, Web, Vanish }

    /// <summary>Bones: a Skeleton Warrior that fell once and lies waiting to stand up again (D-058).</summary>
    /// <summary>Enraged: the Goblin Brute King below half his hearts, every blow one harder (D-062).</summary>
    public enum EnemyMode { Normal = 0, Puffed, Deflated, Bones, Enraged }

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

    /// <summary>Where an item is worn (D-028). One item per slot.</summary>
    /// <summary>Where an item is worn (D-028). Helmet came with the shop (D-036); the numbers never change, saves hold names.</summary>
    public enum ItemSlot { Weapon = 0, Shield, Armor, Boots, Trinket, Helmet }

    /// <summary>
    /// What a class talent does in play (D-037). Talents are data: each names one effect and how much a rank adds. Stat
    /// effects become the hero's starting numbers; the rest are read by the rules through <c>RunState.Perk</c>.
    /// </summary>
    public enum TalentEffect
    {
        // Starting numbers
        MaxHearts, DashCostCut, ShieldCostCut, CoinsPerChestReward, PotionHeal,
        // Rules the simulation reads
        OpeningStrike, Cleave, Executioner, Relentless, Riposte, Bastion, ChestTapCut, SecondWind,
        Judgement, Consecrate, Dawnstrike, WrathOfDawn, HolyBulwark, Unyielding, DivineShield, Prayer, GuidingLight, Sanctified,
        // Class rules (D-063): each class starts every run with its own, and its talents may add to it. Appended, so no
        // saved perk changes meaning.
        Ambush, Reach, Knockback, Longshot, Sanctuary, Rage, Drone,
        // The new classes' talents (D-063).
        MaxMana, Eviscerate, Dodge, Pickpocket, Fireball, PiercingArrow, PinningShot, Hawkeye, Bloodlust, DroneRange, ArcChain,
        // The Paladin's holy wrath against the risen (D-072).
        HolyWrath,
    }

    /// <summary>How rare an item is (D-036): its drop weight, its shop price and the colour of its frame.</summary>
    public enum ItemRarity { Common = 0, Uncommon, Rare, Epic, Legendary }
}
