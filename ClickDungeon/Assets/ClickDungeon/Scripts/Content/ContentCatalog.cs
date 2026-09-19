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

        static void Gear(ContentCatalog c, string id, string name, ItemSlot slot, ItemRarity rarity, int slash = 0, int hearts = 0,
            int mana = 0, int heal = 0, int dashCut = 0, int coins = 0, int xp = 0)
        {
            var parts = new List<string>();
            if (slash > 0) parts.Add($"+{slash} slash damage");
            if (hearts > 0) parts.Add($"+{hearts} max heart{(hearts == 1 ? "" : "s")}");
            if (mana > 0) parts.Add($"+{mana} max mana");
            if (heal > 0) parts.Add($"potions heal {heal} more");
            if (dashCut > 0) parts.Add($"dash costs {dashCut} less mana");
            if (coins > 0) parts.Add($"+{coins} coins for every chest reward");
            if (xp > 0) parts.Add($"+{xp} XP for every floor walked down");
            var effect = string.Join(", ", parts);
            c.Items.Add(new ItemDefinition
            {
                Id = id, DisplayName = name, Slot = slot, Rarity = rarity,
                Effect = effect.Length > 0 ? char.ToUpperInvariant(effect[0]) + effect.Substring(1) : effect,
                SlashDamage = slash, MaxHp = hearts, MaxMana = mana, PotionHeal = heal, DashCostCut = dashCut,
                CoinsPerChestReward = coins, XpPerFloor = xp,
            });
        }

        /// <summary>How often each rarity drops, relative to the others (D-036): a legendary is six times rarer than a common.</summary>
        public static int DropWeight(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Common: return 6;
                case ItemRarity.Uncommon: return 4;
                case ItemRarity.Rare: return 3;
                case ItemRarity.Epic: return 2;
                default: return 1;
            }
        }

        /// <summary>
        /// One item by rarity weight from a random number the caller draws, so a run's drop stays a function of its seed. Only
        /// items at or above <paramref name="atLeast"/> take part.
        /// </summary>
        public ItemDefinition PickItem(ulong roll, ItemRarity atLeast = ItemRarity.Common)
        {
            long total = 0;
            foreach (var item in Items)
                if (item.Rarity >= atLeast) total += DropWeight(item.Rarity);
            if (total == 0) return null;
            long at = (long)(roll % (ulong)total);
            foreach (var item in Items)
            {
                if (item.Rarity < atLeast) continue;
                at -= DropWeight(item.Rarity);
                if (at < 0) return item;
            }
            return null;
        }

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
                MaxMana = 6, ShieldCost = 2, DashCost = 3, DashDistance = 2,
                RevealRadius = 1, SenseRadius = 2,
            };
            c.HeroIdentities[DefaultHeroId] = new HeroIdentityDefinition
            {
                Id = DefaultHeroId, DisplayName = "Sir Clickington", ClassId = "knight", Tagline = "Brave. Loyal. Clickable.",
            };

            // The Paladin trades reach for staying power: more hearts, more mana for shields and a stronger potion,
            // against a dash of a single tile that costs more (rules §5.1). A softer slash was tried first and made boss
            // fights drag: the sighted bot won 24 of 40 on Blobert's Wrath against the Knight's 40.
            c.HeroClasses["paladin"] = new HeroClassDefinition
            {
                Id = "paladin", DisplayName = "Paladin",
                MaxHp = 12, SlashDamage = 2, StartingPotions = 2, PotionHeal = 6,
                MaxMana = 8, ShieldCost = 2, DashCost = 4, DashDistance = 1,
                RevealRadius = 1, SenseRadius = 2,
            };
            c.HeroIdentities["dawnward"] = new HeroIdentityDefinition
            {
                Id = "dawnward", DisplayName = "Dawnward", ClassId = "paladin", Tagline = "Steadfast. Shielded. Unshaken.",
            };

            // The equipment library (D-028, D-036): every piece is starting numbers, grouped by slot, rarer is stronger.
            Gear(c, "steel_sword", "Steel Sword", ItemSlot.Weapon, ItemRarity.Common, slash: 1);
            Gear(c, "battle_axe", "Battle Axe", ItemSlot.Weapon, ItemRarity.Uncommon, slash: 1, hearts: 1);
            Gear(c, "lucky_wand", "Lucky Wand", ItemSlot.Weapon, ItemRarity.Uncommon, coins: 3);
            Gear(c, "spiked_mace", "Spiked Mace", ItemSlot.Weapon, ItemRarity.Uncommon, slash: 1, coins: 2);
            Gear(c, "frost_blade", "Frost Blade", ItemSlot.Weapon, ItemRarity.Rare, slash: 2);
            Gear(c, "void_blade", "Void Blade", ItemSlot.Weapon, ItemRarity.Epic, slash: 2, hearts: 1);
            Gear(c, "celestial_staff", "Celestial Staff", ItemSlot.Weapon, ItemRarity.Legendary, slash: 2, mana: 2);
            Gear(c, "dragonslayer", "Dragonslayer", ItemSlot.Weapon, ItemRarity.Legendary, slash: 3);
            Gear(c, "iron_helm", "Iron Helm", ItemSlot.Helmet, ItemRarity.Common, hearts: 1);
            Gear(c, "horned_helm", "Horned Helm", ItemSlot.Helmet, ItemRarity.Uncommon, hearts: 1, mana: 1);
            Gear(c, "cobalt_helm", "Cobalt Helm", ItemSlot.Helmet, ItemRarity.Rare, mana: 2);
            Gear(c, "shadow_hood", "Shadow Hood", ItemSlot.Helmet, ItemRarity.Rare, dashCut: 1);
            Gear(c, "void_helm", "Void Helm", ItemSlot.Helmet, ItemRarity.Epic, hearts: 2, mana: 1);
            Gear(c, "crown_of_kings", "Crown of Kings", ItemSlot.Helmet, ItemRarity.Legendary, coins: 5, xp: 10);
            Gear(c, "iron_cuirass", "Iron Cuirass", ItemSlot.Armor, ItemRarity.Common, hearts: 1);
            Gear(c, "ranger_mail", "Ranger Mail", ItemSlot.Armor, ItemRarity.Uncommon, hearts: 1, heal: 1);
            Gear(c, "sapphire_plate", "Sapphire Plate", ItemSlot.Armor, ItemRarity.Rare, hearts: 2, mana: 1);
            Gear(c, "royal_plate", "Royal Plate", ItemSlot.Armor, ItemRarity.Epic, hearts: 3);
            Gear(c, "void_plate", "Void Plate", ItemSlot.Armor, ItemRarity.Epic, hearts: 2, mana: 2);
            Gear(c, "sunforged_plate", "Sunforged Plate", ItemSlot.Armor, ItemRarity.Legendary, slash: 1, hearts: 3);
            Gear(c, "iron_shield", "Iron Shield", ItemSlot.Shield, ItemRarity.Common, hearts: 1);
            Gear(c, "round_buckler", "Round Buckler", ItemSlot.Shield, ItemRarity.Common, heal: 1);
            Gear(c, "star_shield", "Star Shield", ItemSlot.Shield, ItemRarity.Uncommon, hearts: 1, mana: 1);
            Gear(c, "gilded_shield", "Gilded Shield", ItemSlot.Shield, ItemRarity.Rare, mana: 2);
            Gear(c, "lion_shield", "Lion Shield", ItemSlot.Shield, ItemRarity.Rare, hearts: 2);
            Gear(c, "aegis_of_dawn", "Aegis of Dawn", ItemSlot.Shield, ItemRarity.Legendary, hearts: 3, mana: 1);
            Gear(c, "iron_greaves", "Iron Greaves", ItemSlot.Boots, ItemRarity.Common, hearts: 1);
            Gear(c, "swift_boots", "Swift Boots", ItemSlot.Boots, ItemRarity.Uncommon, dashCut: 1);
            Gear(c, "gold_treads", "Gold Treads", ItemSlot.Boots, ItemRarity.Rare, coins: 3);
            Gear(c, "gem_treads", "Gem Treads", ItemSlot.Boots, ItemRarity.Rare, mana: 2);
            Gear(c, "boots_of_swiftness", "Boots of Swiftness", ItemSlot.Boots, ItemRarity.Epic, mana: 1, dashCut: 1);
            Gear(c, "emerald_ring", "Emerald Ring", ItemSlot.Trinket, ItemRarity.Common, coins: 2);
            Gear(c, "ruby_ring", "Ruby Ring", ItemSlot.Trinket, ItemRarity.Uncommon, slash: 1);
            Gear(c, "healing_charm", "Healing Charm", ItemSlot.Trinket, ItemRarity.Uncommon, heal: 2);
            Gear(c, "scholars_ring", "Scholar's Ring", ItemSlot.Trinket, ItemRarity.Uncommon, xp: 5);
            Gear(c, "amethyst_ring", "Amethyst Ring", ItemSlot.Trinket, ItemRarity.Rare, mana: 2);
            Gear(c, "sapphire_amulet", "Sapphire Amulet", ItemSlot.Trinket, ItemRarity.Epic, hearts: 2, mana: 2);
            Gear(c, "sun_amulet", "Sun Amulet", ItemSlot.Trinket, ItemRarity.Legendary, slash: 1, xp: 10);

            c.DailyRewards.Add(new RewardBundle { Label = "30 coins", Coins = 30 });
            c.DailyRewards.Add(new RewardBundle { Label = "a potion ration", PotionRations = 1 });
            c.DailyRewards.Add(new RewardBundle { Label = "50 coins", Coins = 50 });
            c.DailyRewards.Add(new RewardBundle { Label = "a heart token", HeartTokens = 1 });
            c.DailyRewards.Add(new RewardBundle { Label = "80 coins", Coins = 80 });
            c.DailyRewards.Add(new RewardBundle { Label = "3 gems", Gems = 3 });
            c.DailyRewards.Add(new RewardBundle { Label = "a special key", SpecialKeys = 1 });

            Achieve(c, "first_steps", "First Steps", "Finish a run", AchievementStat.RunsFinished, 1, new RewardBundle { Label = "25 coins", Coins = 25 });
            Achieve(c, "deep_diver", "Deep Diver", "Reach floor 3", AchievementStat.DeepestFloor, 3, new RewardBundle { Label = "40 coins", Coins = 40 });
            Achieve(c, "into_the_lair", "Into the Lair", "Reach floor 5", AchievementStat.DeepestFloor, 5, new RewardBundle { Label = "a potion ration", PotionRations = 1 });
            Achieve(c, "blobert_bested", "Blobert Bested", "Defeat Lord Blobert", AchievementStat.RunsWon, 1, new RewardBundle { Label = "5 gems", Gems = 5 });
            Achieve(c, "champion", "Champion", "Win 5 runs", AchievementStat.RunsWon, 5, new RewardBundle { Label = "a special key", SpecialKeys = 1 });
            Achieve(c, "monster_hunter", "Monster Hunter", "Slay 25 monsters", AchievementStat.MonstersSlain, 25, new RewardBundle { Label = "50 coins", Coins = 50 });
            Achieve(c, "monster_slayer", "Monster Slayer", "Slay 100 monsters", AchievementStat.MonstersSlain, 100, new RewardBundle { Label = "3 gems", Gems = 3 });
            Achieve(c, "treasure_seeker", "Treasure Seeker", "Open 20 chests", AchievementStat.ChestsOpened, 20, new RewardBundle { Label = "50 coins", Coins = 50 });
            Achieve(c, "hoarder", "Hoarder", "Carry out 1,000 coins", AchievementStat.CoinsEarned, 1000, new RewardBundle { Label = "4 gems", Gems = 4 });
            Achieve(c, "seasoned", "Seasoned", "Reach level 5", AchievementStat.Level, 5, new RewardBundle { Label = "a heart token", HeartTokens = 1 });
            Achieve(c, "collector", "Collector", "Own 5 pieces of gear", AchievementStat.ItemsOwned, 5, new RewardBundle { Label = "3 gems", Gems = 3 });

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
