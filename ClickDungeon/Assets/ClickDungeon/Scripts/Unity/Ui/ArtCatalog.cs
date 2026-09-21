using System;
using System.Collections.Generic;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using UnityEngine;

namespace ClickDungeon.Unity.Ui
{
    /// <summary>
    /// Production art keyed by file name (decision D-016). Built by the editor from
    /// Assets/ClickDungeon/Art/Runtime; lives in Art/Resources so code-built UI can load it without scene references.
    /// </summary>
    [CreateAssetMenu(menuName = "ClickDungeon/Art Catalog", fileName = "ArtCatalog")]
    public sealed class ArtCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string Key;
            public Sprite[] Frames = Array.Empty<Sprite>();
            public float Fps = 10f;
            /// <summary>True for reference slices in Art/Runtime/Placeholders (art brief D4), not production art.</summary>
            public bool Placeholder;
        }

        [SerializeField] List<Entry> _entries = new List<Entry>();
        Dictionary<string, Entry> _lookup;

        public IReadOnlyList<Entry> Entries => _entries;

        public void SetEntries(IEnumerable<Entry> entries)
        {
            _entries = new List<Entry>(entries);
            _lookup = null;
        }

        public bool TryGet(string key, out Entry entry)
        {
            if (_lookup == null)
            {
                _lookup = new Dictionary<string, Entry>();
                foreach (var e in _entries)
                {
                    if (e == null || string.IsNullOrEmpty(e.Key) || e.Frames == null || e.Frames.Length == 0 || e.Frames[0] == null) continue;
                    _lookup[e.Key] = e;
                }
            }
            return _lookup.TryGetValue(key, out entry);
        }

        void OnValidate() => _lookup = null;
    }

    /// <summary>
    /// Runtime access to production art. A missing key returns false so callers keep drawing the procedural
    /// placeholder: art can arrive piece by piece without breaking the game.
    /// </summary>
    public static class Art
    {
        public const string CatalogResourceName = "ArtCatalog";

        static ArtCatalog _catalog;
        static bool _loaded;

        public static ArtCatalog Catalog
        {
            get
            {
                if (!_loaded)
                {
                    _catalog = Resources.Load<ArtCatalog>(CatalogResourceName);
                    _loaded = true;
                }
                return _catalog;
            }
        }

        public static bool TryGet(string key, out ArtCatalog.Entry entry)
        {
            entry = null;
            var catalog = Catalog;
            return catalog != null && !string.IsNullOrEmpty(key) && catalog.TryGet(key, out entry);
        }

        public static bool TryGetSprite(string key, out Sprite sprite)
        {
            sprite = TryGet(key, out var entry) ? entry.Frames[0] : null;
            return sprite != null;
        }

        public static bool Has(string key) => TryGet(key, out _);

        /// <summary>Use a specific catalog (tests, tools). Pass null to force placeholders.</summary>
        public static void Override(ArtCatalog catalog)
        {
            _catalog = catalog;
            _loaded = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            _catalog = null;
            _loaded = false;
        }
    }

    /// <summary>Art key naming (docs/art-brief.md, appendix). Keys are file names without extension.</summary>
    public static class ArtKeys
    {
        /// <summary>The default playable hero: whose art stands in when a hero has none of its own yet.</summary>
        public const string HeroId = ContentCatalog.DefaultHeroId;
        /// <summary>The mascot painted into the title and HUD backgrounds (D-057). Not a playable hero.</summary>
        public const string MascotId = ContentCatalog.MascotId;

        public const string FloorStone = "tile_floor_stone";
        public const string Wall = "tile_wall";
        public const string Pit = "tile_pit";
        public const string Spikes = "tile_spikes";
        public const string Bomb = "tile_bomb";
        public const string BombArmed = "tile_bomb_armed";
        public const string Key = "tile_key";
        public const string ChestClosed = "tile_chest_closed";
        public const string ChestOpen = "tile_chest_open";
        /// <summary>The premium chest a special key hides in a run (D-026), and the lock badge it wears until opened.</summary>
        public const string PremiumChestClosed = "tile_chest_premium_closed";
        public const string PremiumChestOpen = "tile_chest_premium_open";
        public const string SpecialLockBadge = "icon_special_lock";
        public const string SpecialKeyIcon = "icon_key_special";
        public const string Potion = "tile_potion";
        public const string ExitLocked = "tile_exit_locked";
        /// <summary>The way in, as the reference draws it: a raised stone staircase climbing up out of the floor.</summary>
        public const string EntranceStairs = "tile_entrance_stairs";
        public const string ExitOpen = "tile_exit_open";
        // Production tile set (rules §11). The sheets name the exit "stair down", so both spellings are accepted.
        public const string StairDownLocked = "tile_stair_down_locked";
        public const string StairDown = "tile_stair_down";
        public const string StairUp = "tile_stair_up";
        public const string TrapPit = "tile_trap_pit";
        public const string TrapSpike = "tile_trap_spike";
        public const string TrapBomb = "tile_trap_bomb";
        public const string Lava = "tile_lava";
        public const string Water = "tile_water";
        /// <summary>The second swirl in the tile sheets: another teleport pad, not an unseen tile.</summary>
        public const string Shadow = "tile_shadow";
        public const string FloorCracked = "tile_floor_cracked";
        public const string FloorMoss = "tile_floor_moss";
        public const string WallCorner = "tile_wall_corner";
        public const string TorchWall = "tile_torch";
        public const string DoorLocked = "tile_door_locked";
        public const string DoorOpen = "tile_door_open";
        public const string PressurePlate = "tile_pressure_plate";
        public const string Teleport = "tile_teleport";
        public const string FountainHeal = "tile_fountain_heal";
        public const string Logo = "logo_clickdungeon";
        public const string TitleBackground = "bg_title";
        /// <summary>Portrait backgrounds: the scene between the landscape art's painted HUD rows (make_portrait_backgrounds.py).</summary>
        public const string TitleBackgroundPortrait = "bg_title_portrait";
        public const string GameplayBackgroundPortrait = "bg_gameplay_portrait";
        public const string GameplayBackground = "bg_gameplay";
        public const string DangerWarning = "icon_danger_warning";

        public const string HighlightLegal = "ui_highlight_legal";
        public const string HighlightTarget = "ui_highlight_target";
        public const string HighlightHover = "ui_highlight_hover";
        public const string PortraitFrame = "ui_frame_portrait";
        /// <summary>The purse: coins come out of the dungeon, gems off Lord Blobert (D-025).</summary>
        public const string CoinIcon = "ui_icon_gold";
        public const string GemIcon = "ui_icon_gem";
        public const string HpBack = "ui_hp_back";
        /// <summary>The bar over a monster's token (the monster pack's enemy_health_bar).</summary>
        public const string EnemyHpBack = "ui_enemy_hp_back";
        public const string EnemyHpFill = "ui_enemy_hp_fill";
        public const string HpFill = "ui_hp_fill";
        /// <summary>The mana bar (D-032): the reference's blue fill and orb, on the HP bar's back and frame.</summary>
        public const string ManaFill = "ui_mana_fill";
        public const string ManaIcon = "ui_icon_mana";
        public const string HpFrame = "ui_hp_frame";
        public const string Heart = "ui_icon_heart";
        public const string Chip = "ui_chip";
        /// <summary>Not "ui_floor_plaque": that reference slice has baked sample text.</summary>
        public const string FloorPlaque = "ui_plaque_floor";
        public const string Panel = "ui_panel";
        public const string SpeechStrip = "ui_speech_strip";
        public const string AbilityButtonDefault = "ui_button_ability";
        public const string AbilitySelected = "ui_button_ability_selected";
        public const string CountBadge = "ui_badge_count";
        public const string SettingsButton = "ui_button_settings";
        public const string HelpButton = "ui_button_help";

        public const string ModalPanel = "ui_modal_panel";
        public const string ButtonPrimary = "ui_button_primary";
        public const string ButtonSecondary = "ui_button_secondary";
        public const string ButtonDanger = "ui_button_danger";
        public const string ChestLargeClosed = "ui_chest_large_closed";
        public const string ChestLargeOpen = "ui_chest_large_open";
        public const string ChestGlow = "fx_chest_glow";
        public const string ChestProgressBack = "ui_chest_progress_back";
        public const string ChestProgressFill = "ui_chest_progress_fill";
        public const string ChestRewardCard = "ui_chest_reward_card";

        public const string TitleHeroCard = "ui_title_hero_card";
        public const string TitlePlank = "ui_title_tagline_plank";
        public const string TitleBanner = "ui_title_banner";
        public const string TitleContinuePanel = "ui_title_continue_panel";
        public const string ContinuePreview = "ui_continue_preview";
        /// <summary>The daily reward (D-029): the reference's panel frame, its glowing chest and its CLAIM button with the red "!".</summary>
        public const string TitleDailyPanel = "ui_title_daily_panel";
        /// <summary>
        /// The reference title's own hero card, CONTINUE panel and mail button with their sample text painted out (D-033):
        /// laid over the unblurred background in their exact places, with the live name, level, purse and floor on top.
        /// </summary>
        public const string TitleNamePlate = "ui_title_name_plate";
        /// <summary>
        /// The reference gameplay screen's own level shield, floor plaque, purse fields and ability buttons, sample text
        /// painted out (D-034), laid over the unblurred background in their exact places.
        /// </summary>
        public const string HudLevelBadge = "ui_hud_level_badge";
        public const string HudPlaque = "ui_hud_plaque";
        public const string HudCoinField = "ui_hud_coin_field";
        public const string HudGemField = "ui_hud_gem_field";
        public static string HudAbility(CommandKind kind) => "ui_hud_ability_" + kind.ToString().ToLowerInvariant();
        public const string TitleLevelBadge = "ui_title_level_badge";
        public const string TitleCoinField = "ui_title_coin_field";
        public const string TitleGemField = "ui_title_gem_field";
        public const string TitleContinueClean = "ui_title_continue_clean";
        public const string TitleMailClean = "ui_title_mail_clean";
        public const string DailyRewardChest = "ui_daily_reward_chest";
        public const string ButtonClaim = "ui_button_claim";
        public const string MenuButton = "ui_button_menu";
        /// <summary>The crown (achievements) and mail buttons (D-030), and the reference's red "!" on its own, shown on the mail
        /// button only while a letter wants reading or holds a gift.</summary>
        public const string CrownButton = "ui_button_crown";
        public const string MailButton = "ui_button_mail";
        public const string AlertBadge = "ui_badge_alert";
        /// <summary>The game screen's INVENTORY / TALENTS / SHOP bar (its "!" painted out) and the purse's "+" button.</summary>
        public const string NavBar = "ui_nav_bar";
        public const string PlusButton = "ui_button_plus";
        public const string TitleHero = "ui_title_hero";
        public const string TitleBlobert = "ui_title_blobert";
        public const string TitleGoblin = "ui_title_goblin";
        public const string ButtonPlay = "ui_button_play";
        public const string ButtonTitleSettings = "ui_button_title_settings";
        public const string ButtonQuit = "ui_button_quit";
        /// <summary>Title buttons for the systems built so far (D-024, D-025); each carries its own icon and label.</summary>
        public const string ButtonHeroSelect = "ui_button_hero_select";
        public const string ButtonShop = "ui_button_shop";
        /// <summary>TALENTS, and the same button with its red "!" for when a talent point is waiting (D-027).</summary>
        public const string ButtonTalents = "ui_button_talents";
        public const string ButtonInventory = "ui_button_inventory";
        public const string ButtonTalentsAlert = "ui_button_talents_alert";
        public const string PlayIcon = "ui_icon_play";
        public const string SettingsIcon = "ui_icon_settings";
        public const string QuitIcon = "ui_icon_quit";

        public static readonly string[] ModalStyles = { "victory", "defeat" };

        public const string ChestRays = "fx_chest_rays";
        public const string ChestCoin = "fx_chest_coin";
        public const string ChestGem = "fx_chest_gem";
        public const string ChestShimmer = "fx_chest_shimmer";

        public const string UnderfootSpikes = "icon_underfoot_spikes";
        public const string UnderfootBomb = "icon_underfoot_bomb";
        public const string UnderfootBombArmed = "icon_underfoot_bomb_armed";
        public const string UnderfootExitLocked = "icon_underfoot_exit_locked";
        public const string UnderfootExitOpen = "icon_underfoot_exit_open";

        // Kept for later, not drawn since D-045: sparkle stars on every key, potion and spike step.
        public const string FxSpikesTrigger = "fx_spikes_trigger";
        public const string FxExplosion = "fx_explosion";
        public const string FxKeyCollect = "fx_key_collect";
        public const string FxPotionCollect = "fx_potion_collect";
        public const string FxExitUnlock = "fx_exit_unlock";
        public const string FxEnemyWake = "fx_enemy_wake";
        /// <summary>Looping fuse drawn over an armed bomb tile.</summary>
        public const string BombFuse = "fx_bomb_fuse";

        public const string FloorBanner = "ui_banner_floor";
        public const string FloorBannerBoss = "ui_banner_floor_boss";

        /// <summary>Sir Clickington's chest reactions, in order: overlay opens, burst, then two beats after it.</summary>
        public static readonly string[] ChestReactionSteps = { "anticipation", "reveal", "heavy", "triumph" };

        public static readonly RewardKind[] RewardKinds = { RewardKind.Potion, RewardKind.MaxHp, RewardKind.SlashDamage };

        /// <summary>One-shot action animations (art brief §8), keyed actor_&lt;id&gt;_&lt;name&gt; with numbered frames.</summary>
        public static readonly string[] HeroAnimations = { "step", "slash", "shield", "dash", "hit", "potion", "victory", "defeat" };
        public static readonly string[] EnemyAnimations = { "wake", "move", "attack", "hit", "defeat" };

        /// <summary>Actor-specific poses and animations beyond the shared set.</summary>
        public static readonly Dictionary<string, string[]> ActorExtras = new Dictionary<string, string[]>
        {
            ["crowned_slime"] = new[] { "rest" },
            ["fire_imp"] = new[] { "fire" },
            ["slimelet"] = new[] { "spawn" },
            ["lord_blobert"] = new[] { "boast", "slam", "summon", "puffup", "immune", "deflate" },
            // The act bosses (D-062) boast while a slam is telegraphed, as Blobert does.
            ["goblin_brute_king"] = new[] { "boast" },
            ["bat_swarm_leader"] = new[] { "boast" },
            ["theater_curtain_demon"] = new[] { "boast" },
        };

        public static readonly CommandKind[] AbilityKinds =
            { CommandKind.Move, CommandKind.Slash, CommandKind.Shield, CommandKind.Dash, CommandKind.Potion };

        public static readonly IntentKind[] IntentKinds =
        {
            IntentKind.Attack, IntentKind.Move, IntentKind.Fire, IntentKind.Rest,
            IntentKind.Recover, IntentKind.Summon, IntentKind.Slam, IntentKind.PuffUp,
        };

        public static readonly ThreatKind[] ThreatKinds =
        {
            ThreatKind.Attack, ThreatKind.Fire, ThreatKind.Slam, ThreatKind.BombBlast, ThreatKind.BombArmed, ThreatKind.Summon,
        };

        public static readonly string[] Expressions =
            { "neutral", "happy", "confident", "worried", "shocked", "angry", "victorious", "defeated" };

        public static string Actor(string contentId, string state = "idle") => $"actor_{contentId}_{state}";

        /// <summary>An item's icon (D-028), for the INVENTORY screen and the item found in a run.</summary>
        public static string ItemIcon(string itemId) => $"icon_item_{itemId}";

        /// <summary>A class talent's picture (D-037).</summary>
        public static string TalentIcon(string talentId) => $"icon_talent_{talentId}";

        /// <summary>A hero's full-body art for Hero Select (D-037), playable or coming soon.</summary>
        public static string HeroArt(string heroId) => $"portrait_{heroId}_full";

        public const string LockIcon = "ui_icon_lock";

        /// <summary>The items library's rarity frames (D-036), clear in the middle so the item shows through.</summary>
        public static string RarityFrame(ItemRarity rarity) => "ui_frame_rarity_" + rarity.ToString().ToLowerInvariant();

        /// <summary>The picture on a shop card (D-036): the items library's potions, scrolls and chests, or the HUD's own icons.</summary>
        public static string ShopIcon(ClickDungeon.Application.ShopItem item)
        {
            switch (item)
            {
                case ClickDungeon.Application.ShopItem.HeartToken: return Heart;
                case ClickDungeon.Application.ShopItem.SpecialKey: return SpecialKeyIcon;
                case ClickDungeon.Application.ShopItem.CoinPouch: return CoinIcon;
                case ClickDungeon.Application.ShopItem.GemPouch: return GemIcon;
                default:
                    var name = item.ToString();
                    var snake = new System.Text.StringBuilder();
                    for (int i = 0; i < name.Length; i++)
                    {
                        if (i > 0 && char.IsUpper(name[i])) snake.Append('_');
                        snake.Append(char.ToLowerInvariant(name[i]));
                    }
                    return "icon_shop_" + snake;
            }
        }

        /// <summary>A monster's portrait icon, shown in the Inspect panel while hovering it (the monster pack's <c>*_icon</c>).</summary>
        public static string EnemyPortrait(string enemyId) => $"portrait_{enemyId}";

        public static string ClueIcon(Clue flag) => $"icon_clue_{flag.ToString().ToLowerInvariant()}";

        public static string AbilityIcon(CommandKind kind) => $"icon_ability_{kind.ToString().ToLowerInvariant()}";

        public static string ChestReaction(string step) => $"ui_chest_reaction_{step}";

        /// <summary>Underfoot badge for a hazard or exit under a token; null when nothing needs a badge.</summary>
        public static string Underfoot(CellState cell, bool exitUnlocked)
        {
            if (cell.Hazard == HazardKind.Spikes) return UnderfootSpikes;
            if (cell.Hazard == HazardKind.Bomb) return cell.BombArmed ? UnderfootBombArmed : UnderfootBomb;
            if (cell.IsExit) return exitUnlocked ? UnderfootExitOpen : UnderfootExitLocked;
            return null;
        }

        /// <summary>Portrait expression shown in a frame when a reaction pose has no art.</summary>
        public static string ChestReactionFallback(string step)
        {
            switch (step)
            {
                case "anticipation": return "confident";
                case "reveal": return "shocked";
                case "heavy": return "worried";
                default: return "victorious";
            }
        }

        /// <summary>Reward card icon, e.g. icon_reward_maxhp.</summary>
        public static string RewardIcon(RewardKind kind) => $"icon_reward_{kind.ToString().ToLowerInvariant()}";

        /// <summary>Existing art to use when a reward has no icon of its own.</summary>
        public static string RewardIconFallback(RewardKind kind)
        {
            switch (kind)
            {
                case RewardKind.Potion: return Potion;
                case RewardKind.MaxHp: return Heart;
                default: return AbilityIcon(CommandKind.Slash);
            }
        }

        /// <summary>Styled modal panel, e.g. ui_modal_panel_victory; falls back to ui_modal_panel.</summary>
        public static string ModalPanelStyle(string style) => $"ui_modal_panel_{style.ToLowerInvariant()}";

        /// <summary>Modal buttons pick art by role from their fill colour: green = primary, red = danger, else secondary.</summary>
        public static string ModalButton(Color fill) =>
            fill == Palette.PlayGreen ? ButtonPrimary : fill == Palette.QuitRed ? ButtonDanger : ButtonSecondary;

        /// <summary>Ability button frame and fill (icon, label, hotkey and badge draw on top).</summary>
        public static string AbilityButton(CommandKind kind) => $"ui_button_ability_{kind.ToString().ToLowerInvariant()}";

        public static string Portrait(string heroId, string expression) => $"portrait_{heroId}_{expression.ToLowerInvariant()}";

        /// <summary>Icon shown at the left of an enemy intent badge, e.g. icon_intent_attack, icon_intent_puffup.</summary>
        public static string IntentIcon(IntentKind kind)
        {
            switch (kind)
            {
                case IntentKind.None: return null;
                // The first expansion monsters' intents (D-058) borrow the nearest icon rather than looking up art that
                // does not exist: a charge is a blow, and lying as bones is a kind of recovering. A throw has none - its
                // BOMB badge and the marked landing tile say it plainly.
                case IntentKind.Charge: return IntentIcon(IntentKind.Attack);
                case IntentKind.Reassemble: return IntentIcon(IntentKind.Recover);
                case IntentKind.Throw: return null;
                // D-061/D-062 likewise: a web has no icon (its WEB badge and marked tile say it), a vanish is a kind of move.
                case IntentKind.Web: return null;
                case IntentKind.Vanish: return IntentIcon(IntentKind.Move);
                default: return $"icon_intent_{kind.ToString().ToLowerInvariant()}";
            }
        }

        /// <summary>Tile-sized telegraph overlay, e.g. ui_danger_attack, ui_danger_blast.</summary>
        public static string DangerOverlay(ThreatKind kind)
        {
            switch (kind)
            {
                case ThreatKind.BombBlast: return "ui_danger_blast";
                case ThreatKind.BombArmed: return "ui_danger_armed";
                default: return $"ui_danger_{kind.ToString().ToLowerInvariant()}";
            }
        }

        /// <summary>Every key the game currently looks up (used by the coverage report).</summary>
        public static List<string> Wired(ContentCatalog catalog)
        {
            var keys = new List<string>
            {
                FloorStone, Wall, Pit, Spikes, Bomb, BombArmed, Key, ChestClosed, ChestOpen, PremiumChestClosed, PremiumChestOpen,
                SpecialLockBadge, SpecialKeyIcon, Potion, EntranceStairs, ExitLocked, ExitOpen,
                StairDownLocked, StairDown, StairUp, TrapPit, TrapSpike, TrapBomb, Lava, Water, Shadow, FloorCracked, FloorMoss,
                WallCorner, TorchWall, DoorLocked, DoorOpen, PressurePlate, Teleport, FountainHeal,
                Logo, TitleBackground, GameplayBackground, TitleBackgroundPortrait, GameplayBackgroundPortrait,
            };
            foreach (var enemy in catalog.Enemies.Values) keys.Add(Actor(enemy.Id));
            foreach (var enemy in catalog.Enemies.Values) keys.Add(EnemyPortrait(enemy.Id));
            foreach (var item in catalog.Items) keys.Add(ItemIcon(item.Id));
            foreach (var talent in catalog.Talents) keys.Add(TalentIcon(talent.Id));
            foreach (var identity in catalog.HeroIdentities.Values) keys.Add(HeroArt(identity.Id));
            foreach (var preview in catalog.ComingSoon)
            {
                keys.Add(HeroArt(preview.Id));
                keys.Add(Portrait(preview.Id, "neutral"));
            }
            keys.Add(LockIcon);
            foreach (ItemRarity rarity in System.Enum.GetValues(typeof(ItemRarity))) keys.Add(RarityFrame(rarity));
            foreach (var enemy in catalog.Enemies.Values)
            {
                // Only a boss that puffs up has puffed and deflated poses (D-062).
                if (!enemy.IsBoss || enemy.PuffTurns <= 0) continue;
                keys.Add(Actor(enemy.Id, "puffed"));
                keys.Add(Actor(enemy.Id, "deflated"));
            }
            foreach (var clue in new[] { Clue.Enemy, Clue.Danger, Clue.Objective, Clue.Treasure, Clue.Safe, Clue.Exit, Clue.Feature }) keys.Add(ClueIcon(clue));
            foreach (var kind in new[] { CommandKind.Move, CommandKind.Slash, CommandKind.Shield, CommandKind.Dash, CommandKind.Potion })
                keys.Add(AbilityIcon(kind));
            foreach (var identity in catalog.HeroIdentities.Values)
            foreach (var expression in Expressions) keys.Add(Portrait(identity.Id, expression));
            foreach (var kind in IntentKinds) keys.Add(IntentIcon(kind));
            foreach (var kind in ThreatKinds) keys.Add(DangerOverlay(kind));
            keys.Add(DangerWarning);
            keys.AddRange(new[]
            {
                HighlightTarget, HighlightHover,
                PortraitFrame, HpBack, HpFill, HpFrame, ManaFill, ManaIcon, EnemyHpBack, EnemyHpFill, Heart, CoinIcon, GemIcon, Chip, FloorPlaque, Panel, SpeechStrip,
                AbilityButtonDefault, AbilitySelected, CountBadge, SettingsButton, HelpButton,
            });
            foreach (var kind in AbilityKinds) keys.Add(AbilityButton(kind));
            foreach (var kind in AbilityKinds) keys.Add(HudAbility(kind));
            keys.AddRange(new[]
            {
                ModalPanel, ButtonPrimary, ButtonSecondary, ButtonDanger,
                ChestLargeClosed, ChestLargeOpen, ChestProgressBack, ChestProgressFill, ChestRewardCard,
                TitleHeroCard, TitlePlank, TitleBanner, TitleContinuePanel, ContinuePreview, TitleDailyPanel, TitleNamePlate, TitleLevelBadge, TitleCoinField, TitleGemField, TitleContinueClean, TitleMailClean, HudLevelBadge, HudPlaque, HudCoinField, HudGemField, DailyRewardChest, ButtonClaim, MenuButton, CrownButton, MailButton, AlertBadge, NavBar, PlusButton,
                TitleHero, TitleBlobert, TitleGoblin, ButtonPlay, ButtonTitleSettings, ButtonQuit, ButtonHeroSelect, ButtonShop, ButtonTalents, ButtonTalentsAlert, ButtonInventory, PlayIcon, SettingsIcon, QuitIcon,
            });
            foreach (var style in ModalStyles) keys.Add(ModalPanelStyle(style));
            keys.AddRange(new[] { ChestCoin, ChestGem });
            foreach (var step in ChestReactionSteps) keys.Add(ChestReaction(step));
            foreach (var kind in RewardKinds) keys.Add(RewardIcon(kind));
            keys.AddRange(new[] { UnderfootSpikes, UnderfootBomb, UnderfootBombArmed, UnderfootExitLocked, UnderfootExitOpen });
            keys.AddRange(new[] { FxExplosion, FxExitUnlock, FxEnemyWake, BombFuse });
            keys.AddRange(new[] { FloorBanner, FloorBannerBoss });
            foreach (var identity in catalog.HeroIdentities.Values)
            {
                keys.Add(Actor(identity.Id));
                keys.Add(Actor(identity.Id, "guard"));
                foreach (var animation in HeroAnimations) keys.Add(Actor(identity.Id, animation));
            }
            // The mascot is no hero (D-057), but the title still stands him under the arch.
            if (!keys.Contains(Actor(MascotId))) keys.Add(Actor(MascotId));
            foreach (var enemy in catalog.Enemies.Values)
            {
                foreach (var animation in EnemyAnimations) keys.Add(Actor(enemy.Id, animation));
                if (ActorExtras.TryGetValue(enemy.Id, out var extras))
                    foreach (var extra in extras) keys.Add(Actor(enemy.Id, extra));
            }
            // Last: several shop cards reuse the HUD's own heart, coin, gem and key icons.
            foreach (ClickDungeon.Application.ShopItem item in System.Enum.GetValues(typeof(ClickDungeon.Application.ShopItem)))
                if (!keys.Contains(ShopIcon(item))) keys.Add(ShopIcon(item));
            return keys;
        }
    }
}
