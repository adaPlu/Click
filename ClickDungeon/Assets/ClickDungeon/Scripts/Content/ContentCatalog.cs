using System;
using System.Collections.Generic;
using ClickDungeon.Domain;

namespace ClickDungeon.Content
{
    /// <summary>
    /// All gameplay content, addressed by stable string ids. Gate 1 content is authored in code
    /// (decision D-012); ids are the contract saves depend on.
    /// </summary>
    public sealed class ContentCatalog
    {
        public const string DefaultHeroId = "sir_clickington";
        /// <summary>Guard used for a vault on a floor whose profile has no enemy pool of its own.</summary>
        public const string DefaultVaultEnemyId = "goblin";

        public int Version = 1;
        public int RunFloorCount = 5;
        public HazardTuning Hazards = new HazardTuning();
        /// <summary>What a vault room behind a door holds (D-018).</summary>
        public VaultTuning Vault = new VaultTuning();
        public TreasureTuning Treasure = new TreasureTuning();
        public XpTuning Xp = new XpTuning();
        /// <summary>The tier this catalog was built for. Every number in it already includes that tier's adjustments.</summary>
        public Difficulty Difficulty = Difficulty.Medium;
        /// <summary>HP restored when the hero arrives on the next floor.</summary>
        public int FloorClearHeal;

        public readonly Dictionary<string, HeroClassDefinition> HeroClasses = new Dictionary<string, HeroClassDefinition>();
        public readonly Dictionary<string, HeroIdentityDefinition> HeroIdentities = new Dictionary<string, HeroIdentityDefinition>();
        public readonly Dictionary<string, EnemyDefinition> Enemies = new Dictionary<string, EnemyDefinition>();
        public readonly List<TopologyTemplate> Templates = new List<TopologyTemplate>();
        public readonly List<FloorProfile> FloorProfiles = new List<FloorProfile>();
        public readonly List<RewardEntry> ChestRewards = new List<RewardEntry>();
        /// <summary>Equipment, in drop-table order (D-028). A drop picks one by hash; a duplicate becomes coins at banking.</summary>
        public readonly List<ItemDefinition> Items = new List<ItemDefinition>();
        public int DuplicateItemCoins = 25;
        /// <summary>The daily reward's week (D-029): day one after a missed day, and again after day seven.</summary>
        public readonly List<RewardBundle> DailyRewards = new List<RewardBundle>();
        /// <summary>The crown's goals (D-030), in the order the crown lists them.</summary>
        public readonly List<AchievementDefinition> Achievements = new List<AchievementDefinition>();
        /// <summary>Attached to the first letter a new profile receives.</summary>
        public RewardBundle WelcomeGift = new RewardBundle { Label = "a potion ration", PotionRations = 1 };

        static void Achieve(ContentCatalog c, string id, string title, string description, AchievementStat stat, int target, RewardBundle reward) =>
            c.Achievements.Add(new AchievementDefinition { Id = id, Title = title, Description = description, Stat = stat, Target = target, Reward = reward });

        public ItemDefinition Item(string id)
        {
            foreach (var item in Items)
                if (item.Id == id) return item;
            return null;
        }

        /// <summary>
        /// Rewards a regular chest grants when it opens, indexed by <see cref="ChestQuality"/> (D-022). Better chests cost
        /// more taps, so they pay one reward per tap: Common 2, Rare 3, Epic 4.
        /// </summary>
        public readonly int[] ChestRewardsByQuality = { 2, 3, 4 };
        public readonly Dictionary<Difficulty, DifficultyDefinition> Difficulties = new Dictionary<Difficulty, DifficultyDefinition>();

        public HeroClassDefinition HeroClass(string id) => Get(HeroClasses, id, "hero class");
        public HeroIdentityDefinition HeroIdentity(string id) => Get(HeroIdentities, id, "hero identity");
        public EnemyDefinition Enemy(string id) => Get(Enemies, id, "enemy");
        public bool HasEnemy(string id) => id != null && Enemies.ContainsKey(id);

        public DifficultyDefinition DifficultyInfo(Difficulty id) =>
            Difficulties.TryGetValue(id, out var info) ? info : throw new KeyNotFoundException($"Unknown difficulty '{id}'.");

        /// <summary>
        /// The default content tuned for <paramref name="difficulty"/> with this catalog's own tier table (so tuned tiers stay tuned);
        /// this catalog when it already matches. Other edits to this catalog (floor count, hazards) are not carried over.
        /// </summary>
        public ContentCatalog ForDifficulty(Difficulty difficulty)
        {
            if (difficulty == Difficulty) return this;
            var c = BuildBase();
            foreach (var tier in Difficulties) c.Difficulties[tier.Key] = tier.Value;
            c.ApplyDifficulty(c.DifficultyInfo(difficulty));
            return c;
        }

        public FloorProfile ProfileFor(int floorIndex)
        {
            FloorProfile fallback = null;
            foreach (var profile in FloorProfiles)
            {
                if (profile.FloorIndex == floorIndex) return profile;
                if (!profile.IsBoss && (fallback == null || profile.FloorIndex > fallback.FloorIndex)) fallback = profile;
            }
            return fallback ?? throw new InvalidOperationException("No floor profiles defined.");
        }

        static T Get<T>(Dictionary<string, T> table, string id, string kind)
        {
            if (id != null && table.TryGetValue(id, out var value)) return value;
            throw new KeyNotFoundException($"Unknown {kind} id '{id}'.");
        }

        public static ContentCatalog CreateDefault() => CreateDefault(Difficulty.Medium);

        /// <summary>Default content tuned for one tier. Every call builds fresh definitions, so tiers never share state.</summary>
        public static ContentCatalog CreateDefault(Difficulty difficulty)
        {
            var c = BuildBase();
            c.ApplyDifficulty(c.DifficultyInfo(difficulty));
            return c;
        }

        /// <summary>Default content with custom numbers for one tier, for balance sweeps. Other tiers keep their defaults.</summary>
        public static ContentCatalog CreateTuned(DifficultyDefinition tuning)
        {
            var c = BuildBase();
            c.Difficulties[tuning.Id] = tuning;
            c.ApplyDifficulty(tuning);
            return c;
        }

        /// <summary>Untuned base content with every tier registered.</summary>
        static ContentCatalog BuildBase()
        {
            var c = new ContentCatalog();

            c.HeroClasses["knight"] = new HeroClassDefinition
            {
                Id = "knight", DisplayName = "Knight",
                MaxHp = 10, SlashDamage = 2, StartingPotions = 2, PotionHeal = 4,
                ShieldCooldown = 3, DashCooldown = 3, DashDistance = 2,
                RevealRadius = 1, SenseRadius = 2,
            };
            c.HeroIdentities[DefaultHeroId] = new HeroIdentityDefinition
            {
                Id = DefaultHeroId, DisplayName = "Sir Clickington", ClassId = "knight", Tagline = "Brave. Loyal. Clickable.",
            };

            // The Paladin trades reach for staying power: more hearts, a shield that comes back sooner and a stronger
            // potion, against a dash of a single tile that also recharges slower (rules §5.1). A softer slash was tried
            // first and made boss fights drag: the sighted bot won 24 of 40 on Blobert's Wrath against the Knight's 40.
            c.HeroClasses["paladin"] = new HeroClassDefinition
            {
                Id = "paladin", DisplayName = "Paladin",
                MaxHp = 12, SlashDamage = 2, StartingPotions = 2, PotionHeal = 6,
                ShieldCooldown = 2, DashCooldown = 4, DashDistance = 1,
                RevealRadius = 1, SenseRadius = 2,
            };
            c.HeroIdentities["dawnward"] = new HeroIdentityDefinition
            {
                Id = "dawnward", DisplayName = "Dawnward", ClassId = "paladin", Tagline = "Steadfast. Shielded. Unshaken.",
            };

            c.Items.Add(new ItemDefinition { Id = "steel_sword", DisplayName = "Steel Sword", Slot = ItemSlot.Weapon, Effect = "+1 slash damage", SlashDamage = 1 });
            c.Items.Add(new ItemDefinition { Id = "lucky_wand", DisplayName = "Lucky Wand", Slot = ItemSlot.Weapon, Effect = "+3 coins for every chest reward", CoinsPerChestReward = 3 });
            c.Items.Add(new ItemDefinition { Id = "iron_shield", DisplayName = "Iron Shield", Slot = ItemSlot.Shield, Effect = "+1 max heart", MaxHp = 1 });
            c.Items.Add(new ItemDefinition { Id = "gilded_shield", DisplayName = "Gilded Shield", Slot = ItemSlot.Shield, Effect = "Shield recharges 1 turn sooner", ShieldCooldownCut = 1 });
            c.Items.Add(new ItemDefinition { Id = "iron_cuirass", DisplayName = "Iron Cuirass", Slot = ItemSlot.Armor, Effect = "+1 max heart", MaxHp = 1 });
            c.Items.Add(new ItemDefinition { Id = "royal_plate", DisplayName = "Royal Plate", Slot = ItemSlot.Armor, Effect = "+2 max hearts", MaxHp = 2 });
            c.Items.Add(new ItemDefinition { Id = "swift_boots", DisplayName = "Swift Boots", Slot = ItemSlot.Boots, Effect = "Dash recharges 1 turn sooner", DashCooldownCut = 1 });
            c.Items.Add(new ItemDefinition { Id = "healing_charm", DisplayName = "Healing Charm", Slot = ItemSlot.Trinket, Effect = "Potions heal 2 more", PotionHeal = 2 });
            c.Items.Add(new ItemDefinition { Id = "scholars_ring", DisplayName = "Scholar's Ring", Slot = ItemSlot.Trinket, Effect = "+5 XP for every floor walked down", XpPerFloor = 5 });

            c.DailyRewards.Add(new RewardBundle { Label = "30 coins", Coins = 30 });
            c.DailyRewards.Add(new RewardBundle { Label = "a potion ration", PotionRations = 1 });
            c.DailyRewards.Add(new RewardBundle { Label = "50 coins", Coins = 50 });
            c.DailyRewards.Add(new RewardBundle { Label = "a heart token", HeartTokens = 1 });
            c.DailyRewards.Add(new RewardBundle { Label = "80 coins", Coins = 80 });
            c.DailyRewards.Add(new RewardBundle { Label = "15 gems", Gems = 15 });
            c.DailyRewards.Add(new RewardBundle { Label = "a special key", SpecialKeys = 1 });

            Achieve(c, "first_steps", "First Steps", "Finish a run", AchievementStat.RunsFinished, 1, new RewardBundle { Label = "25 coins", Coins = 25 });
            Achieve(c, "deep_diver", "Deep Diver", "Reach floor 3", AchievementStat.DeepestFloor, 3, new RewardBundle { Label = "40 coins", Coins = 40 });
            Achieve(c, "into_the_lair", "Into the Lair", "Reach floor 5", AchievementStat.DeepestFloor, 5, new RewardBundle { Label = "a potion ration", PotionRations = 1 });
            Achieve(c, "blobert_bested", "Blobert Bested", "Defeat Lord Blobert", AchievementStat.RunsWon, 1, new RewardBundle { Label = "25 gems", Gems = 25 });
            Achieve(c, "champion", "Champion", "Win 5 runs", AchievementStat.RunsWon, 5, new RewardBundle { Label = "a special key", SpecialKeys = 1 });
            Achieve(c, "monster_hunter", "Monster Hunter", "Slay 25 monsters", AchievementStat.MonstersSlain, 25, new RewardBundle { Label = "50 coins", Coins = 50 });
            Achieve(c, "monster_slayer", "Monster Slayer", "Slay 100 monsters", AchievementStat.MonstersSlain, 100, new RewardBundle { Label = "15 gems", Gems = 15 });
            Achieve(c, "treasure_seeker", "Treasure Seeker", "Open 20 chests", AchievementStat.ChestsOpened, 20, new RewardBundle { Label = "50 coins", Coins = 50 });
            Achieve(c, "hoarder", "Hoarder", "Carry out 1,000 coins", AchievementStat.CoinsEarned, 1000, new RewardBundle { Label = "20 gems", Gems = 20 });
            Achieve(c, "seasoned", "Seasoned", "Reach level 5", AchievementStat.Level, 5, new RewardBundle { Label = "a heart token", HeartTokens = 1 });
            Achieve(c, "collector", "Collector", "Own 5 pieces of gear", AchievementStat.ItemsOwned, 5, new RewardBundle { Label = "15 gems", Gems = 15 });

            AddEnemy(c, new EnemyDefinition { Id = "goblin", DisplayName = "Goblin", Behavior = EnemyBehavior.Chaser, MaxHp = 3, Damage = 2 });
            AddEnemy(c, new EnemyDefinition { Id = "crowned_slime", DisplayName = "Crowned Slime", Behavior = EnemyBehavior.SlowChaser, MaxHp = 5, Damage = 3 });
            AddEnemy(c, new EnemyDefinition { Id = "fire_imp", DisplayName = "Fire Imp", Behavior = EnemyBehavior.Lane, MaxHp = 2, Damage = 2, Range = 3 });
            AddEnemy(c, new EnemyDefinition { Id = "slimelet", DisplayName = "Slimelet", Behavior = EnemyBehavior.Chaser, MaxHp = 1, Damage = 1 });
            AddEnemy(c, new EnemyDefinition
            {
                Id = "lord_blobert", DisplayName = "Lord Blobert", Behavior = EnemyBehavior.Boss, IsBoss = true,
                MaxHp = 12, Damage = 2, SlamDamage = 4, PuffTurns = 2, SummonId = "slimelet",
            });

            AddTemplate(c, "pillars", false,
                ".....",
                ".#.#.",
                ".....",
                ".#.#.",
                ".....");
            AddTemplate(c, "split_hall", false,
                "..#..",
                "..#..",
                ".....",
                "..#..",
                "..#..");
            AddTemplate(c, "moat", false,
                ".....",
                ".ooo.",
                ".o.o.",
                ".o...",
                ".....");
            AddTemplate(c, "zigzag", false,
                "...#.",
                ".#...",
                ".#.#.",
                "...#.",
                ".#...");
            AddTemplate(c, "corners", false,
                "..#..",
                ".....",
                "#.o.#",
                ".....",
                "..#..");
            AddTemplate(c, "gallery", false,
                ".#...",
                ".#.o.",
                ".....",
                ".o.#.",
                "...#.");
            AddTemplate(c, "cross_roads", false,
                "#.#.#",
                ".....",
                "#.#.#",
                ".....",
                "#.#.#");
            AddTemplate(c, "switchback", false,
                ".....",
                ".###.",
                "...#.",
                ".#...",
                ".....");
            AddTemplate(c, "twin_rooms", false,
                "...#.",
                "...#.",
                ".#...",
                ".#...",
                ".#...");
            AddTemplate(c, "pit_bridge", false,
                ".....",
                ".o.o.",
                "..o..",
                ".o.o.",
                ".....");
            AddTemplate(c, "vault", false,
                ".....",
                ".###.",
                ".#.#.",
                ".#.#.",
                ".....");
            AddTemplate(c, "hourglass", false,
                ".....",
                "#...#",
                "##.##",
                "#...#",
                ".....");
            AddTemplate(c, "ring_road", false,
                ".....",
                ".#o#.",
                ".o#o.",
                ".#o#.",
                ".....");
            AddTemplate(c, "comb", false,
                ".....",
                ".#.#.",
                ".#.#.",
                ".#.#.",
                ".#.#.");
            AddTemplate(c, "broken_diagonal", false,
                "....#",
                "...#.",
                ".....",
                ".#...",
                "#....");

            AddTemplate(c, "blobert_court", true,
                ".....",
                ".o.o.",
                ".....",
                ".o.o.",
                ".....");
            AddTemplate(c, "blobert_throne", true,
                ".....",
                ".....",
                "..#..",
                ".....",
                ".....");
            AddTemplate(c, "blobert_pits", true,
                "o...o",
                ".....",
                "..o..",
                ".....",
                "o...o");

            c.FloorProfiles.Add(new FloorProfile
            {
                FloorIndex = 1, Name = "The Upper Halls", EnemyPool = new[] { "goblin" }, MinEnemies = 1, MaxEnemies = 2,
                MinSpikes = 1, MaxSpikes = 2, MinBombs = 0, MaxBombs = 1, Chests = 1,
            });
            c.FloorProfiles.Add(new FloorProfile
            {
                FloorIndex = 2, Name = "The Damp Cellars", EnemyPool = new[] { "goblin", "goblin", "crowned_slime" }, MinEnemies = 2, MaxEnemies = 2,
                MinSpikes = 1, MaxSpikes = 2, MinBombs = 1, MaxBombs = 1, Chests = 1, MinPotions = 0, MaxPotions = 1,
                Vault = true, Fountains = 1,
            });
            c.FloorProfiles.Add(new FloorProfile
            {
                FloorIndex = 3, Name = "The Ember Vaults", EnemyPool = new[] { "goblin", "fire_imp", "crowned_slime" }, MinEnemies = 2, MaxEnemies = 3,
                MinSpikes = 1, MaxSpikes = 2, MinBombs = 1, MaxBombs = 1, Chests = 1, MinPotions = 0, MaxPotions = 1,
                Vault = true, MinLava = 1, MaxLava = 2, Teleports = true,
            });
            c.FloorProfiles.Add(new FloorProfile
            {
                FloorIndex = 4, Name = "The Locked Depths", EnemyPool = new[] { "goblin", "fire_imp", "fire_imp", "crowned_slime" }, MinEnemies = 3, MaxEnemies = 3,
                MinSpikes = 2, MaxSpikes = 2, MinBombs = 1, MaxBombs = 2, Chests = 1, MinPotions = 1, MaxPotions = 1,
                Vault = true, MinLava = 1, MaxLava = 2, Teleports = true, Fountains = 1,
            });
            c.FloorProfiles.Add(new FloorProfile
            {
                FloorIndex = 5, Name = "Blobert's Court", IsBoss = true, BossId = "lord_blobert", MinPotions = 1, MaxPotions = 1, MinExitDistance = 3,
            });

            // Chests cost 2-4 turns each (D-022 amendment, measured with ChestWorth). Potions stay single: at two per draw
            // a looting run ended with ~11 unused, so that weight went to max HP instead.
            c.ChestRewards.Add(new RewardEntry { Kind = RewardKind.Potion, Amount = 1, Weight = 2 });
            c.ChestRewards.Add(new RewardEntry { Kind = RewardKind.MaxHp, Amount = 3, Weight = 3 });
            c.ChestRewards.Add(new RewardEntry { Kind = RewardKind.SlashDamage, Amount = 1, Weight = 1 });

            c.Difficulties[Difficulty.Easy] = new DifficultyDefinition
            {
                Id = Difficulty.Easy, DisplayName = "Squire's Stroll", Tagline = "More hearts, softer hits and a breather on every stair.",
                HeroMaxHp = 4, StartingPotions = 1, EnemyDamage = -1, HazardDamage = -1, BossHp = -2, BossSlamDamage = -1, FloorClearHeal = 3,
            };
            c.Difficulties[Difficulty.Medium] = new DifficultyDefinition
            {
                Id = Difficulty.Medium, DisplayName = "Knight's Trial", Tagline = "The dungeon as designed. Click carefully.",
                // Tuned for click-to-reveal (D-023), where every click is a blind step: traps blunted by one, no extra
                // monsters or missing potions. Blind novice AutoPlayer wins ~65% (rules §10.2).
                HazardDamage = -1,
            };
            c.Difficulties[Difficulty.Hardcore] = new DifficultyDefinition
            {
                Id = Difficulty.Hardcore, DisplayName = "Blobert's Wrath", Tagline = "Tougher monsters, a mightier Blobert, one potion. No mercy.",
                // Tuned for click-to-reveal (D-023): blind traps and bumped monsters already hurt, so the pressure moved to
                // Lord Blobert. Blind novice AutoPlayer wins ~23%, casual ~53% (rules §10.2).
                StartingPotions = -1, EnemyHp = 1, BossHp = 4, BossSlamDamage = 1,
            };
            return c;
        }

        void ApplyDifficulty(DifficultyDefinition d)
        {
            Difficulty = d.Id;
            FloorClearHeal = Math.Max(0, d.FloorClearHeal);

            foreach (var hero in HeroClasses.Values)
            {
                hero.MaxHp = Math.Max(1, hero.MaxHp + d.HeroMaxHp);
                hero.StartingPotions = Math.Max(0, hero.StartingPotions + d.StartingPotions);
            }
            foreach (var enemy in Enemies.Values)
            {
                enemy.MaxHp = Math.Max(1, enemy.MaxHp + (enemy.IsBoss ? d.BossHp : d.EnemyHp));
                enemy.Damage = Math.Max(1, enemy.Damage + d.EnemyDamage);
                if (enemy.IsBoss) enemy.SlamDamage = Math.Max(1, enemy.SlamDamage + d.BossSlamDamage);
            }
            Hazards.SpikeDamage = Math.Max(1, Hazards.SpikeDamage + d.HazardDamage);
            Hazards.BombDamage = Math.Max(1, Hazards.BombDamage + d.HazardDamage);
            Hazards.LavaDamage = Math.Max(1, Hazards.LavaDamage + d.HazardDamage);
            foreach (var profile in FloorProfiles)
            {
                if (profile.IsBoss || profile.MaxEnemies <= 0) continue;
                // A minimum of 1 or more stays at least 1, and the maximum stays at least 1. A minimum of 0 stays 0.
                profile.MinEnemies = Math.Max(Math.Min(1, profile.MinEnemies), profile.MinEnemies + d.ExtraEnemies);
                profile.MaxEnemies = Math.Max(Math.Max(1, profile.MinEnemies), profile.MaxEnemies + d.ExtraEnemies);
            }
        }

        static void AddEnemy(ContentCatalog c, EnemyDefinition def) => c.Enemies.Add(def.Id, def);

        static void AddTemplate(ContentCatalog c, string id, bool bossArena, params string[] rows) =>
            c.Templates.Add(new TopologyTemplate { Id = id, BossArena = bossArena, Rows = rows });
    }
}
