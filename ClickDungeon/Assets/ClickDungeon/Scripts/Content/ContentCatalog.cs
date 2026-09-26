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
        /// <summary>The hero a new player starts as, and the one a run falls back to (D-057).</summary>
        public const string DefaultHeroId = "ironheart";

        /// <summary>
        /// Sir Clickington: the game's mascot, painted into the title and HUD backgrounds and the voice of its letters,
        /// and - for now - still a playable Knight at the end of the roster (D-060), until his comedy campaign gives him a
        /// home of his own. Screens that lay the playing hero's face over the painted background compare against this,
        /// never against <see cref="DefaultHeroId"/>: when he is the one playing, the painted face is already his.
        /// </summary>
        public const string MascotId = "sir_clickington";

        /// <summary>
        /// Hero ids that once existed and what a saved run of theirs becomes, applied as a save loads. Empty today: D-057
        /// sent Sir Clickington's runs to Ironheart, and D-060 made him playable again, so a run saved as him stays his.
        /// </summary>
        public static readonly System.Collections.Generic.IReadOnlyDictionary<string, string> RetiredHeroes =
            new System.Collections.Generic.Dictionary<string, string>();
        /// <summary>Guard used for a vault on a floor whose profile has no enemy pool of its own.</summary>
        public const string DefaultVaultEnemyId = "goblin";

        /// <summary>2: the first expansion monsters (D-058); 3: the second wave and the act bosses (D-061, D-062); 4: the six new
        /// classes (D-063). A save that
        /// names them is refused by an older build.</summary>
        public int Version = 4;
        /// <summary>
        /// Twenty: four acts of <see cref="BossEvery"/> floors, each ending in a boss, Blobert's Court the last (D-062).
        /// </summary>
        public int RunFloorCount = 20;

        /// <summary>A boss floor every this many floors (D-062). The floor profiles are laid out to match it.</summary>
        public const int BossEvery = 5;
        public HazardTuning Hazards = new HazardTuning();
        /// <summary>What a vault room behind a door holds (D-018).</summary>
        public VaultTuning Vault = new VaultTuning();
        public TreasureTuning Treasure = new TreasureTuning();
        public XpTuning Xp = new XpTuning();
        public RenownTuning Renown = new RenownTuning();
        /// <summary>The tier this catalog was built for. Every number in it already includes that tier's adjustments.</summary>
        public Difficulty Difficulty = Difficulty.Medium;
        /// <summary>HP restored when the hero arrives on the next floor.</summary>
        public int FloorClearHeal;
        /// <summary>
        /// The stairs never leave the hero below `MaxHp / MercyOnStairs` (D-071), set by the tier. Zero turns it off - as
        /// Blobert's Wrath does, where the card has always said so. This is help that
        /// only arrives when it is needed: a careful player is under half their hearts on 6% of turns and a careless one
        /// on up to 33%, so it lifts the floor of the game without raising its ceiling. It is why the four fragile
        /// classes stop being unplayable for a sloppy player while a sharp one barely notices it.
        /// </summary>
        public int MercyOnStairs;

        /// <summary>Extra hearts and potions for the first run of a profile, set by the tier (D-076).</summary>
        public int FirstRunHearts, FirstRunPotions;

        /// <summary>
        /// Turns the hero is stuck when a spider webs them (D-061), set by the tier (D-074). It was the literal 1,
        /// written into EnemyAi where no tier could reach it.
        /// </summary>
        public int WebTurns = 1;
        /// <summary>
        /// Turns Lord Blobert stays deflated after puffing up - the window in which he rests and takes double damage
        /// (D-039), set by the tier (D-074). It was a literal 1 assigned to a field the Deflated branch then overwrote
        /// without reading, so the knob existed and did nothing.
        /// </summary>
        public int DeflatedTurns = 1;

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

        /// <summary>Class talents (D-037), each tree in path and tier order.</summary>
        public readonly List<TalentDefinition> Talents = new List<TalentDefinition>();
        /// <summary>Heroes shown locked on Hero Select (D-037).</summary>
        public readonly List<HeroPreview> ComingSoon = new List<HeroPreview>();

        static void Talent(ContentCatalog c, string id, string classId, string branch, int tier, int maxRank, string name, string summary,
            string perRank, TalentEffect effect, int amount = 1, string requires = null, string skill = null) =>
            c.Talents.Add(new TalentDefinition
            {
                Id = id, ClassId = classId, BranchId = branch, Tier = tier, MaxRank = maxRank, Name = name, Summary = summary,
                PerRank = perRank, Effect = effect, Amount = amount, Requires = requires, SkillId = skill,
            });

        /// <summary>A usable skill (D-075), named by the talent that unlocks it.</summary>
        static void Skill(ContentCatalog c, string id, string classId, string name, string summary,
            int manaCost, SkillEffect effect, int amount, SkillTarget target, int range = 1) =>
            c.Skills.Add(id, new SkillDefinition
            {
                Id = id, ClassId = classId, Name = name, Summary = summary,
                ManaCost = manaCost, Effect = effect, Amount = amount, Target = target, Range = range,
            });

        public TalentDefinition Talent(string id)
        {
            foreach (var talent in Talents)
                if (talent.Id == id) return talent;
            return null;
        }

        public List<TalentDefinition> TalentsOf(string classId) => Talents.FindAll(t => t.ClassId == classId);

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
        /// <summary>The usable skills (D-075), by id.</summary>
        public readonly Dictionary<string, SkillDefinition> Skills = new Dictionary<string, SkillDefinition>();

        /// <summary>How many usable skills a hero carries at once. <see cref="BoardRules.SkillSlots"/> is the number.</summary>
        public const int SkillSlots = BoardRules.SkillSlots;

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

        public SkillDefinition Skill(string id) => Get(Skills, id, "skill");

        /// <summary>The skill with this id, or null. For the places that hold an id they did not choose - a save, a profile.</summary>
        public SkillDefinition SkillOrNull(string id) => id != null && Skills.TryGetValue(id, out var s) ? s : null;

        /// <summary>This class's usable skills, in the order its talent tree lists the talents that unlock them.</summary>
        public List<SkillDefinition> SkillsOf(string classId)
        {
            var list = new List<SkillDefinition>();
            foreach (var talent in TalentsOf(classId))
                if (talent.SkillId != null && Skills.TryGetValue(talent.SkillId, out var skill)) list.Add(skill);
            return list;
        }

        /// <summary>The talent whose learning unlocks this skill, or null.</summary>
        public TalentDefinition TalentGranting(string skillId)
        {
            foreach (var talent in Talents)
                if (talent.SkillId == skillId) return talent;
            return null;
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
                // D-047: slash 3, not 2. The Paladin's hearts and potions beat the Knight's reach for a player who cannot
                // see what is coming, so the Knight answers in his own lane: he kills in fewer turns and so takes fewer hits.
                MaxHp = 10, SlashDamage = 3, StartingPotions = 2, PotionHeal = 4,
                MaxMana = 6, ShieldCost = 2, DashCost = 3, DashDistance = 2,
                RevealRadius = 1, SenseRadius = 2,
            };
            c.HeroIdentities[DefaultHeroId] = new HeroIdentityDefinition
            {
                Id = DefaultHeroId, DisplayName = "Ironheart", ClassId = "knight", Tagline = "Sturdy. Stubborn. Unbroken.",
            };

            // The Paladin trades reach for staying power: more hearts, more mana for shields and a stronger potion,
            // against a dash of a single tile that costs more (rules §5.1). A softer slash was tried first and made boss
            // fights drag: the sighted bot won 24 of 40 on Blobert's Wrath against the Knight's 40.
            // D-047: 11 hearts, not 12 — with the Knight hitting for 3 the classes now win about as often as each other.
            c.HeroClasses["paladin"] = new HeroClassDefinition
            {
                Id = "paladin", DisplayName = "Paladin",
                MaxHp = 11, SlashDamage = 2, StartingPotions = 2, PotionHeal = 6,
                MaxMana = 8, ShieldCost = 2, DashCost = 4, DashDistance = 1,
                RevealRadius = 1, SenseRadius = 2,
            };
            c.HeroIdentities["dawnward"] = new HeroIdentityDefinition
            {
                Id = "dawnward", DisplayName = "Dawnward", ClassId = "paladin", Tagline = "Steadfast. Shielded. Unshaken.",
            };
            // The six new heroes (D-063), in the order of their sheets.
            c.HeroIdentities["shadowcut"] = new HeroIdentityDefinition
            {
                Id = "shadowcut", DisplayName = "Shadowcut", ClassId = "rogue", Tagline = "Quick. Quiet. Gone.",
                Title = "The Unseen Blade", Quote = "Exploits openings and converts precision into burst damage.",
            };
            c.HeroIdentities["emberwisp"] = new HeroIdentityDefinition
            {
                Id = "emberwisp", DisplayName = "Emberwisp", ClassId = "wizard", Tagline = "Curious. Clever. Combustible.",
                Title = "Spark of the Arcane", Quote = "Controls the battlefield with powerful magical effects.",
            };
            c.HeroIdentities["windsong"] = new HeroIdentityDefinition
            {
                Id = "windsong", DisplayName = "Windsong", ClassId = "ranger", Tagline = "Swift. Sure. Silent.",
                Title = "Voice of the Wild", Quote = "The dungeon whispers. I simply listen.",
            };
            c.HeroIdentities["lightbringer"] = new HeroIdentityDefinition
            {
                Id = "lightbringer", DisplayName = "Lightbringer", ClassId = "cleric", Tagline = "Gentle. Radiant. Resolute.",
                Title = "Keeper of the Flame", Quote = "Sustains, restores momentum and turns protection into victory.",
            };
            c.HeroIdentities["rageclaw"] = new HeroIdentityDefinition
            {
                Id = "rageclaw", DisplayName = "Rageclaw", ClassId = "berserker", Tagline = "Loud. Hairy. Unstoppable.",
                Title = "The Roaring Storm", Quote = "Pain is just progress.",
            };
            c.HeroIdentities["gearspark"] = new HeroIdentityDefinition
            {
                Id = "gearspark", DisplayName = "Gearspark", ClassId = "engineer", Tagline = "Clever. Curious. Clanky.",
                Title = "Tinkerer of the Deep", Quote = "A problem is just a puzzle with more pieces!",
            };
            // The mascot, playable for now (D-060): last on the roster, a Knight like Ironheart, with the words he had.
            c.HeroIdentities[MascotId] = new HeroIdentityDefinition
            {
                Id = MascotId, DisplayName = "Sir Clickington", ClassId = "knight", Tagline = "Brave. Loyal. Clickable.",
            };

            // ------------------------------------------------------------------ class talent trees (D-037)
            var knight = c.HeroClasses["knight"];
            knight.Role = "Versatile vanguard";
            knight.Playstyle = "Strikes first, moves fast and turns every chest into momentum. The Knight rewards picking fights on his own terms.";
            knight.Difficulty = 1;
            knight.Theme = "#D8433A";
            knight.Branches = new[]
            {
                new TalentBranch { Id = "blade", Name = "BLADE", Focus = "First strikes and finishing blows", Color = "#E0533F" },
                new TalentBranch { Id = "bulwark", Name = "BULWARK", Focus = "Shield work and hitting back", Color = "#4C8DE0" },
                new TalentBranch { Id = "adventurer", Name = "ADVENTURER", Focus = "Dashes, chests and the long road", Color = "#6CC04A" },
            };
            var paladin = c.HeroClasses["paladin"];
            paladin.Role = "Holy guardian";
            paladin.Playstyle = "Outlasts everything. The Paladin blocks, punishes the staggered and turns faith into second chances.";
            paladin.Difficulty = 2;
            paladin.Theme = "#F2C14E";
            paladin.Branches = new[]
            {
                new TalentBranch { Id = "hammer", Name = "HAMMER", Focus = "Punish the staggered and the mighty", Color = "#F2B233" },
                new TalentBranch { Id = "aegis", Name = "AEGIS", Focus = "Blocks that give back", Color = "#7FB5F0" },
                new TalentBranch { Id = "devotion", Name = "DEVOTION", Focus = "Potions, prayer and light", Color = "#F4E6B0" },
            };

            Talent(c, "k_opening_strike", "knight", "blade", 1, 3, "Opening Strike", "Hit hard before they hit back.",
                "+1 slash damage against an enemy at full health", TalentEffect.OpeningStrike);
            Talent(c, "k_cleave", "knight", "blade", 2, 1, "Cleave", "Your slash carries on into their friends.",
                "A slash also deals 1 damage to every other awake enemy next to you", TalentEffect.Cleave, requires: "k_opening_strike", skill: "kni_shockwave");
            Talent(c, "k_executioner", "knight", "blade", 3, 2, "Executioner", "Finish what you started.",
                "+1 slash damage against an enemy at 2 hearts or fewer", TalentEffect.Executioner, requires: "k_cleave");
            Talent(c, "k_relentless", "knight", "blade", 4, 1, "Relentless", "Every kill fuels the next.",
                "Slaying an enemy with a slash restores 1 heart and 2 mana", TalentEffect.Relentless, requires: "k_executioner");
            Talent(c, "k_sturdy", "knight", "bulwark", 1, 3, "Sturdy", "More knight to go around.",
                "+1 max heart", TalentEffect.MaxHearts);
            Talent(c, "k_shield_wall", "knight", "bulwark", 2, 1, "Shield Wall", "Raise it without a second thought.",
                "SHIELD costs 1 less mana", TalentEffect.ShieldCostCut, requires: "k_sturdy", skill: "kni_rally");
            Talent(c, "k_riposte", "knight", "bulwark", 3, 2, "Riposte", "Block, then answer.",
                "An attack your shield blocks deals 1 damage back to the attacker", TalentEffect.Riposte, requires: "k_shield_wall");
            Talent(c, "k_bastion", "knight", "bulwark", 4, 1, "Bastion", "Behind the shield, he mends.",
                "Every SHIELD also restores 1 heart", TalentEffect.Bastion, requires: "k_riposte");
            Talent(c, "k_light_step", "knight", "adventurer", 1, 2, "Light Step", "Travel light, dash often.",
                "DASH costs 1 less mana (never below 1)", TalentEffect.DashCostCut);
            Talent(c, "k_treasure_sense", "knight", "adventurer", 2, 1, "Treasure Sense", "He knows exactly where to kick.",
                "Chests open with one tap fewer (never below 1)", TalentEffect.ChestTapCut, requires: "k_light_step", skill: "kni_shield_bash");
            Talent(c, "k_fortune", "knight", "adventurer", 3, 2, "Fortune's Favour", "The dungeon pays the bold.",
                "+3 coins for every chest reward", TalentEffect.CoinsPerChestReward, amount: 3, requires: "k_treasure_sense");
            Talent(c, "k_second_wind", "knight", "adventurer", 4, 1, "Second Wind", "Every staircase is a fresh start.",
                "Arriving on a new floor restores 3 more hearts", TalentEffect.SecondWind, amount: 3, requires: "k_fortune");

            Talent(c, "p_holy_wrath", "paladin", "hammer", 1, 3, "Holy Wrath", "What is risen should lie down.",
                "+1 slash damage against the undead", TalentEffect.HolyWrath);
            Talent(c, "p_consecrate", "paladin", "hammer", 2, 1, "Consecrate", "Holy light bursts from the raised shield.",
                "SHIELD deals 1 damage to every awake enemy next to you", TalentEffect.Consecrate, requires: "p_holy_wrath", skill: "pal_smite");
            Talent(c, "p_dawnstrike", "paladin", "hammer", 3, 2, "Dawnstrike", "The mighty fall hardest.",
                "+1 slash damage against bosses", TalentEffect.Dawnstrike, requires: "p_consecrate");
            Talent(c, "p_wrath_of_dawn", "paladin", "hammer", 4, 1, "Wrath of Dawn", "One falls, the rest reel.",
                "Slaying an enemy with a slash staggers every other awake enemy next to you", TalentEffect.WrathOfDawn, requires: "p_dawnstrike");
            Talent(c, "p_plated", "paladin", "aegis", 1, 3, "Plated", "Another layer of gold and faith.",
                "+1 max heart", TalentEffect.MaxHearts);
            Talent(c, "p_holy_bulwark", "paladin", "aegis", 2, 1, "Holy Bulwark", "Every block is answered by grace.",
                "An attack your shield blocks restores 2 mana", TalentEffect.HolyBulwark, amount: 2, requires: "p_plated", skill: "pal_bulwark");
            Talent(c, "p_unyielding", "paladin", "aegis", 3, 1, "Unyielding", "Wounded, never broken.",
                "While at half hearts or fewer, every hit deals 1 less damage (never below 1)", TalentEffect.Unyielding, requires: "p_holy_bulwark");
            Talent(c, "p_divine_shield", "paladin", "aegis", 4, 1, "Divine Shield", "Not today.",
                "Once per floor, a blow that would end you leaves you at 1 heart and heals 3", TalentEffect.DivineShield, amount: 3, requires: "p_unyielding");
            Talent(c, "p_blessed_draught", "paladin", "devotion", 1, 3, "Blessed Draught", "Every potion, a small miracle.",
                "Potions heal 1 more", TalentEffect.PotionHeal);
            Talent(c, "p_prayer", "paladin", "devotion", 2, 1, "Prayer", "Stillness restores the spirit.",
                "Waiting a turn restores 1 extra mana", TalentEffect.Prayer, requires: "p_blessed_draught", skill: "pal_lay_on_hands");
            Talent(c, "p_guiding_light", "paladin", "devotion", 3, 1, "Guiding Light", "The way down is shown.",
                "Each new floor starts with its key uncovered", TalentEffect.GuidingLight, requires: "p_prayer");
            Talent(c, "p_sanctified", "paladin", "devotion", 4, 1, "Sanctified", "Blessed waters, blessed wine.",
                "Potions also refill your mana, and fountains heal you fully", TalentEffect.Sanctified, requires: "p_guiding_light");


            // ------------------------------------------------------------------ the six new classes (D-063)
            // Each has its own rule (Traits): a perk every run of the class starts with, which its talents may raise.
            // The Rogue (D-063): quick, and deadly against any monster not already swinging at her tile.
            c.HeroClasses["rogue"] = new HeroClassDefinition
            {
                Id = "rogue", DisplayName = "Rogue",
                MaxHp = 10, SlashDamage = 2, StartingPotions = 2, PotionHeal = 4,
                MaxMana = 6, ShieldCost = 2, DashCost = 2, DashDistance = 2,
                RevealRadius = 1, SenseRadius = 2,
                Traits = new Dictionary<TalentEffect, int> { [TalentEffect.Ambush] = 2 },
                TraitName = "Ambush", TraitText = "Slashes deal +2 to a monster whose telegraphed action is not aimed at your tile.",
                Role = "Burst damage", Difficulty = 3, Theme = "#8E5BD6",
                Playstyle = "Strikes the moment a monster looks away. The Rogue reads every telegraph and cuts where the danger is not.",
                Branches = new[]
                {
                    new TalentBranch { Id = "shadows", Name = "SHADOWS", Focus = "Ambushes and the killing blow", Color = "#8E5BD6" },
                    new TalentBranch { Id = "evasion", Name = "EVASION", Focus = "Dashes, dodges and staying alive", Color = "#5FB0A8" },
                    new TalentBranch { Id = "greed", Name = "GREED", Focus = "Coins, locks and keys", Color = "#E7B640" },
                },
            };
            Talent(c, "ro_cruel_edge", "rogue", "shadows", 1, 3, "Cruel Edge", "Every opening, a little wider.",
                "+1 ambush damage", TalentEffect.Ambush);
            Talent(c, "ro_twin_fangs", "rogue", "shadows", 2, 1, "Twin Fangs", "One dagger for each of them.",
                "A slash also deals 1 damage to every other awake enemy next to you", TalentEffect.Cleave, requires: "ro_cruel_edge", skill: "rog_throat_cut");
            Talent(c, "ro_coup_de_grace", "rogue", "shadows", 3, 2, "Coup de Grace", "Finish it quietly.",
                "+1 slash damage against an enemy at 2 hearts or fewer", TalentEffect.Executioner, requires: "ro_twin_fangs");
            Talent(c, "ro_eviscerate", "rogue", "shadows", 4, 1, "Eviscerate", "They never saw it coming.",
                "An ambush that does not kill staggers the target (not bosses)", TalentEffect.Eviscerate, requires: "ro_coup_de_grace");
            Talent(c, "ro_supple_leathers", "rogue", "evasion", 1, 3, "Supple Leathers", "Light, but not that light.",
                "+1 max heart", TalentEffect.MaxHearts);
            Talent(c, "ro_light_feet", "rogue", "evasion", 2, 1, "Light Feet", "Here, then there.",
                "DASH costs 1 less mana (never below 1)", TalentEffect.DashCostCut, requires: "ro_supple_leathers", skill: "rog_smoke");
            Talent(c, "ro_slippery", "rogue", "evasion", 3, 1, "Slippery", "Missed me.",
                "Once per floor, the first hit that would land on you misses", TalentEffect.Dodge, requires: "ro_light_feet");
            Talent(c, "ro_vanishing_act", "rogue", "evasion", 4, 1, "Vanishing Act", "Down the stairs and good as new.",
                "Arriving on a new floor restores 3 more hearts", TalentEffect.SecondWind, amount: 3, requires: "ro_slippery");
            Talent(c, "ro_fence", "rogue", "greed", 1, 2, "Fence", "Knows who pays best.",
                "+3 coins for every chest reward", TalentEffect.CoinsPerChestReward, amount: 3);
            Talent(c, "ro_lockpick", "rogue", "greed", 2, 1, "Lockpick", "Why kick what you can pick?",
                "Chests open with one tap fewer (never below 1)", TalentEffect.ChestTapCut, requires: "ro_fence", skill: "rog_bandage");
            Talent(c, "ro_casing_the_joint", "rogue", "greed", 3, 1, "Casing the Joint", "She knows where they keep it.",
                "Each new floor starts with its key uncovered", TalentEffect.GuidingLight, requires: "ro_lockpick");
            Talent(c, "ro_pickpocket", "rogue", "greed", 4, 1, "Pickpocket", "Their loss.",
                "Every monster you slay with a slash drops 5 coins", TalentEffect.Pickpocket, amount: 5, requires: "ro_casing_the_joint");
            // The Wizard (D-063): a heavy bolt that reaches down a clear line and shoves what it hits.
            c.HeroClasses["wizard"] = new HeroClassDefinition
            {
                Id = "wizard", DisplayName = "Wizard",
                MaxHp = 9, SlashDamage = 3, StartingPotions = 2, PotionHeal = 4,
                MaxMana = 8, ShieldCost = 2, DashCost = 3, DashDistance = 2,
                RevealRadius = 1, SenseRadius = 2,
                Traits = new Dictionary<TalentEffect, int> { [TalentEffect.Reach] = 3, [TalentEffect.Knockback] = 1 },
                TraitName = "Firebolt", TraitText = "Slashes fly up to 3 tiles in a straight, uncovered line and knock the target back a tile.",
                Role = "Battlefield control", Difficulty = 3, Theme = "#E0642E",
                Playstyle = "Burns from afar and keeps monsters where he wants them. The Wizard is fragile up close, and never needs to be.",
                Branches = new[]
                {
                    new TalentBranch { Id = "pyromancy", Name = "PYROMANCY", Focus = "Bolts that burn and spread", Color = "#E0642E" },
                    new TalentBranch { Id = "arcana", Name = "ARCANA", Focus = "Mana, and more of it", Color = "#9A6BE0" },
                    new TalentBranch { Id = "warding", Name = "WARDING", Focus = "Wards and second chances", Color = "#6FB6E8" },
                },
            };
            Talent(c, "w_searing_bolt", "wizard", "pyromancy", 1, 3, "Searing Bolt", "The first one always stings.",
                "+1 slash damage against an enemy at full health", TalentEffect.OpeningStrike);
            Talent(c, "w_fireball", "wizard", "pyromancy", 2, 1, "Fireball", "Bigger is better.",
                "A slash also deals 1 damage to every other awake enemy next to your target", TalentEffect.Fireball, requires: "w_searing_bolt", skill: "wiz_firebolt");
            Talent(c, "w_cinders", "wizard", "pyromancy", 3, 2, "Cinders", "Nothing left but ash.",
                "+1 slash damage against an enemy at 2 hearts or fewer", TalentEffect.Executioner, requires: "w_fireball");
            Talent(c, "w_blinding_flash", "wizard", "pyromancy", 4, 1, "Blinding Flash", "One falls, the rest are seeing spots.",
                "Slaying an enemy with a slash staggers every other awake enemy next to you", TalentEffect.WrathOfDawn, requires: "w_cinders");
            Talent(c, "w_deep_well", "wizard", "arcana", 1, 3, "Deep Well", "Always a little more.",
                "+1 max mana", TalentEffect.MaxMana);
            Talent(c, "w_meditation", "wizard", "arcana", 2, 1, "Meditation", "Breathe in. Breathe fire.",
                "Waiting a turn restores 1 extra mana", TalentEffect.Prayer, requires: "w_deep_well", skill: "wiz_nova");
            Talent(c, "w_soul_siphon", "wizard", "arcana", 3, 1, "Soul Siphon", "Waste not.",
                "Slaying an enemy with a slash restores 1 heart and 2 mana", TalentEffect.Relentless, requires: "w_meditation");
            Talent(c, "w_alchemy", "wizard", "arcana", 4, 1, "Alchemy", "He improved the recipe.",
                "Potions also refill your mana, and fountains heal you fully", TalentEffect.Sanctified, requires: "w_soul_siphon");
            Talent(c, "w_warded_robes", "wizard", "warding", 1, 3, "Warded Robes", "Stitched with runes.",
                "+1 max heart", TalentEffect.MaxHearts);
            Talent(c, "w_quick_ward", "wizard", "warding", 2, 1, "Quick Ward", "A flick of the wrist.",
                "SHIELD costs 1 less mana", TalentEffect.ShieldCostCut, requires: "w_warded_robes", skill: "wiz_concussion");
            Talent(c, "w_flame_ward", "wizard", "warding", 3, 1, "Flame Ward", "Hot to the touch.",
                "SHIELD deals 1 damage to every awake enemy next to you", TalentEffect.Consecrate, requires: "w_quick_ward");
            Talent(c, "w_phoenix_feather", "wizard", "warding", 4, 1, "Phoenix Feather", "Not from the ashes. Not today.",
                "Once per floor, a blow that would end you leaves you at 1 heart and heals 3", TalentEffect.DivineShield, amount: 3, requires: "w_flame_ward");
            // The Ranger (D-063): the longest reach, the fewest hearts, and more damage the further the shot.
            c.HeroClasses["ranger"] = new HeroClassDefinition
            {
                Id = "ranger", DisplayName = "Ranger",
                MaxHp = 8, SlashDamage = 3, StartingPotions = 2, PotionHeal = 4,
                MaxMana = 6, ShieldCost = 2, DashCost = 2, DashDistance = 2,
                RevealRadius = 1, SenseRadius = 2,
                Traits = new Dictionary<TalentEffect, int> { [TalentEffect.Reach] = 4, [TalentEffect.Longshot] = 1 },
                TraitName = "Longshot", TraitText = "Slashes fly up to 4 tiles in a straight, uncovered line, +1 damage from 3 or more away.",
                Role = "Ranged damage", Difficulty = 2, Theme = "#6CC04A",
                Playstyle = "Keeps her distance and makes every step toward her cost. The Ranger wins fights before they reach her.",
                Branches = new[]
                {
                    new TalentBranch { Id = "marksman", Name = "MARKSMAN", Focus = "Aimed, piercing and pinning shots", Color = "#8FCB4A" },
                    new TalentBranch { Id = "survival", Name = "SURVIVAL", Focus = "Hearts, herbs and endurance", Color = "#B8864B" },
                    new TalentBranch { Id = "awareness", Name = "AWARENESS", Focus = "Speed, sight and the road ahead", Color = "#E8C85A" },
                },
            };
            Talent(c, "r_aimed_shot", "ranger", "marksman", 1, 3, "Aimed Shot", "Breathe, then loose.",
                "+1 slash damage against an enemy at full health", TalentEffect.OpeningStrike);
            Talent(c, "r_piercing_arrow", "ranger", "marksman", 2, 1, "Piercing Arrow", "Through one and into the next.",
                "A slash also deals 1 damage to the enemy right behind your target", TalentEffect.PiercingArrow, requires: "r_aimed_shot", skill: "ran_aimed_shot");
            Talent(c, "r_kill_shot", "ranger", "marksman", 3, 2, "Kill Shot", "Right where it hurts.",
                "+1 slash damage against an enemy at 2 hearts or fewer", TalentEffect.Executioner, requires: "r_piercing_arrow");
            Talent(c, "r_pinning_shot", "ranger", "marksman", 4, 1, "Pinning Shot", "Stay right there.",
                "A shot from 3 or more tiles away staggers the target (not bosses)", TalentEffect.PinningShot, requires: "r_kill_shot");
            Talent(c, "r_rangers_leathers", "ranger", "survival", 1, 3, "Ranger's Leathers", "Patched, and patched again.",
                "+1 max heart", TalentEffect.MaxHearts);
            Talent(c, "r_herbalism", "ranger", "survival", 2, 1, "Herbalism", "A little moss makes it better.",
                "Potions heal 1 more", TalentEffect.PotionHeal, requires: "r_rangers_leathers", skill: "ran_poultice");
            Talent(c, "r_endurance", "ranger", "survival", 3, 1, "Endurance", "Bent, not broken.",
                "While at half hearts or fewer, every hit deals 1 less damage (never below 1)", TalentEffect.Unyielding, requires: "r_herbalism");
            Talent(c, "r_second_wind", "ranger", "survival", 4, 1, "Second Wind", "Fresh air on every stair.",
                "Arriving on a new floor restores 3 more hearts", TalentEffect.SecondWind, amount: 3, requires: "r_endurance");
            Talent(c, "r_fleet_foot", "ranger", "awareness", 1, 2, "Fleet Foot", "Lighter than the wind.",
                "DASH costs 1 less mana (never below 1)", TalentEffect.DashCostCut);
            Talent(c, "r_tracker", "ranger", "awareness", 2, 1, "Tracker", "Goblins leave footprints.",
                "Each new floor starts with its key uncovered", TalentEffect.GuidingLight, requires: "r_fleet_foot", skill: "ran_snare");
            Talent(c, "r_scavenger", "ranger", "awareness", 3, 2, "Scavenger", "Nothing goes to waste.",
                "+3 coins for every chest reward", TalentEffect.CoinsPerChestReward, amount: 3, requires: "r_tracker");
            Talent(c, "r_hawkeye", "ranger", "awareness", 4, 1, "Hawkeye", "The way down, spotted from the top.",
                "Each new floor starts with its exit uncovered", TalentEffect.Hawkeye, requires: "r_scavenger");
            // The Cleric (D-063): a costly shield, and every block it makes heals her.
            c.HeroClasses["cleric"] = new HeroClassDefinition
            {
                Id = "cleric", DisplayName = "Cleric",
                MaxHp = 8, SlashDamage = 2, StartingPotions = 2, PotionHeal = 4,
                MaxMana = 6, ShieldCost = 3, DashCost = 4, DashDistance = 1,
                RevealRadius = 1, SenseRadius = 2,
                Traits = new Dictionary<TalentEffect, int> { [TalentEffect.Sanctuary] = 1 },
                TraitName = "Sanctuary", TraitText = "At half hearts or fewer, every attack your shield blocks heals you 1 heart.",
                Role = "Healer", Difficulty = 1, Theme = "#E8D8A0",
                Playstyle = "Mends through every fight. The Cleric picks her moment to shield, and once she is hurt every blow she turns aside heals her.",
                Branches = new[]
                {
                    new TalentBranch { Id = "mercy", Name = "MERCY", Focus = "Potions, prayer and rest", Color = "#F4E6B0" },
                    new TalentBranch { Id = "sanctity", Name = "SANCTITY", Focus = "Blocks that heal and protect", Color = "#8EC5F0" },
                    new TalentBranch { Id = "judgement", Name = "JUDGEMENT", Focus = "Holy light that burns", Color = "#F2C14E" },
                },
            };
            Talent(c, "c_blessed_draught", "cleric", "mercy", 1, 3, "Blessed Draught", "Every potion, a small miracle.",
                "Potions heal 1 more", TalentEffect.PotionHeal);
            Talent(c, "c_prayer", "cleric", "mercy", 2, 1, "Prayer", "Stillness restores the spirit.",
                "Waiting a turn restores 1 extra mana", TalentEffect.Prayer, requires: "c_blessed_draught", skill: "cle_mend");
            Talent(c, "c_renewal", "cleric", "mercy", 3, 1, "Renewal", "Every stair, a blessing.",
                "Arriving on a new floor restores 3 more hearts", TalentEffect.SecondWind, amount: 3, requires: "c_prayer");
            Talent(c, "c_holy_water", "cleric", "mercy", 4, 1, "Holy Water", "Blessed waters, blessed wine.",
                "Potions also refill your mana, and fountains heal you fully", TalentEffect.Sanctified, requires: "c_renewal");
            Talent(c, "c_faith", "cleric", "sanctity", 1, 3, "Faith", "It holds her up.",
                "+1 max heart", TalentEffect.MaxHearts);
            Talent(c, "c_swift_grace", "cleric", "sanctity", 2, 1, "Swift Grace", "Quick to the light.",
                "SHIELD costs 1 less mana", TalentEffect.ShieldCostCut, requires: "c_faith", skill: "cle_ward");
            Talent(c, "c_blessed_ward", "cleric", "sanctity", 3, 1, "Blessed Ward", "Every block, a blessing.",
                "Blocked attacks heal 1 more", TalentEffect.Sanctuary, requires: "c_swift_grace");
            Talent(c, "c_miracle", "cleric", "sanctity", 4, 1, "Miracle", "Not yet.",
                "Once per floor, a blow that would end you leaves you at 1 heart and heals 3", TalentEffect.DivineShield, amount: 3, requires: "c_blessed_ward");
            Talent(c, "c_rebuke", "cleric", "judgement", 1, 3, "Rebuke", "Strike the one who faltered.",
                "+1 slash damage against a staggered enemy", TalentEffect.Judgement);
            Talent(c, "c_holy_light", "cleric", "judgement", 2, 1, "Holy Light", "Light bursts from the raised shield.",
                "SHIELD deals 1 damage to every awake enemy next to you", TalentEffect.Consecrate, requires: "c_rebuke", skill: "cle_dispel");
            Talent(c, "c_smite", "cleric", "judgement", 3, 2, "Smite the Mighty", "The bigger they are.",
                "+1 slash damage against bosses", TalentEffect.Dawnstrike, requires: "c_holy_light");
            Talent(c, "c_radiance", "cleric", "judgement", 4, 1, "Radiance", "Behind the light, she mends.",
                "Every SHIELD also restores 1 heart", TalentEffect.Bastion, requires: "c_smite");
            // The Berserker (D-063): the least mana, and a slash that grows as he bleeds.
            c.HeroClasses["berserker"] = new HeroClassDefinition
            {
                Id = "berserker", DisplayName = "Berserker",
                MaxHp = 9, SlashDamage = 2, StartingPotions = 2, PotionHeal = 4,
                MaxMana = 4, ShieldCost = 3, DashCost = 3, DashDistance = 1,
                RevealRadius = 1, SenseRadius = 2,
                Traits = new Dictionary<TalentEffect, int> { [TalentEffect.Rage] = 6 },
                TraitName = "Rage", TraitText = "Slashes deal +1 for every 6 hearts you are missing.",
                Role = "Melee damage", Difficulty = 2, Theme = "#D9531E",
                Playstyle = "Wades in and gets angrier. The Berserker trades safety for a blade that sharpens with every heart he loses.",
                Branches = new[]
                {
                    new TalentBranch { Id = "fury", Name = "FURY", Focus = "Rage, and what it does to an axe", Color = "#E0533F" },
                    new TalentBranch { Id = "hide", Name = "HIDE", Focus = "Hearts, grit and refusing to fall", Color = "#A0703C" },
                    new TalentBranch { Id = "warpath", Name = "WARPATH", Focus = "Charges, plunder and war cries", Color = "#E09A2E" },
                },
            };
            Talent(c, "b_bloodlust", "berserker", "fury", 1, 3, "Bloodlust", "Hurt him. See what happens.",
                "+1 slash damage while at half hearts or fewer", TalentEffect.Bloodlust);
            Talent(c, "b_wide_swing", "berserker", "fury", 2, 1, "Wide Swing", "Everyone gets some.",
                "A slash also deals 1 damage to every other awake enemy next to you", TalentEffect.Cleave, requires: "b_bloodlust", skill: "ber_whirl");
            Talent(c, "b_brutal_finish", "berserker", "fury", 3, 2, "Brutal Finish", "No half measures.",
                "+1 slash damage against an enemy at 2 hearts or fewer", TalentEffect.Executioner, requires: "b_wide_swing");
            Talent(c, "b_blood_frenzy", "berserker", "fury", 4, 1, "Blood Frenzy", "Every kill fuels the next.",
                "Slaying an enemy with a slash restores 1 heart and 2 mana", TalentEffect.Relentless, requires: "b_brutal_finish");
            Talent(c, "b_thick_hide", "berserker", "hide", 1, 3, "Thick Hide", "More beard, more hearts.",
                "+1 max heart", TalentEffect.MaxHearts);
            Talent(c, "b_iron_gut", "berserker", "hide", 2, 1, "Iron Gut", "Drinks it all in one go.",
                "Potions heal 2 more", TalentEffect.PotionHeal, amount: 2, requires: "b_thick_hide", skill: "ber_second_wind");
            Talent(c, "b_pain_is_progress", "berserker", "hide", 3, 1, "Pain Is Progress", "He says it a lot.",
                "While at half hearts or fewer, every hit deals 1 less damage (never below 1)", TalentEffect.Unyielding, requires: "b_iron_gut");
            Talent(c, "b_undying_rage", "berserker", "hide", 4, 1, "Undying Rage", "Too angry to fall.",
                "Once per floor, a blow that would end you leaves you at 1 heart and heals 3", TalentEffect.DivineShield, amount: 3, requires: "b_pain_is_progress");
            Talent(c, "b_headlong", "berserker", "warpath", 1, 2, "Headlong", "Straight through.",
                "DASH costs 1 less mana (never below 1)", TalentEffect.DashCostCut);
            Talent(c, "b_smash_open", "berserker", "warpath", 2, 1, "Smash Open", "Locks are a suggestion.",
                "Chests open with one tap fewer (never below 1)", TalentEffect.ChestTapCut, requires: "b_headlong", skill: "ber_warcry");
            Talent(c, "b_plunder", "berserker", "warpath", 3, 2, "Plunder", "To the victor.",
                "+3 coins for every chest reward", TalentEffect.CoinsPerChestReward, amount: 3, requires: "b_smash_open");
            Talent(c, "b_war_cry", "berserker", "warpath", 4, 1, "War Cry", "One falls, the rest flinch.",
                "Slaying an enemy with a slash staggers every other awake enemy next to you", TalentEffect.WrathOfDawn, requires: "b_plunder");
            // The Engineer (D-063): the weakest slash, and a drone that fights on every turn he spends elsewhere.
            c.HeroClasses["engineer"] = new HeroClassDefinition
            {
                Id = "engineer", DisplayName = "Engineer",
                MaxHp = 7, SlashDamage = 1, StartingPotions = 2, PotionHeal = 4,
                MaxMana = 6, ShieldCost = 2, DashCost = 3, DashDistance = 2,
                RevealRadius = 1, SenseRadius = 2,
                Traits = new Dictionary<TalentEffect, int> { [TalentEffect.Drone] = 1 },
                TraitName = "Spark Drone", TraitText = "On every turn you do not slash, your drone zaps a monster next to you for 1.",
                Role = "Utility", Difficulty = 2, Theme = "#3FA7E0",
                Playstyle = "Never fights alone. His own blade is feeble; the drone does the work on every turn he spends moving, shielding or drinking.",
                Branches = new[]
                {
                    new TalentBranch { Id = "invention", Name = "INVENTION", Focus = "The drone, and making it better", Color = "#3FA7E0" },
                    new TalentBranch { Id = "control", Name = "CONTROL", Focus = "Plating, shields and shocks", Color = "#8C9AB0" },
                    new TalentBranch { Id = "tactics", Name = "TACTICS", Focus = "Locks, loot and the lay of the land", Color = "#E0B04A" },
                },
            };
            Talent(c, "e_calibrated_wrench", "engineer", "invention", 1, 3, "Calibrated Wrench", "Measure twice, whack once.",
                "+1 slash damage against an enemy at full health", TalentEffect.OpeningStrike);
            Talent(c, "e_overclock", "engineer", "invention", 2, 1, "Overclock", "More sparks.",
                "Your drone zaps for 1 more", TalentEffect.Drone, requires: "e_calibrated_wrench", skill: "eng_discharge");
            Talent(c, "e_long_range_coil", "engineer", "invention", 3, 1, "Long-Range Coil", "A longer leash.",
                "Your drone reaches monsters 2 tiles away", TalentEffect.DroneRange, requires: "e_overclock");
            Talent(c, "e_tesla_coil", "engineer", "invention", 4, 1, "Tesla Coil", "Everybody gets a spark.",
                "Your drone zaps every monster in reach, not just one", TalentEffect.ArcChain, requires: "e_long_range_coil");
            Talent(c, "e_riveted_plating", "engineer", "control", 1, 3, "Riveted Plating", "One more rivet.",
                "+1 max heart", TalentEffect.MaxHearts);
            Talent(c, "e_quick_deploy", "engineer", "control", 2, 1, "Quick Deploy", "Shield up in a snap.",
                "SHIELD costs 1 less mana", TalentEffect.ShieldCostCut, requires: "e_riveted_plating", skill: "eng_repair");
            Talent(c, "e_shock_plating", "engineer", "control", 3, 2, "Shock Plating", "Touch it and see.",
                "An attack your shield blocks deals 1 damage back to the attacker", TalentEffect.Riposte, requires: "e_quick_deploy");
            Talent(c, "e_static_field", "engineer", "control", 4, 1, "Static Field", "The air crackles.",
                "SHIELD deals 1 damage to every awake enemy next to you", TalentEffect.Consecrate, requires: "e_shock_plating");
            Talent(c, "e_salvage", "engineer", "tactics", 1, 2, "Salvage", "Spare parts are worth coin.",
                "+3 coins for every chest reward", TalentEffect.CoinsPerChestReward, amount: 3);
            Talent(c, "e_lockpicks", "engineer", "tactics", 2, 1, "Lockpicks", "A gadget for every lock.",
                "Chests open with one tap fewer (never below 1)", TalentEffect.ChestTapCut, requires: "e_salvage", skill: "eng_emp");
            Talent(c, "e_survey_drone", "engineer", "tactics", 3, 1, "Survey Drone", "It flies ahead.",
                "Each new floor starts with its exit uncovered", TalentEffect.Hawkeye, requires: "e_lockpicks");
            Talent(c, "e_field_repairs", "engineer", "tactics", 4, 1, "Field Repairs", "Patched up between floors.",
                "Arriving on a new floor restores 3 more hearts", TalentEffect.SecondWind, amount: 3, requires: "e_survey_drone");

            c.HeroIdentities[DefaultHeroId].Title = "The Iron Vanguard";
            c.HeroIdentities[DefaultHeroId].Quote = "Absorbs pressure, protects space, and wins through durability.";
            c.HeroIdentities["dawnward"].Title = "Shield of the Dawn";
            c.HeroIdentities["dawnward"].Quote = "Anchors the front line and turns faith into victory.";
            c.HeroIdentities[MascotId].Title = "The Brave...ish";
            c.HeroIdentities[MascotId].Quote = "Adventure looks better together.";

            // Heroes on the way (D-037) are all here now (D-063); the list stays for the next ones.

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
            // DATA-23: the depth goals stopped at floor 5 when the dungeon grew to twenty, leaving three quarters of it
            // with nothing to reach for. One for the end of each act.
            Achieve(c, "past_the_roost", "Past the Roost", "Reach floor 10", AchievementStat.DeepestFloor, 10, new RewardBundle { Label = "3 gems", Gems = 3 });
            Achieve(c, "backstage_pass", "Backstage Pass", "Reach floor 15", AchievementStat.DeepestFloor, 15, new RewardBundle { Label = "a heart token", HeartTokens = 1 });
            Achieve(c, "the_long_way_down", "The Long Way Down", "Reach floor 20", AchievementStat.DeepestFloor, 20, new RewardBundle { Label = "a special key", SpecialKeys = 1 });
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

            // First expansion monsters (D-058). Each adds one new rule, and every rule is telegraphed a turn ahead.
            // Skeleton Warrior: slow, and its first fall leaves bones that stand up again unless broken.
            AddEnemy(c, new EnemyDefinition { Id = "skeleton", DisplayName = "Skeleton Warrior", Behavior = EnemyBehavior.SlowChaser, MaxHp = 3, Damage = 2, Reassembles = true, Undead = true });
            // Armored Boar: heavy, and charges down any clear line - the whole path is marked the turn before.
            AddEnemy(c, new EnemyDefinition { Id = "armored_boar", DisplayName = "Armored Boar", Behavior = EnemyBehavior.Charger, MaxHp = 5, Damage = 3, Range = 4 });
            // Goblin Bomber: fragile, keeps its distance and lobs a lit bomb where the hero stands.
            AddEnemy(c, new EnemyDefinition { Id = "goblin_bomber", DisplayName = "Goblin Bomber", Behavior = EnemyBehavior.Bomber, MaxHp = 2, Damage = 1, ThrowRange = 3 });

            // Second expansion monsters (D-061), each one more rule, each telegraphed a turn ahead.
            // Cave Spider: spits a web that holds the hero in place for a turn.
            AddEnemy(c, new EnemyDefinition { Id = "cave_spider", DisplayName = "Cave Spider", Behavior = EnemyBehavior.Spinner, MaxHp = 3, Damage = 2, Range = 3 });
            // Spooky Spellbook: an imp's fire lanes, plus a spectral page summoned beside it every third turn.
            AddEnemy(c, new EnemyDefinition { Id = "spooky_spellbook", DisplayName = "Spooky Spellbook", Behavior = EnemyBehavior.Caster, MaxHp = 3, Damage = 2, Range = 3,
                SummonId = "spectral_page", SummonCount = 1, MaxMinions = 2, Undead = true });
            AddEnemy(c, new EnemyDefinition { Id = "spectral_page", DisplayName = "Spectral Page", Behavior = EnemyBehavior.Chaser, MaxHp = 1, Damage = 1, Undead = true });
            // Mimic Chest: looks like a chest until the hero comes right up to it; pays out in coins when it falls.
            AddEnemy(c, new EnemyDefinition { Id = "mimic_chest", DisplayName = "Mimic Chest", Behavior = EnemyBehavior.Chaser, MaxHp = 4, Damage = 3, Disguised = true, LootCoins = 15 });
            // Goblin Key Warden: holds the floor's key and backs away with it (placed by FloorProfile.KeyWarden, not by pools).
            AddEnemy(c, new EnemyDefinition { Id = "goblin_key_warden", DisplayName = "Goblin Key Warden", Behavior = EnemyBehavior.Keeper, MaxHp = 3, Damage = 1 });
            // Act bosses (D-062): one closes each act of five floors, Lord Blobert the last.
            AddEnemy(c, new EnemyDefinition
            {
                Id = "goblin_brute_king", DisplayName = "Goblin Brute King", Behavior = EnemyBehavior.BruteKing, IsBoss = true,
                // The gentlest boss, as the first should be: enraged, his blows still reach 3.
                MaxHp = 10, Damage = 2, SlamDamage = 2, Range = 3, EnragesAtHalf = true,
            });
            AddEnemy(c, new EnemyDefinition
            {
                Id = "bat_swarm_leader", DisplayName = "Bat Swarm Leader", Behavior = EnemyBehavior.SwarmLeader, IsBoss = true,
                MaxHp = 13, Damage = 2, Range = 4, SummonId = "bat", SummonCount = 2, MaxMinions = 4,
            });
            AddEnemy(c, new EnemyDefinition
            {
                Id = "theater_curtain_demon", DisplayName = "Theater Curtain Demon", Behavior = EnemyBehavior.Showman, IsBoss = true,
                MaxHp = 16, Damage = 3, SlamDamage = 3, Range = 4, SummonId = "stage_mask", SummonCount = 2, MaxMinions = 3,
                SlamShakesLines = true,
            });
            AddEnemy(c, new EnemyDefinition { Id = "bat", DisplayName = "Bat", Behavior = EnemyBehavior.Chaser, MaxHp = 1, Damage = 1 });
            AddEnemy(c, new EnemyDefinition { Id = "stage_mask", DisplayName = "Stage Mask", Behavior = EnemyBehavior.Chaser, MaxHp = 1, Damage = 2 });
            AddEnemy(c, new EnemyDefinition
            {
                Id = "lord_blobert", DisplayName = "Lord Blobert", Behavior = EnemyBehavior.Boss, IsBoss = true,
                // D-039: he never landed a blow (a telegraphed slam is always dodged in Free Roam), so he has more to chew
                // through, a slam that shakes its row and column, two minions per summon, and covered traps on his floor.
                MaxHp = 18, Damage = 2, SlamDamage = 4, PuffTurns = 2, SummonId = "slimelet", SummonCount = 2, MaxMinions = 4, SlamShakesLines = true,
                DoubleSlam = true,  // D-040: a second slam in every cycle, so even careful play has to keep moving
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

            // Twenty floors in four acts of five (D-062): four floors to learn an act's monsters, then its boss. Each act
            // introduces its newcomers one floor at a time, so no floor asks the player to learn two new rules at once.
            // ---- Act I: the goblins' halls. New: Mimic Chest (2), Skeleton Warrior (3), Goblin Key Warden (4).
            c.FloorProfiles.Add(new FloorProfile
            {
                FloorIndex = 1, Name = "The Upper Halls", EnemyPool = new[] { "goblin" }, MinEnemies = 1, MaxEnemies = 2,
                MinSpikes = 1, MaxSpikes = 2, MinBombs = 0, MaxBombs = 1, Chests = 1, MinPotions = 0, MaxPotions = 0,
            });
            c.FloorProfiles.Add(new FloorProfile
            {
                FloorIndex = 2, Name = "The Damp Cellars", EnemyPool = new[] { "goblin", "goblin", "crowned_slime", "mimic_chest" }, MinEnemies = 2, MaxEnemies = 2,
                MinSpikes = 1, MaxSpikes = 2, MinBombs = 1, MaxBombs = 1, Chests = 1, MinPotions = 0, MaxPotions = 1,
                Vault = true, Fountains = 1,
            });
            c.FloorProfiles.Add(new FloorProfile
            {
                FloorIndex = 3, Name = "The Bone Crypt", EnemyPool = new[] { "skeleton", "skeleton", "goblin", "crowned_slime" }, MinEnemies = 2, MaxEnemies = 3,
                MinSpikes = 1, MaxSpikes = 2, MinBombs = 1, MaxBombs = 1, Chests = 1, MinPotions = 0, MaxPotions = 1,
                Vault = true,
            });
            c.FloorProfiles.Add(new FloorProfile
            {
                FloorIndex = 4, Name = "The Goblin Warren", EnemyPool = new[] { "goblin", "goblin", "skeleton", "mimic_chest" }, MinEnemies = 2, MaxEnemies = 3,
                MinSpikes = 1, MaxSpikes = 2, MinBombs = 1, MaxBombs = 1, Chests = 1, MinPotions = 0, MaxPotions = 1,
                KeyWarden = true, Fountains = 1,
            });
            c.FloorProfiles.Add(new FloorProfile
            {
                FloorIndex = 5, Name = "The Goblin King's Hall", IsBoss = true, BossId = "goblin_brute_king", MinPotions = 1, MaxPotions = 1, MinExitDistance = 3,
                MinSpikes = 1, MaxSpikes = 1, MinBombs = 0, MaxBombs = 1,
            });
            // ---- Act II: the burning deep. New: Goblin Bomber (6), Cave Spider (7).
            c.FloorProfiles.Add(new FloorProfile
            {
                FloorIndex = 6, Name = "The Ember Vaults", EnemyPool = new[] { "goblin", "fire_imp", "goblin_bomber", "skeleton" }, MinEnemies = 2, MaxEnemies = 3,
                MinSpikes = 1, MaxSpikes = 2, MinBombs = 1, MaxBombs = 1, Chests = 1, MinPotions = 0, MaxPotions = 1,
                Vault = true, MinLava = 1, MaxLava = 2, Teleports = true,
            });
            c.FloorProfiles.Add(new FloorProfile
            {
                FloorIndex = 7, Name = "The Spider Caverns", EnemyPool = new[] { "cave_spider", "cave_spider", "goblin", "fire_imp" }, MinEnemies = 2, MaxEnemies = 3,
                MinSpikes = 1, MaxSpikes = 2, MinBombs = 1, MaxBombs = 1, Chests = 1, MinPotions = 0, MaxPotions = 1,
                Vault = true, Teleports = true,
            });
            c.FloorProfiles.Add(new FloorProfile
            {
                FloorIndex = 8, Name = "The Locked Depths", EnemyPool = new[] { "fire_imp", "goblin_bomber", "cave_spider", "crowned_slime", "skeleton" }, MinEnemies = 3, MaxEnemies = 3,
                MinSpikes = 2, MaxSpikes = 2, MinBombs = 1, MaxBombs = 2, Chests = 1, MinPotions = 1, MaxPotions = 1,
                Vault = true, MinLava = 1, MaxLava = 2, Teleports = true, KeyWarden = true, Fountains = 1,
            });
            c.FloorProfiles.Add(new FloorProfile
            {
                FloorIndex = 9, Name = "The Echoing Dark", EnemyPool = new[] { "cave_spider", "goblin_bomber", "fire_imp", "skeleton", "mimic_chest" }, MinEnemies = 3, MaxEnemies = 3,
                MinSpikes = 2, MaxSpikes = 2, MinBombs = 1, MaxBombs = 1, Chests = 1, MinPotions = 1, MaxPotions = 1,
                Teleports = true,
            });
            c.FloorProfiles.Add(new FloorProfile
            {
                FloorIndex = 10, Name = "The Bat Roost", IsBoss = true, BossId = "bat_swarm_leader", MinPotions = 1, MaxPotions = 1, MinExitDistance = 3,
                MinSpikes = 1, MaxSpikes = 1, MinBombs = 0, MaxBombs = 1,
            });
            // ---- Act III: the haunted stage. New: Spooky Spellbook (11), Armored Boar (12).
            c.FloorProfiles.Add(new FloorProfile
            {
                FloorIndex = 11, Name = "The Haunted Library", EnemyPool = new[] { "spooky_spellbook", "spooky_spellbook", "skeleton", "fire_imp" }, MinEnemies = 2, MaxEnemies = 3,
                MinSpikes = 1, MaxSpikes = 2, MinBombs = 1, MaxBombs = 1, Chests = 1, MinPotions = 1, MaxPotions = 1,
                Vault = true, Fountains = 1,
            });
            c.FloorProfiles.Add(new FloorProfile
            {
                FloorIndex = 12, Name = "The Boar Warrens", EnemyPool = new[] { "armored_boar", "armored_boar", "goblin_bomber", "skeleton" }, MinEnemies = 3, MaxEnemies = 3,
                MinSpikes = 1, MaxSpikes = 2, MinBombs = 1, MaxBombs = 1, Chests = 1, MinPotions = 1, MaxPotions = 1,
                Vault = true, Teleports = true, Fountains = 1,
            });
            c.FloorProfiles.Add(new FloorProfile
            {
                FloorIndex = 13, Name = "The Forgotten Archive", EnemyPool = new[] { "spooky_spellbook", "cave_spider", "armored_boar", "mimic_chest", "fire_imp" }, MinEnemies = 3, MaxEnemies = 3,
                MinSpikes = 2, MaxSpikes = 2, MinBombs = 1, MaxBombs = 2, Chests = 1, MinPotions = 1, MaxPotions = 1,
                Vault = true, MinLava = 1, MaxLava = 1, Teleports = true,
            });
            c.FloorProfiles.Add(new FloorProfile
            {
                FloorIndex = 14, Name = "The Backstage", EnemyPool = new[] { "spooky_spellbook", "armored_boar", "cave_spider", "goblin_bomber" }, MinEnemies = 3, MaxEnemies = 3,
                MinSpikes = 2, MaxSpikes = 2, MinBombs = 1, MaxBombs = 2, Chests = 1, MinPotions = 1, MaxPotions = 1,
                KeyWarden = true, Teleports = true, Fountains = 1,
            });
            c.FloorProfiles.Add(new FloorProfile
            {
                FloorIndex = 15, Name = "The Grand Stage", IsBoss = true, BossId = "theater_curtain_demon", MinPotions = 1, MaxPotions = 1, MinExitDistance = 3,
                MinSpikes = 1, MaxSpikes = 2, MinBombs = 0, MaxBombs = 1,
            });
            // ---- Act IV: the royal descent. Nothing new: every monster, and more of them, on the way to Lord Blobert.
            c.FloorProfiles.Add(new FloorProfile
            {
                FloorIndex = 16, Name = "The Deep Halls", EnemyPool = new[] { "goblin", "skeleton", "cave_spider", "goblin_bomber", "fire_imp", "crowned_slime" }, MinEnemies = 3, MaxEnemies = 4,
                MinSpikes = 2, MaxSpikes = 2, MinBombs = 1, MaxBombs = 2, Chests = 1, MinPotions = 1, MaxPotions = 1,
                Vault = true, Teleports = true, Fountains = 1,
            });
            c.FloorProfiles.Add(new FloorProfile
            {
                FloorIndex = 17, Name = "The Molten Depths", EnemyPool = new[] { "fire_imp", "fire_imp", "goblin_bomber", "armored_boar", "skeleton", "mimic_chest" }, MinEnemies = 3, MaxEnemies = 4,
                MinSpikes = 2, MaxSpikes = 2, MinBombs = 1, MaxBombs = 2, Chests = 1, MinPotions = 1, MaxPotions = 1,
                Vault = true, MinLava = 1, MaxLava = 2, Teleports = true,
            });
            c.FloorProfiles.Add(new FloorProfile
            {
                FloorIndex = 18, Name = "The Sealed Vaults", EnemyPool = new[] { "spooky_spellbook", "cave_spider", "armored_boar", "skeleton", "crowned_slime" }, MinEnemies = 3, MaxEnemies = 4,
                MinSpikes = 2, MaxSpikes = 2, MinBombs = 1, MaxBombs = 2, Chests = 1, MinPotions = 1, MaxPotions = 1,
                Vault = true, MinLava = 1, MaxLava = 2, Teleports = true, KeyWarden = true, Fountains = 1,
            });
            c.FloorProfiles.Add(new FloorProfile
            {
                FloorIndex = 19, Name = "The Royal Approach", EnemyPool = new[] { "armored_boar", "spooky_spellbook", "cave_spider", "goblin_bomber", "skeleton", "fire_imp", "mimic_chest" }, MinEnemies = 4, MaxEnemies = 4,
                MinSpikes = 2, MaxSpikes = 2, MinBombs = 1, MaxBombs = 2, Chests = 1, MinPotions = 1, MaxPotions = 1,
                Vault = true, MinLava = 1, MaxLava = 2, Teleports = true, Fountains = 1,
            });
            c.FloorProfiles.Add(new FloorProfile
            {
                FloorIndex = 20, Name = "Blobert's Court", IsBoss = true, BossId = "lord_blobert", MinPotions = 1, MaxPotions = 1, MinExitDistance = 3,
                MinSpikes = 2, MaxSpikes = 2, MinBombs = 1, MaxBombs = 1,
            });

            // Chests cost 2-4 turns each (D-022 amendment, measured with ChestWorth). Potions stay single: at two per draw
            // a looting run ended with ~11 unused, so that weight went to max HP instead.
            c.ChestRewards.Add(new RewardEntry { Kind = RewardKind.Potion, Amount = 1, Weight = 2 });
            c.ChestRewards.Add(new RewardEntry { Kind = RewardKind.MaxHp, Amount = 3, Weight = 3 });
            c.ChestRewards.Add(new RewardEntry { Kind = RewardKind.SlashDamage, Amount = 1, Weight = 1 });

            // ---------------------------------------------------------------- usable skills (D-075)
            // The first verb the game has had. Every one of the ninety-six talents is passive, so a class could only ever
            // be expressed as a modifier on a slash or a shield; these are things a player DOES, on a turn they choose,
            // for mana. One a branch at tier 2, so a build that climbs all three earns all three.
            // Knight - the line holds, and then it moves.
            Skill(c, "kni_shockwave", "knight", "Shockwave", "2 damage to everything beside you.",
                manaCost: 3, effect: SkillEffect.Burst, amount: 2, target: SkillTarget.Self);
            Skill(c, "kni_rally", "knight", "Rally", "Mend 2 hearts.",
                manaCost: 3, effect: SkillEffect.Heal, amount: 2, target: SkillTarget.Self);
            Skill(c, "kni_shield_bash", "knight", "Shield Bash", "Leave one monster beside you reeling.",
                manaCost: 2, effect: SkillEffect.Stagger, amount: 0, target: SkillTarget.Enemy);

            // Paladin - the hammer, the shield and the hands.
            Skill(c, "pal_smite", "paladin", "Smite", "4 damage to one monster.",
                manaCost: 4, effect: SkillEffect.Strike, amount: 4, target: SkillTarget.Enemy);
            Skill(c, "pal_bulwark", "paladin", "Bulwark", "3 damage to everything beside you.",
                manaCost: 4, effect: SkillEffect.Burst, amount: 3, target: SkillTarget.Self);
            Skill(c, "pal_lay_on_hands", "paladin", "Lay on Hands", "Mend 3 hearts.",
                manaCost: 3, effect: SkillEffect.Heal, amount: 3, target: SkillTarget.Self);

            // Rogue - openings, not fights.
            Skill(c, "rog_throat_cut", "rogue", "Throat Cut", "5 damage to one monster beside you.",
                manaCost: 4, effect: SkillEffect.Strike, amount: 5, target: SkillTarget.Enemy);
            Skill(c, "rog_smoke", "rogue", "Smoke Bomb", "Leave one monster reeling.",
                manaCost: 2, effect: SkillEffect.Stagger, amount: 0, target: SkillTarget.Enemy, range: 2);
            Skill(c, "rog_bandage", "rogue", "Bandage", "Mend 3 hearts.",
                manaCost: 3, effect: SkillEffect.Heal, amount: 3, target: SkillTarget.Self);

            // Wizard - reach, sight and a shove.
            Skill(c, "wiz_firebolt", "wizard", "Firebolt", "4 damage at three tiles.",
                manaCost: 3, effect: SkillEffect.Strike, amount: 4, target: SkillTarget.Enemy, range: 3);
            Skill(c, "wiz_nova", "wizard", "Arcane Nova", "3 damage to everything beside you.",
                manaCost: 4, effect: SkillEffect.Burst, amount: 3, target: SkillTarget.Self);
            Skill(c, "wiz_concussion", "wizard", "Concussion", "Leave one monster reeling, at two tiles.",
                manaCost: 3, effect: SkillEffect.Stagger, amount: 0, target: SkillTarget.Enemy, range: 2);

            // Ranger - distance, and what grows by the path.
            Skill(c, "ran_aimed_shot", "ranger", "Aimed Shot", "5 damage at three tiles.",
                manaCost: 4, effect: SkillEffect.Strike, amount: 5, target: SkillTarget.Enemy, range: 3);
            Skill(c, "ran_poultice", "ranger", "Poultice", "Mend 3 hearts.",
                manaCost: 3, effect: SkillEffect.Heal, amount: 3, target: SkillTarget.Self);
            Skill(c, "ran_snare", "ranger", "Snare", "Leave one monster reeling, at three tiles.",
                manaCost: 3, effect: SkillEffect.Stagger, amount: 0, target: SkillTarget.Enemy, range: 3);

            // Cleric - mend, ward, and the answer to the risen.
            Skill(c, "cle_mend", "cleric", "Mend", "Mend 3 hearts.",
                manaCost: 3, effect: SkillEffect.Heal, amount: 3, target: SkillTarget.Self);
            Skill(c, "cle_ward", "cleric", "Ward", "2 damage to everything beside you.",
                manaCost: 3, effect: SkillEffect.Burst, amount: 2, target: SkillTarget.Self);
            Skill(c, "cle_dispel", "cleric", "Dispel Undead", "3 damage, doubled against the risen.",
                manaCost: 3, effect: SkillEffect.Banish, amount: 3, target: SkillTarget.Enemy, range: 2);

            // Berserker - forward, always.
            Skill(c, "ber_whirl", "berserker", "Whirlwind", "3 damage to everything beside you.",
                manaCost: 3, effect: SkillEffect.Burst, amount: 3, target: SkillTarget.Self);
            Skill(c, "ber_second_wind", "berserker", "Second Wind", "Mend 4 hearts.",
                manaCost: 4, effect: SkillEffect.Heal, amount: 4, target: SkillTarget.Self);
            Skill(c, "ber_warcry", "berserker", "War Cry", "Leave one monster reeling.",
                manaCost: 2, effect: SkillEffect.Stagger, amount: 0, target: SkillTarget.Enemy);

            // Engineer - the drone, the toolkit and the sweep.
            Skill(c, "eng_discharge", "engineer", "Discharge", "3 damage to everything beside you.",
                manaCost: 3, effect: SkillEffect.Burst, amount: 3, target: SkillTarget.Self);
            Skill(c, "eng_repair", "engineer", "Field Repairs", "Mend 3 hearts.",
                manaCost: 3, effect: SkillEffect.Heal, amount: 3, target: SkillTarget.Self);
            Skill(c, "eng_emp", "engineer", "EMP Charge", "Leave one monster reeling, at two tiles.",
                manaCost: 2, effect: SkillEffect.Stagger, amount: 0, target: SkillTarget.Enemy, range: 2);

            c.Difficulties[Difficulty.Easy] = new DifficultyDefinition
            {
                Id = Difficulty.Easy, DisplayName = "Squire's Stroll", Tagline = "More hearts, softer hits and a breather on every stair.",
                HeroMaxHp = 4, StartingPotions = 1, EnemyDamage = -1, HazardDamage = -1, BossHp = -2, BossSlamDamage = -1, FloorClearHeal = 3,
                MercyOnStairs = 2,
                // Time, and fewer bodies (D-074): two turns to walk away from a bomb, a shorter stretch of untouchable
                // Blobert and a longer one with his guard down, one minion a summon, and bones that stay down longer.
                WebTurns = 1, BombFuse = 2, BossPuffTurns = -1, DeflatedTurns = 2,
                SummonCount = -1, ReassembleTurns = 1, MaxMinions = 0,
                FirstRunHearts = 3, FirstRunPotions = 1,
            };
            c.Difficulties[Difficulty.Medium] = new DifficultyDefinition
            {
                Id = Difficulty.Medium, DisplayName = "Knight's Trial", Tagline = "Biting traps and an extra monster a floor. Click carefully.",
                // D-038: each floor holds one more monster. D-050: traps hit one harder again, measured with a bot that no
                // longer stalls (D-048/D-049) — blind casual wins ~78%, novice ~35% (rules §10.2).
                // D-071: the stairs bring a badly hurt hero back to half, which is what makes the fragile classes
                // playable for a careless player without touching what a sharp one gets.
                ExtraEnemies = 1, HazardDamage = 1, MercyOnStairs = 2,
                // The base numbers: every other tier is written as a distance from these (D-074).
                WebTurns = 1, BombFuse = 1, BossPuffTurns = 0, DeflatedTurns = 1,
                SummonCount = 0, ReassembleTurns = 0, MaxMinions = 0,
                FirstRunHearts = 3, FirstRunPotions = 1,
            };
            c.Difficulties[Difficulty.Hardcore] = new DifficultyDefinition
            {
                Id = Difficulty.Hardcore, DisplayName = "Blobert's Wrath", Tagline = "Tougher monsters, a mightier Blobert, one potion. No mercy.",
                // D-050: Knight's Trial caught up on traps, so the gap here is the monsters, the boss and the missing potion.
                // Blind casual AutoPlayer wins ~45%, novice ~15% (rules §10.2).
                // No mercy is what the card says, so the stairs give none here: D-071's floor is Squire's Stroll's and
                // Knight's Trial's, and this tier is the one that is meant to make carelessness cost (DATA-52).
                StartingPotions = -1, EnemyHp = 1, BossHp = 4, BossSlamDamage = 1, HazardDamage = 1, MercyOnStairs = 0,
                // Less time, more bodies (D-074): a web that holds for two turns, a third turn of untouchable Blobert,
                // an extra minion every summon, a bigger swarm kept alive, and bones back on their feet a turn sooner.
                WebTurns = 2, BombFuse = 1, BossPuffTurns = 1, DeflatedTurns = 1,
                SummonCount = 1, ReassembleTurns = -1, MaxMinions = 1,
                // None here, for the same reason the stairs give none (D-073): this tier is opt-in, its card says so,
                // and nobody meets the game for the first time on it by accident.
                FirstRunHearts = 0, FirstRunPotions = 0,
            };
            return c;
        }

        void ApplyDifficulty(DifficultyDefinition d)
        {
            Difficulty = d.Id;
            FloorClearHeal = Math.Max(0, d.FloorClearHeal);
            MercyOnStairs = Math.Max(0, d.MercyOnStairs);
            // The behavioural numbers (D-074). A fuse under 1 would go off in the same turn it was thrown, which is a
            // blow with no warning, so the floor here is the telegraph contract and not a taste call (rules 3.2).
            FirstRunHearts = Math.Max(0, d.FirstRunHearts);
            FirstRunPotions = Math.Max(0, d.FirstRunPotions);
            WebTurns = Math.Max(1, d.WebTurns);
            DeflatedTurns = Math.Max(1, d.DeflatedTurns);
            Hazards.BombFuse = Math.Max(1, d.BombFuse);

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
                // Behaviour, not size (D-074). Each stays at least 1 where the monster had one at all, so a tier can
                // soften a rule without switching it off: a summoner with no summon keeps none, and one with a summon
                // never brings nobody.
                if (enemy.IsBoss) enemy.PuffTurns = Math.Max(1, enemy.PuffTurns + d.BossPuffTurns);
                if (enemy.SummonId != null) enemy.SummonCount = Math.Max(1, enemy.SummonCount + d.SummonCount);
                if (enemy.MaxMinions > 0) enemy.MaxMinions = Math.Max(1, enemy.MaxMinions + d.MaxMinions);
                if (enemy.Reassembles) enemy.ReassembleTurns = Math.Max(1, enemy.ReassembleTurns + d.ReassembleTurns);
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
