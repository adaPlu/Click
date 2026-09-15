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

        public int Version = 1;
        public int RunFloorCount = 5;
        public HazardTuning Hazards = new HazardTuning();
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
        public readonly Dictionary<Difficulty, DifficultyDefinition> Difficulties = new Dictionary<Difficulty, DifficultyDefinition>();

        public HeroClassDefinition HeroClass(string id) => Get(HeroClasses, id, "hero class");
        public HeroIdentityDefinition HeroIdentity(string id) => Get(HeroIdentities, id, "hero identity");
        public EnemyDefinition Enemy(string id) => Get(Enemies, id, "enemy");
        public bool HasEnemy(string id) => id != null && Enemies.ContainsKey(id);

        public DifficultyDefinition DifficultyInfo(Difficulty id) =>
            Difficulties.TryGetValue(id, out var info) ? info : throw new KeyNotFoundException($"Unknown difficulty '{id}'.");

        /// <summary>
        /// This catalog's content tuned for <paramref name="difficulty"/>, using this catalog's own tier table (so tuned catalogs stay
        /// tuned); this catalog when it already matches.
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
            });
            c.FloorProfiles.Add(new FloorProfile
            {
                FloorIndex = 3, Name = "The Ember Vaults", EnemyPool = new[] { "goblin", "fire_imp", "crowned_slime" }, MinEnemies = 2, MaxEnemies = 3,
                MinSpikes = 1, MaxSpikes = 2, MinBombs = 1, MaxBombs = 1, Chests = 1, MinPotions = 0, MaxPotions = 1,
            });
            c.FloorProfiles.Add(new FloorProfile
            {
                FloorIndex = 4, Name = "The Locked Depths", EnemyPool = new[] { "goblin", "fire_imp", "fire_imp", "crowned_slime" }, MinEnemies = 3, MaxEnemies = 3,
                MinSpikes = 2, MaxSpikes = 2, MinBombs = 1, MaxBombs = 2, Chests = 1, MinPotions = 1, MaxPotions = 1,
            });
            c.FloorProfiles.Add(new FloorProfile
            {
                FloorIndex = 5, Name = "Blobert's Court", IsBoss = true, BossId = "lord_blobert", MinPotions = 1, MaxPotions = 1, MinExitDistance = 3,
            });

            c.ChestRewards.Add(new RewardEntry { Kind = RewardKind.Potion, Amount = 1, Weight = 3 });
            c.ChestRewards.Add(new RewardEntry { Kind = RewardKind.MaxHp, Amount = 2, Weight = 2 });
            c.ChestRewards.Add(new RewardEntry { Kind = RewardKind.SlashDamage, Amount = 1, Weight = 1 });

            c.Difficulties[Difficulty.Easy] = new DifficultyDefinition
            {
                Id = Difficulty.Easy, DisplayName = "Squire's Stroll", Tagline = "More hearts, softer hits and a breather on every stair.",
                HeroMaxHp = 4, StartingPotions = 1, EnemyDamage = -1, HazardDamage = -1, BossHp = -2, BossSlamDamage = -1, FloorClearHeal = 3,
            };
            c.Difficulties[Difficulty.Medium] = new DifficultyDefinition
            {
                Id = Difficulty.Medium, DisplayName = "Knight's Trial", Tagline = "The dungeon as designed. Read every tile.",
            };
            c.Difficulties[Difficulty.Hardcore] = new DifficultyDefinition
            {
                Id = Difficulty.Hardcore, DisplayName = "Blobert's Wrath", Tagline = "Tougher monsters, meaner traps, fewer potions. No mercy.",
                StartingPotions = -1, EnemyHp = 1, EnemyDamage = 1, HazardDamage = 1, BossHp = 4, BossSlamDamage = 1, ExtraEnemies = 1,
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
            foreach (var profile in FloorProfiles)
            {
                if (profile.IsBoss || profile.MaxEnemies <= 0) continue;
                // A floor that had enemies keeps at least one, whatever its original minimum.
                profile.MinEnemies = Math.Max(Math.Min(1, profile.MinEnemies), profile.MinEnemies + d.ExtraEnemies);
                profile.MaxEnemies = Math.Max(Math.Max(1, profile.MinEnemies), profile.MaxEnemies + d.ExtraEnemies);
            }
        }

        static void AddEnemy(ContentCatalog c, EnemyDefinition def) => c.Enemies.Add(def.Id, def);

        static void AddTemplate(ContentCatalog c, string id, bool bossArena, params string[] rows) =>
            c.Templates.Add(new TopologyTemplate { Id = id, BossArena = bossArena, Rows = rows });
    }
}
