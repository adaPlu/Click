using System;
using System.Collections.Generic;
using System.Text;
using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Domain;
using ClickDungeon.Simulation;
using ClickDungeon.Unity.Ui;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static ClickDungeon.Unity.Ui.RefLayout;
using Terrain = ClickDungeon.Domain.Terrain;

namespace ClickDungeon.Unity.Screens
{
    /// <summary>
    /// Main gameplay screen, laid out after the reference "main game screen": logo, portrait and HP top-left,
    /// floor plaque, centred 5×5 board, MOVE/SLASH/SHIELD/DASH/POTION under the board, settings top-right.
    /// Every gameplay change goes through GameSession.Submit.
    /// </summary>
    public sealed class GameScreen
    {
        enum TargetMode { Move, Slash, Dash, Skill }

        sealed class SkillButton
        {
            public UiFactory.ButtonParts Parts;
            public Image Selected;
            public Text Cost;
            public CanvasGroup Group;
        }

        sealed class AbilityButton
        {
            public UiFactory.ButtonParts Parts;
            public Image Selected;
            public Image BadgeBack;
            public Text Badge;
            public CanvasGroup Group;
        }

        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);
        static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        static readonly Vector2 TopRight = new Vector2(1f, 1f);
        const int MaxLogLines = 14;

        readonly ClickDungeonApp _app;
        readonly BoardView _board;
        readonly ModalOverlay _modal;
        readonly ChestOverlay _chest;
        readonly FloorBanner _floorBanner;
        readonly Dictionary<CommandKind, AbilityButton> _abilities = new Dictionary<CommandKind, AbilityButton>();
        /// <summary>The hero's usable skills (D-075), in slot order. Built once; a slot the hero has not filled is hidden.</summary>
        readonly List<SkillButton> _skills = new List<SkillButton>();
        /// <summary>Which slot is being aimed while <see cref="TargetMode.Skill"/> is on, or -1.</summary>
        int _skillSlot = -1;
        readonly List<string> _log = new List<string>();

        Text _face;
        Image _portraitArt;
        Text _speechFace;
        Text _speech;
        Text _hpText;
        RectTransform _hpFill;
        Text _manaText;
        RectTransform _manaFill;
        Text _floorTitle;
        Text _floorName;
        /// <summary>The run's story, newest first; the menu's WHAT HAPPENED shows it.</summary>
        string _logText;
        Text _goal;
        Text _status;
        Text _coins;
        Text _gems;
        GameObject _talentsBadge;
        GameObject _inspectPanel;
        InventoryOverlay _inventory;
        ShopOverlay _shop;
        TalentOverlay _talents;
        Text _inspectTitle;
        Text _inspectBody;
        Image _inspectPortrait;
        Text _levelBadge;

        /// <summary>
        /// True when the background is the reference gameplay screen itself (D-034): its logo, portrait, purse, buttons and
        /// board frame are then the background's own pixels, and this screen adds live values and cleaned patches in place.
        /// </summary>
        readonly bool _matched;
        GameObject _portraitRoot;
        CanvasGroup _speechGroup;
        Image _speechPortrait;
        float _speechUntil;
        const float SpeechSeconds = 5f;

        TargetMode _mode = TargetMode.Move;
        GridPos? _hover;
        List<Threat> _threats = new List<Threat>();
        string _lastDamageSource;

        public GameScreen(ClickDungeonApp app, RectTransform parent)
        {
            _app = app;
            Root = UiFactory.Rect(parent, "GameScreen");
            RefLayout.Stage(Root);

            // Portrait cannot use the reference's painted HUD, so it builds every piece itself over the portrait room.
            bool portrait = RefLayout.Portrait;
            _matched = !portrait && Art.Has(ArtKeys.GameplayBackground) && Art.Has(ArtKeys.HudPlaque) && Art.Has(ArtKeys.HudAbility(CommandKind.Move));
            Backdrop.Build(Root, portrait && Art.Has(ArtKeys.GameplayBackgroundPortrait) ? ArtKeys.GameplayBackgroundPortrait : ArtKeys.GameplayBackground,
                new[] { new Vector2(-420f, 220f), new Vector2(420f, 220f), new Vector2(-420f, -120f), new Vector2(420f, -120f) });
            BuildTopLeft();
            BuildTopRight();
            _logText = "<color=#A69F93>Nothing yet.\n\nEvery hit, discovery and wake-up will be explained here, newest first.</color>";
            // The reference keeps both sides of the board clear: INSPECT only appears while there is something to inspect.
            _inspectBody = BuildPanel("Inspect", new Vector2(1f, 0.5f), new Vector2(-36f, 40f), "INSPECT", out _inspectTitle);
            _inspectPanel = _inspectBody.transform.parent.gameObject;
            _inspectPanel.SetActive(false);
            _inspectPortrait = UiFactory.Image((RectTransform)_inspectBody.transform.parent, "Portrait", Color.white, null);
            _inspectPortrait.preserveAspect = true;
            _inspectPortrait.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -14f), new Vector2(44f, 44f));
            _inspectPortrait.gameObject.SetActive(false);

            _board = new BoardView(Root, app, portrait ? PortraitBoardCenter : _matched ? MatchedBoardCenter : new Vector2(0f, BoardY));
            _board.Root.localScale = portrait ? Vector3.one * PortraitBoardScale : new Vector3(BoardScale, BoardScale, 1f);
            // Inside the reference's own stone frame, covering the sample tiles it shows there (in portrait, the portrait
            // room's copy of that frame).
            if (_matched) _board.FitInside(MatchedBoardInterior / BoardScale);
            else if (portrait && Art.Has(ArtKeys.GameplayBackgroundPortrait)) _board.FitInside(PortraitBoardInterior / PortraitBoardScale);
            _board.CellClicked += OnCellClicked;
            _board.CellEntered += p =>
            {
                _hover = p;
                RefreshBoardOnly();
            };
            _board.CellExited += p =>
            {
                if (_hover.HasValue && _hover.Value == p) _hover = null;
                RefreshBoardOnly();
            };

            BuildAbilityBar();
            BuildSkillBar();
            BuildSpeechStrip();
            BuildNavBar();

            if (portrait) LayoutPortrait();

            // Above the board, over the top HUD band, so it never hides board tiles.
            _floorBanner = new FloorBanner(Root, app, portrait ? new Vector2(0f, 590f) : new Vector2(0f, 482f));
            // Each menu's own panel width, so in portrait it fills the screen's width (RefLayout.OverlayStage).
            _chest = new ChestOverlay(RefLayout.OverlayStage(Root, 1200f), app);
            _modal = new ModalOverlay(RefLayout.OverlayStage(Root, 760f), app);
            _inventory = new InventoryOverlay(RefLayout.OverlayStage(Root, 1060f));
            _shop = new ShopOverlay(RefLayout.OverlayStage(Root, 1340f));
            // The talent tree has a portrait layout of its own, on the screen's stage.
            _talents = new TalentOverlay(RefLayout.Portrait ? Root : RefLayout.OverlayStage(Root, 1840f));
        }

        // The portrait room's board frame (make_portrait_backgrounds.py): its inner edge, centred 612 below the stage's top.
        static readonly Vector2 PortraitBoardCenter = new Vector2(0f, RefLayout.PortraitHeight / 2f - 612f);
        static readonly Vector2 PortraitBoardInterior = new Vector2(893f, 612f);
        /// <summary>The 5 × 5 grid scaled to fit that interior's height, as the landscape board fits its frame.</summary>
        static readonly float PortraitBoardScale = PortraitBoardInterior.y / (BoardRules.Size * BoardView.CellSize + (BoardRules.Size - 1) * BoardView.Gap);

        /// <summary>
        /// The phone-upright layout on the 1080 × 1920 stage: portrait, bars and purse across the top; the floor plaque, logo,
        /// settings and menu under them; the board in the room's frame; the abilities under it; the hero's line and the
        /// goal below; INSPECT when there is something to inspect; the INVENTORY / TALENTS / SHOP bar along the bottom.
        /// </summary>
        void LayoutPortrait()
        {
            RefLayout.Top(Root, "Portrait", 24f, 20f, 128f, 128f);
            RefLayout.Top(Root, "Hp", 172f, 26f, 370f, 47f);
            RefLayout.Top(Root, "Mana", 172f, 86f, 370f, 47f);
            RefLayout.Top(Root, "Coins", 792f, 24f, 264f, 48f);
            RefLayout.Top(Root, "Gems", 792f, 84f, 264f, 48f);
            RefLayout.Top(Root, "FloorPlaque", 24f, 166f, 306f, 84f);
            RefLayout.Top(Root, "Logo", 348f, 172f, 410f, 74f);
            RefLayout.Top(Root, "Art " + ArtKeys.Logo, 348f, 172f, 410f, 74f);
            RefLayout.Top(Root, "Settings", 872f, 164f, 86f, 86f);
            RefLayout.Top(Root, "Menu", 970f, 164f, 86f, 86f);
            // The abilities a size up: they are the thumb's targets.
            var abilities = RefLayout.Top(Root, "AbilityBar", 22f, 950f, 900f, 166f);
            if (abilities != null) abilities.localScale = Vector3.one * 1.15f;
            RefLayout.Top(Root, "Speech", 24f, 1162f, 1032f, 128f);
            RefLayout.Top(Root, "Goal", 24f, 1304f, 1032f, 104f);
            RefLayout.Top(Root, "Inspect", 24f, 1424f, 1032f, 356f);
            RefLayout.Top(Root, "NavBar", 11f, 1800f, 1057f, 110f);
        }

        /// <summary>
        /// Rebuilt after the device turned: shows the run as it stands, without a greeting or animation. A finished run
        /// gets its end panel back, because that panel lived on the screen this one replaces (D-044).
        /// </summary>
        public void Reopen()
        {
            Refresh(false);
            CheckRunEnd();
        }

        /// <summary>
        /// Called before this screen is torn down (a turn of the device): a chest reveal still on screen closes and runs
        /// whatever was waiting on it, so the run's end is settled on the session rather than lost with the overlay.
        /// </summary>
        public void PrepareForRebuild() => _chest.CloseNow();

        /// <summary>
        /// Takes over what only a screen knows from the one it replaces: WHAT HAPPENED, which lives here rather than in the
        /// session, and would otherwise read "Nothing yet." after a turn of the device (D-044).
        /// </summary>
        public void AdoptFrom(GameScreen previous)
        {
            if (previous == null || previous == this) return;
            _log.Clear();
            _log.AddRange(previous._log);
            _logText = previous._logText;
            _lastDamageSource = previous._lastDamageSource;
        }

        public RectTransform Root { get; }

        RunState Run => _app.Session.Run;
        ContentCatalog Catalog => _app.Catalog;
        bool Blocked => _modal.IsOpen || _chest.IsOpen || _inventory.IsOpen || _shop.IsOpen || _talents.IsOpen || Run == null || Run.Status != RunStatus.InProgress;

        // ------------------------------------------------------------------ lifecycle

        public void Begin(List<GameEvent> events, string notice)
        {
            _log.Clear();
            if (events != null)
            {
                AppendLog(events, Discovery.Before(Run));
                Say(Lines.FloorStart(Run.Floor), Run.Floor.IsBossFloor ? Expression.Shocked : Expression.Confident);
            }
            else
            {
                Say(notice ?? "Welcome back. Where were we? Ah yes: danger.", Expression.Happy);
                _logText = "Run resumed.";
            }
            Refresh(false);
            ShowFloorBanner();

            if (!_app.AutomationMode && !UserPrefs.SeenHelp)
            {
                UserPrefs.SeenHelp = true;
                OpenHelp();
            }
        }

        public void Tick()
        {
            if (_speechGroup != null)
                _speechGroup.alpha = Mathf.Clamp01((_speechUntil - Time.unscaledTime) / 0.5f);
            var kb = Keyboard.current;
            if (kb == null) return;
            // The keys belong to the bot's driver in automation, not to this screen. Watch mode polls Escape once a move
            // to stop the batch; this method sees every frame, so without this it wins the race and opens the pause menu
            // instead, which blocks the bot for the rest of the run (REL-28).
            if (_app.AutomationMode) return;

            if (_chest.IsOpen)
            {
                if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame) _chest.Tap();
                return;
            }
            if (_modal.IsOpen)
            {
                if (kb.escapeKey.wasPressedThisFrame) _modal.Back();
                return;
            }
            if (_inventory.IsOpen)
            {
                if (kb.escapeKey.wasPressedThisFrame) CloseInventory();
                return;
            }
            if (_shop.IsOpen)
            {
                if (kb.escapeKey.wasPressedThisFrame) _shop.Hide();
                return;
            }
            if (_talents.IsOpen)
            {
                if (kb.escapeKey.wasPressedThisFrame) _talents.Hide();
                return;
            }
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (_mode != TargetMode.Move) SetMode(TargetMode.Move);
                else OpenPause();
                return;
            }
            if (kb.slashKey.wasPressedThisFrame || kb.hKey.wasPressedThisFrame)
            {
                OpenHelp();
                return;
            }
            if (Blocked) return;

            if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) Directional(Direction.Up);
            else if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) Directional(Direction.Down);
            else if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) Directional(Direction.Left);
            else if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) Directional(Direction.Right);
            else if (kb.spaceKey.wasPressedThisFrame) Submit(PlayerCommand.Wait());
            else if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) OnAbility(CommandKind.Move);
            else if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) OnAbility(CommandKind.Slash);
            else if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) OnAbility(CommandKind.Shield);
            else if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) OnAbility(CommandKind.Dash);
            else if (kb.digit5Key.wasPressedThisFrame || kb.numpad5Key.wasPressedThisFrame) OnAbility(CommandKind.Potion);
            else if (kb.digit6Key.wasPressedThisFrame || kb.numpad6Key.wasPressedThisFrame) OnSkill(0);
            else if (kb.digit7Key.wasPressedThisFrame || kb.numpad7Key.wasPressedThisFrame) OnSkill(1);
            else if (kb.digit8Key.wasPressedThisFrame || kb.numpad8Key.wasPressedThisFrame) OnSkill(2);
        }

        /// <summary>Watch mode: whether a chest reveal is on screen, and one tap on it, so the bot's chests play out visibly.</summary>
        public bool AutomationChestOpen => _chest.IsOpen;
        public void AutomationTapChest() => _chest.Tap();

        /// <summary>Automation hook: submits through the normal input path, first tapping through any open chest reveal.</summary>
        public void AutomationSubmit(PlayerCommand command)
        {
            for (int i = 0; i < 4 && _chest.IsOpen; i++) _chest.Tap();
            Submit(command);
        }

        /// <summary>Automation hook (screenshots): opens an overlay without changing gameplay state.</summary>
        public void AutomationOverlay(string name)
        {
            switch (name)
            {
                case "pause": OpenPause(); break;
                case "menu": OpenPause(); break;
                // The HUD as it looks between floors, without the floor banner that covers it for a moment.
                case "hud": _floorBanner.Hide(); break;
                // Screenshots of tile art: uncovers the whole board (automation only; the run is thrown away after).
                case "revealall":
                    _floorBanner.Hide();
                    foreach (var p in Board.AllCells) Run.Floor[p].Knowledge = Knowledge.Revealed;
                    Refresh(false);
                    break;
                case "log": OpenLog(_modal.Hide); break;
                case "inventory": OpenInventory(); break;
                case "talents": OpenTalents(); break;
                case "shop": OpenShop(); break;
                case "help": OpenHelp(); break;
                case "chest": _chest.Open(new RewardRecord { Kind = RewardKind.Potion, Amount = 1 }, null); break;
                case "chestburst":
                    _chest.Open(new RewardRecord { Kind = RewardKind.MaxHp, Amount = 2 }, null);
                    _chest.Tap();
                    break;
                case "banner": ShowFloorBanner(); break;
                case "bossbanner": _floorBanner.Show(Catalog.RunFloorCount, Catalog.ProfileFor(Catalog.RunFloorCount).Name, true); break;
                case "victory":
                    _modal.Show(ModalStyle.Victory, "VICTORY!", "Screenshot preview of the victory panel.", () => { },
                        Menus.B("NEW RUN", Palette.PlayGreen, () => { }), Menus.B("TITLE", Palette.NavyLight, () => { }));
                    break;
                case "defeat":
                    _modal.Show(ModalStyle.Defeat, "DEFEATED", "Screenshot preview of the defeat panel.", () => { },
                        Menus.B("NEW RUN", Palette.PlayGreen, () => { }), Menus.B("ABANDON RUN", Palette.QuitRed, () => { }));
                    break;
            }
        }

        // ------------------------------------------------------------------ input → commands

        void Directional(Direction dir)
        {
            var hero = Run.Hero.Pos;
            switch (_mode)
            {
                case TargetMode.Slash:
                    Submit(PlayerCommand.Slash(hero.Step(dir)));
                    break;
                case TargetMode.Dash:
                    Submit(PlayerCommand.Dash(hero.Step(dir, Catalog.HeroClass(Run.Hero.ClassId).DashDistance)));
                    break;
                case TargetMode.Skill:
                    Submit(PlayerCommand.Skill(_skillSlot, hero.Step(dir)));
                    break;
                default:
                    if (Commands.TryContextual(Run, hero.Step(dir), out var command)) Submit(command);
                    break;
            }
        }

        void OnCellClicked(GridPos p)
        {
            // A tap on the board during a watched run would be a command the bot did not choose, in the middle of the
            // run it is being measured on. The bot reaches the board through AutomationSubmit, not through here.
            if (Blocked || _app.AutomationMode) return;
            switch (_mode)
            {
                case TargetMode.Slash:
                    Submit(PlayerCommand.Slash(p));
                    break;
                case TargetMode.Dash:
                    Submit(PlayerCommand.Dash(p));
                    break;
                case TargetMode.Skill:
                    Submit(PlayerCommand.Skill(_skillSlot, p));
                    break;
                default:
                    if (Commands.TryContextual(Run, p, out var command))
                    {
                        Submit(command);
                    }
                    else
                    {
                        _board.Nudge(p);
                        Say("Too far! One step at a time. (DASH jumps two.)", Expression.Worried);
                    }
                    break;
            }
        }

        void OnAbility(CommandKind kind)
        {
            if (Blocked || _app.AutomationMode) return;
            switch (kind)
            {
                case CommandKind.Move:
                    SetMode(TargetMode.Move);
                    break;
                case CommandKind.Slash:
                    SetMode(_mode == TargetMode.Slash ? TargetMode.Move : TargetMode.Slash);
                    break;
                case CommandKind.Dash:
                    SetMode(_mode == TargetMode.Dash ? TargetMode.Move : TargetMode.Dash);
                    break;
                case CommandKind.Shield:
                    Submit(PlayerCommand.Shield());
                    break;
                case CommandKind.Potion:
                    Submit(PlayerCommand.Potion());
                    break;
            }
        }

        void SetMode(TargetMode mode)
        {
            if (mode == TargetMode.Slash && Commands.LegalTargets(Run, CommandKind.Slash, Catalog).Count == 0)
            {
                Say("Nothing to slash. Stand next to an enemy or a bomb.", Expression.Neutral);
                mode = TargetMode.Move;
            }
            else if (mode == TargetMode.Dash && Commands.LegalTargets(Run, CommandKind.Dash, Catalog).Count == 0)
            {
                int cost = Mana.DashCost(Run, Catalog.HeroClass(Run.Hero.ClassId));
                Say(!Mana.CanPay(Run.Hero, cost) ? $"Not enough mana to dash ({Run.Hero.Mana}/{cost})." : "No room to dash.", Expression.Worried);
                mode = TargetMode.Move;
            }
            else if (mode == TargetMode.Slash)
            {
                Say(Board.SlashReach(Run) > 1 ? "SLASH: pick a lit monster down a clear line." : "SLASH: pick a lit tile next to you.", Expression.Confident);
            }
            else if (mode == TargetMode.Dash)
            {
                Say(Catalog.HeroClass(Run.Hero.ClassId).DashDistance > 1
                    ? "DASH: leap one or two tiles in a straight line, diagonals too, clearing traps."
                    : "DASH: leap one tile in a straight line, diagonals too, clearing traps.", Expression.Confident);
            }
            else if (mode == TargetMode.Skill)
            {
                var skill = SkillAt(_skillSlot);
                if (skill == null || SkillTargets(Run, _skillSlot).Count == 0)
                {
                    // The same courtesy the other two get: say why, and do not leave the player in a mode that cannot
                    // be used. Mana is the usual reason, so name the number.
                    Say(skill == null ? "No skill there."
                        : !Mana.CanPay(Run.Hero, skill.ManaCost) ? $"Not enough mana for {skill.Name} ({Run.Hero.Mana}/{skill.ManaCost})."
                        : $"Nothing in reach of {skill.Name}.", Expression.Worried);
                    mode = TargetMode.Move;
                }
                else
                {
                    Say($"{skill.Name.ToUpperInvariant()}: {skill.Summary}", Expression.Confident);
                }
            }

            if (mode != TargetMode.Skill) _skillSlot = -1;
            _mode = mode;
            Refresh(false);
        }

        void Submit(PlayerCommand command)
        {
            if (Blocked) return;
            var run = Run;
            int floorBefore = run.Floor.FloorIndex;
            // Nothing that happens under a cover may be pointed at (D-023 amendment).
            var discovery = Discovery.Before(run);

            var result = _app.Session.Submit(command);
            if (!result.Accepted)
            {
                Say(result.RejectReason, Expression.Worried);
                _board.Nudge(command.Target);
                return;
            }

            _mode = TargetMode.Move;
            bool floorChanged = run.Floor.FloorIndex != floorBefore;
            AppendLog(result.Events, discovery);

            var rewards = new List<RewardRecord>();
            int bestPriority = 0;
            string line = null;
            var face = Expression.Neutral;
            bool shake = false;
            _board.BeginPopupBatch();
            foreach (var e in result.Events)
            {
                if (!discovery.CanMention(run, e)) continue;
                int priority = Lines.React(e, run, Catalog, out var candidate, out var candidateFace);
                if (candidate != null && priority > bestPriority)
                {
                    bestPriority = priority;
                    line = candidate;
                    face = candidateFace;
                }
                if (e.Kind == GameEventKind.HeroDamaged)
                {
                    _lastDamageSource = e.Source;
                    shake = true;
                }
                // A chest grants one reward per tap (D-022), so the reveal collects every one of them.
                if (e.Kind == GameEventKind.ChestOpened && e.Reward != null) rewards.Add(e.Reward);
                // The stairs heal lands on the new floor's start, so it still gets its popup when the floor changes.
                bool popupAllowed = !floorChanged || (e.Kind == GameEventKind.HeroHealed && e.Source == "stairs");
                if (popupAllowed && discovery.CanMark(run, e)) ShowPopup(e);
            }

            if (floorChanged && run.Status == RunStatus.InProgress)
            {
                _board.ClearEffects();
                _hover = null;
                ShowFloorBanner();
                line = Lines.FloorStart(run.Floor);
                face = run.Floor.IsBossFloor ? Expression.Shocked : Expression.Confident;
            }
            if (line != null) Say(line, face);
            if (shake) _board.Shake();

            if (!floorChanged) _board.QueueActorAnimations(run, discovery.Markable(run, result.Events));
            Refresh(!floorChanged);

            if (rewards.Count > 0)
            {
                _chest.Open(rewards, () =>
                {
                    Refresh(false);
                    CheckRunEnd();
                });
            }
            else
            {
                CheckRunEnd();
            }
        }

        void ShowPopup(GameEvent e)
        {
            switch (e.Kind)
            {
                case GameEventKind.HeroDamaged: _board.Popup(e.To, $"-{e.Amount}", Palette.Danger); break;
                case GameEventKind.HeroBlocked: _board.Popup(e.To, "BLOCK!", Palette.Gold); break;
                case GameEventKind.HeroHealed: _board.Popup(e.To, $"+{e.Amount}", Palette.Safe); break;
                case GameEventKind.EnemyDamaged: _board.Popup(e.To, $"-{e.Amount}", Color.white); break;
                case GameEventKind.EnemyImmune: _board.Popup(e.To, "IMMUNE", Palette.Steel); break;
                case GameEventKind.EnemyStaggered: _board.Popup(e.To, "DAZED", Palette.Gold); break;
                case GameEventKind.EnemyWoke: _board.Popup(e.To, "!", Palette.Danger); break;
                case GameEventKind.EnemyMissed: _board.Popup(e.To, "MISS", Palette.TextDim); break;
                case GameEventKind.BombExploded: _board.Popup(e.To, "BOOM!", Palette.Fuse); break;
                case GameEventKind.KeyCollected: _board.Popup(e.To, "KEY!", Palette.Gold); break;
                case GameEventKind.PotionCollected: _board.Popup(e.To, "+POTION", Palette.Safe); break;
                case GameEventKind.ExitUnlocked:
                    if (e.To.InBounds) _board.Popup(e.To, "OPEN!", Palette.Gold);
                    break;
            }
        }

        string DifficultyName(RunState run) => Catalog.DifficultyInfo(run.Difficulty).DisplayName;

        void ShowFloorBanner()
        {
            var floor = Run?.Floor;
            if (floor == null) return;
            _floorBanner.Show(floor.FloorIndex, Catalog.ProfileFor(floor.FloorIndex).Name, floor.IsBossFloor);
        }

        void CheckRunEnd()
        {
            var run = Run;
            if (run == null || run.Status == RunStatus.InProgress) return;

            var floorName = Catalog.ProfileFor(run.Floor.FloorIndex).Name;
            if (run.Status == RunStatus.Won)
            {
                _modal.Show(ModalStyle.Victory, "VICTORY!",
                    $"Lord Blobert is defeated. Again.\n\nDifficulty: {DifficultyName(run)}\nTurns taken: {run.Turn}\nChests opened: {Chests.ChestsOpened(run.Rewards)} ({run.Rewards.Count} rewards)\n{Purse(run)}\n\n{Lines.HeroName(run, Catalog)}: \"Victory! Snacks for everyone!\"",
                    () => { },
                    Menus.B("NEW RUN", Palette.PlayGreen, _app.StartNewRun),
                    Menus.B("TITLE", Palette.NavyLight, GoToTitle));
            }
            else
            {
                _modal.Show(ModalStyle.Defeat, "DEFEATED",
                    $"Fell on floor {run.Floor.FloorIndex}: {floorName}\nFinal blow: {Lines.SourceName(_lastDamageSource ?? "?", Catalog)}\nDifficulty: {DifficultyName(run)}\nTurns survived: {run.Turn}\n{Purse(run)}\n\nWHAT HAPPENED shows every hit.\n\n{Lines.HeroName(run, Catalog)}: \"Tell my horse... wait. I don't have a horse.\"",
                    () => { },
                    Menus.B("NEW RUN", Palette.PlayGreen, _app.StartNewRun),
                    Menus.B("WHAT HAPPENED", Palette.NavyLight, () => OpenLog(CheckRunEnd)),
                    Menus.B("TITLE", Palette.NavyLight, GoToTitle));
            }
        }

        /// <summary>What the run carried out, which the profile has just banked (D-025).</summary>
        static string Purse(RunState run)
        {
            string gems = run.GemsFound > 0 ? $" and {run.GemsFound} gem{(run.GemsFound == 1 ? "" : "s")}" : "";
            return $"Carried out: {run.CoinsFound} coins{gems}, +{run.XpEarned} XP";
        }

        // ------------------------------------------------------------------ menus

        /// <summary>
        /// Leaving for the title tears this screen down. A watched run is still submitting into it, so in automation the
        /// way out belongs to the batch, not to whoever is looking at it (REL-29).
        /// </summary>
        void GoToTitle()
        {
            if (_app.AutomationMode) return;
            _app.ShowTitle();
        }

        void OpenPause()
        {
            if (_chest.IsOpen || Run == null) return;
            var run = Run;
            _modal.Show("MENU",
                $"{Goal(run, Catalog)}\nFloor {run.Floor.FloorIndex}: {Catalog.ProfileFor(run.Floor.FloorIndex).Name}\nDifficulty: {DifficultyName(run)}\nMovement: {Menus.MovementName(run.Movement)}\nTurn {run.Turn + 1}    Seed {run.RunSeed}\nYour run is saved after every turn.{(_app.TelemetryActive ? "\nPlaytest log is on (saved on this device only)." : "")}",
                _modal.Hide,
                Menus.B("RESUME", Palette.PlayGreen, _modal.Hide),
                Menus.B("WHAT HAPPENED", Palette.NavyLight, () => OpenLog(OpenPause)),
                Menus.B("HOW TO PLAY", Palette.NavyLight, OpenHelp),
                Menus.B("SETTINGS", Palette.NavyLight, () => Menus.OpenSettings(_modal, OpenPause, _app.ApplyTelemetrySetting)),
                Menus.B("ABANDON RUN", Palette.QuitRed, ConfirmAbandon),
                Menus.B("QUIT TO TITLE", Palette.NavyLight, GoToTitle));
        }

        /// <summary>The run's story so far, newest first: every hit, discovery and wake-up, in the words the log used to show beside the board.</summary>
        void OpenLog(Action back) => _modal.Show("WHAT HAPPENED", _logText, back, Menus.B("BACK", Palette.NavyLight, back));

        const string NextRunNote = "\n\n<color=#F2C14E>Changes here outfit your NEXT run. This run keeps what it started with.</color>";

        void OpenInventory()
        {
            if (_chest.IsOpen || Run == null) return;
            _inventory.Open(_app.Catalog, _app.Session.Profile, () =>
            {
                _app.Session.SaveProfile();
                RefreshPurse();
            });
        }

        void CloseInventory()
        {
            _inventory.Hide();
            RefreshPurse();
        }

        /// <summary>The playing class's tree (D-037). What is learned here shapes the next run, not this one.</summary>
        void OpenTalents()
        {
            if (_chest.IsOpen || Run == null) return;
            _talents.Open(Catalog, _app.Session.Profile, Run.Hero.ClassId, () =>
            {
                _app.Session.SaveProfile();
                RefreshPurse();
            }, "<color=#F2C14E>Talents learned now shape your NEXT run.</color>");
        }

        void OpenShop() => OpenShop(ShopTab.Boosts);

        /// <summary>The purse's "+" opens the shop on its exchange (D-036).</summary>
        void OpenPurse(bool coins) => OpenShop(ShopTab.Exchange);

        /// <summary>
        /// The shop spends banked coins and gems; what this run finds is banked when it ends, and what is bought here outfits
        /// the next run.
        /// </summary>
        void OpenShop(ShopTab tab)
        {
            if (_chest.IsOpen || Run == null) return;
            string found = Run.CoinsFound + Run.GemsFound > 0 ? $" This run's {Run.CoinsFound} coins and {Run.GemsFound} gems arrive when it ends." : "";
            _shop.Open(Catalog, _app.Session.Profile, () => DateTime.Now, () =>
            {
                _app.Session.SaveProfile();
                RefreshPurse();
            }, tab, "<color=#F2C14E>Buys outfit your NEXT run.</color>" + found);
        }

        void OpenHelp()
        {
            _modal.Show("HOW TO PLAY", Menus.HelpText, _modal.Hide, Menus.B("GOT IT", Palette.PlayGreen, _modal.Hide));
        }

        void ConfirmAbandon()
        {
            _modal.Show("ABANDON RUN?", "This run will be lost for good.", OpenPause,
                Menus.B("ABANDON", Palette.QuitRed, () =>
                {
                    // Abandoning clears the run out from under the batch that is still stepping it (REL-29). The button
                    // stays on screen so an automated shot of this panel is the panel a player sees; it just does nothing.
                    if (_app.AutomationMode) return;
                    _app.Session.Abandon();
                    GoToTitle();
                }),
                Menus.B("KEEP PLAYING", Palette.PlayGreen, _modal.Hide));
        }

        // ------------------------------------------------------------------ rendering

        /// <summary>The hero whose face the panel, the bubble and the chest overlay wear: whoever is playing (D-065).</summary>
        string FaceId => Run?.Hero.IdentityId ?? ArtKeys.HeroId;

        /// <summary>
        /// The portrait key for a hero pulling this face, or null if nothing is drawn for it. Their own face first, then
        /// their neutral one, and only then the default hero's: a hero short of portraits must not borrow another's face
        /// for an expression, because the face is who is speaking (D-065). Every hero has all eight since D-064, so the
        /// fallbacks only matter to a build with art missing.
        /// </summary>
        public static string SpeechPortraitKey(string heroId, Expression face)
        {
            string own = ArtKeys.Portrait(heroId, face.ToString());
            if (Art.Has(own)) return own;
            string neutral = ArtKeys.Portrait(heroId, "neutral");
            if (Art.Has(neutral)) return neutral;
            string standIn = ArtKeys.Portrait(ArtKeys.HeroId, face.ToString());
            return Art.Has(standIn) ? standIn : null;
        }

        void Say(string line, Expression face)
        {
            _speech.text = line;
            _speechUntil = Time.unscaledTime + SpeechSeconds;
            _speechFace.text = Lines.Face(face);
            _face.text = Lines.Face(face);
            var key = SpeechPortraitKey(FaceId, face);
            if (key == null || !Art.TryGetSprite(key, out var portrait)) return;
            if (_portraitArt != null) _portraitArt.sprite = portrait;
            // The speech bubble shows the same face, and replaces the text one it was drawn with.
            if (_speechPortrait == null) return;
            _speechPortrait.sprite = portrait;
            _speechPortrait.enabled = true;
            _speechFace.enabled = false;
        }

        void AppendLog(List<GameEvent> events, Discovery discovery)
        {
            var lines = new List<string>();
            foreach (var e in events)
            {
                if (!discovery.CanMention(Run, e)) continue;
                var text = Lines.Describe(e, Run, Catalog);
                if (text != null) lines.Add(text);
            }
            if (lines.Count == 0) return;

            lines.Reverse();
            lines.Insert(0, $"<color=#8F8778>- turn {Math.Max(1, Run.Turn)} -</color>");
            _log.InsertRange(0, lines);
            if (_log.Count > MaxLogLines) _log.RemoveRange(MaxLogLines, _log.Count - MaxLogLines);
            _logText = string.Join("\n", _log);
        }

        /// <summary>
        /// Tells the player once when saving a run in progress stops working; play continues in memory. A finished run needs no
        /// warning, since what is left on disk cannot be continued. The raw error can contain file paths, so it only goes to the
        /// player log.
        /// </summary>
        void WarnIfSaveFailed()
        {
            var error = _app.Session.SaveError;
            if (error == _saveWarning) return;
            _saveWarning = error;
            if (error == null) return;
            Debug.LogWarning("[ClickDungeon] Saving failed: " + error);
            if (Run == null || Run.Status != RunStatus.InProgress) return;
            _log.Insert(0, "<color=#FF9A2E>Couldn't save your run. You can keep playing, but it may not resume later.</color>");
            _logText = string.Join("\n", _log);
            Say("I couldn't save the run! We can keep going, but it may not resume later.", Expression.Worried);
        }

        string _saveWarning;

        void Refresh(bool animate)
        {
            var run = Run;
            if (run == null) return;
            WarnIfSaveFailed();
            var hero = run.Hero;
            // Set here rather than at each Open, so a chest opened from anywhere celebrates in the playing hero's face.
            _chest.HeroId = FaceId;

            _hpText.text = $"{hero.Hp} / {hero.MaxHp}";
            _hpFill.anchorMax = new Vector2(Mathf.Clamp01(hero.Hp / (float)hero.MaxHp), 1f);
            _manaText.text = $"{hero.Mana} / {hero.MaxMana}";
            _manaFill.anchorMax = new Vector2(hero.MaxMana > 0 ? Mathf.Clamp01(hero.Mana / (float)hero.MaxMana) : 0f, 1f);
            var heroClass = Catalog.HeroClass(hero.ClassId);
            int shieldCost = Mana.ShieldCost(run, heroClass), dashCost = Mana.DashCost(run, heroClass);
            _levelBadge.text = Progression.Level(_app.Session.Profile).ToString();
            // The reference paints the mascot into its own portrait frame; the playing hero's face is drawn over him. The
            // mascot is not playable (D-057), so in practice the overlay always shows — the comparison is kept so a future
            // campaign that plays as him shows the painted face rather than a copy laid over it.
            if (_portraitRoot != null) _portraitRoot.SetActive(!_matched || hero.IdentityId != ArtKeys.MascotId);
            if (_goal != null) _goal.text = Goal(run, Catalog);
            if (_status != null) _status.text = $"TURN {run.Turn + 1}   ·   SLASH {Lines.SlashRange(run, Catalog)}   ·   KEY {(hero.HasKey ? "YES" : "NO")}"
                           + (hero.SpecialKeys > 0 ? $"   ·   SPECIAL KEYS {hero.SpecialKeys}" : "");
            RefreshPurse();
            _floorTitle.text = $"FLOOR {run.Floor.FloorIndex}";
            _floorName.text = run.Floor.IsVault
                ? "THE VAULT"
                : (Catalog.ProfileFor(run.Floor.FloorIndex).Name ?? "").ToUpperInvariant();

            bool live = run.Status == RunStatus.InProgress;
            SetAbility(CommandKind.Move, _mode == TargetMode.Move, live, null);
            SetAbility(CommandKind.Slash, _mode == TargetMode.Slash, live && Commands.LegalTargets(run, CommandKind.Slash, Catalog).Count > 0, null);
            // SHIELD and DASH wear their mana price, dimmed while the pool cannot pay it (D-032).
            SetAbility(CommandKind.Shield, false, live && Mana.CanPay(hero, shieldCost), shieldCost.ToString(), true);
            RefreshSkills(live);
            SetAbility(CommandKind.Dash, _mode == TargetMode.Dash, live && Commands.LegalTargets(run, CommandKind.Dash, Catalog).Count > 0,
                dashCost.ToString(), true);
            SetAbility(CommandKind.Potion, false, live && Commands.Validate(run, PlayerCommand.Potion(), Catalog, out _), hero.Potions.ToString());

            _threats = Threats.Compute(run, Catalog);
            RenderBoard(animate);
        }

        /// <summary>What this floor asks of the player, the line the INSPECT panel used to open with.</summary>
        static string Goal(RunState run, ContentCatalog catalog)
        {
            var floor = run.Floor;
            if (floor.IsBossFloor && !floor.ExitUnlocked) return $"GOAL: defeat {Lines.BossName(run, catalog)} to open the exit.";
            if (floor.ExitUnlocked || run.Hero.HasKey) return "GOAL: reach the EXIT.";
            return "GOAL: find the KEY, then reach the EXIT.";
        }

        /// <summary>
        /// The reference's purse: coins and gems banked, plus what this run has found so far, which is banked when it ends.
        /// The talents "!" shows while a point is free.
        /// </summary>
        void RefreshPurse()
        {
            var profile = _app.Session.Profile;
            int coins = profile.Coins + (Run?.CoinsFound ?? 0), gems = profile.Gems + (Run?.GemsFound ?? 0);
            _coins.text = coins.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            _gems.text = gems.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            _talentsBadge.SetActive(Run != null && Progression.PointsFree(profile, Catalog, Run.Hero.ClassId) > 0);
            _levelBadge.text = Progression.Level(profile).ToString();
        }

        /// <summary>Hover handler: only the highlights and inspector change, so the tiles are not rebuilt.</summary>
        void RefreshBoardOnly()
        {
            if (Run == null) return;
            if (_legal == null)
            {
                RenderBoard(false);
                return;
            }
            _board.RenderHighlights(_legal, _mode != TargetMode.Move, _hover);
            UpdateInspector();
        }

        /// <summary>Legal tiles from the last full render; state and mode changes always re-render before hover reuses them.</summary>
        HashSet<GridPos> _legal;

        void RenderBoard(bool animate)
        {
            var run = Run;
            var legal = new HashSet<GridPos>();
            if (run.Status == RunStatus.InProgress)
            {
                switch (_mode)
                {
                    case TargetMode.Slash:
                        legal.UnionWith(Commands.LegalTargets(run, CommandKind.Slash, Catalog));
                        break;
                    case TargetMode.Dash:
                        legal.UnionWith(Commands.LegalTargets(run, CommandKind.Dash, Catalog));
                        break;
                    case TargetMode.Skill:
                        legal.UnionWith(SkillTargets(run, _skillSlot));
                        break;
                    default:
                        foreach (var d in Directions.All)
                        {
                            var p = run.Hero.Pos.Step(d);
                            if (Commands.TryContextual(run, p, out var command) && Commands.Validate(run, command, Catalog, out _)) legal.Add(p);
                        }
                        break;
                }
            }
            _legal = legal;
            _board.Render(run, Catalog, _threats, legal, _mode != TargetMode.Move, _hover, animate);
            UpdateInspector();
        }

        /// <summary>
        /// The skill strip (D-075). A slot the hero has not filled is hidden rather than drawn dead: with three skills
        /// a class and three slots that is only the early game, but a Paladin who has climbed one branch should see the
        /// one skill they have, not two empty frames.
        /// </summary>
        void RefreshSkills(bool live)
        {
            for (int slot = 0; slot < _skills.Count; slot++)
            {
                var button = _skills[slot];
                var skill = SkillAt(slot);
                button.Parts.Rect.gameObject.SetActive(skill != null);
                if (skill == null) continue;

                button.Parts.Label.text = skill.Name.ToUpperInvariant();
                button.Cost.text = skill.ManaCost.ToString();
                // Usable means the rules would accept it right now, asked of the rules: a self-cast is asked outright,
                // and a targeted one is usable when there is something it can reach.
                bool usable = live && (skill.Target == SkillTarget.Self
                    ? Commands.Validate(Run, PlayerCommand.Skill(slot), Catalog, out _)
                    : SkillTargets(Run, slot).Count > 0);
                bool selected = _mode == TargetMode.Skill && _skillSlot == slot;
                button.Selected.enabled = selected;
                button.Group.alpha = usable || selected ? 1f : 0.45f;
            }
        }

        void SetAbility(CommandKind kind, bool selected, bool usable, string badge, bool manaCost = false)
        {
            var ability = _abilities[kind];
            ability.Selected.enabled = selected;
            ability.Group.alpha = usable || selected ? 1f : 0.45f;
            if (_matched && kind == CommandKind.Move) ability.Parts.Background.color = selected ? Color.white : new Color(0.62f, 0.62f, 0.66f);
            ability.BadgeBack.gameObject.SetActive(badge != null);
            if (badge != null) ability.Badge.text = badge;
            // A mana price reads blue, a potion count white.
            ability.Badge.color = manaCost ? Palette.ManaText : Color.white;
        }

        void UpdateInspector()
        {
            var run = Run;
            var floor = run.Floor;
            var sb = new StringBuilder();

            // Nothing hovered in plain movement: the goal is on the left, so the panel steps aside as in the reference.
            _inspectPanel.SetActive(_hover.HasValue || _mode != TargetMode.Move);
            if (!_hover.HasValue)
            {
                _inspectTitle.text = "INSPECT";
                if (floor.IsBossFloor && !floor.ExitUnlocked) sb.AppendLine($"Goal: defeat {Lines.BossName(run, Catalog)} to open the exit.");
                else if (floor.ExitUnlocked || run.Hero.HasKey) sb.AppendLine("Goal: reach the EXIT.");
                else sb.AppendLine("Goal: find the KEY, then reach the EXIT.");
                sb.AppendLine();
                switch (_mode)
                {
                    case TargetMode.Slash:
                        // A shooting class reaches further (D-063), and the hint has to say so or its lit tiles look wrong.
                        int reach = Board.SlashReach(run);
                        sb.AppendLine(reach > 1
                            ? $"SLASH: tap a lit monster up to {reach} tiles away, down a straight line you have uncovered. A bomb is still armed from beside it."
                            : "SLASH: tap a lit tile next to you. Bombs can be slashed to arm them.");
                        break;
                    case TargetMode.Dash:
                        // MAINT-32: three classes dash a single tile; the hint used to promise them two.
                        sb.AppendLine(Catalog.HeroClass(run.Hero.ClassId).DashDistance > 1
                            ? "DASH: tap a lit tile one or two steps away in a straight line. You jump over the middle tile."
                            : "DASH: tap a lit tile one step away in a straight line, diagonals too.");
                        break;
                    default:
                        // Free Roam has no sensing (D-021), so there is nothing to learn by hovering a covered tile.
                        sb.AppendLine(run.Movement == MovementMode.Step
                            ? "Tap a lit tile next to you to step. Tap an enemy beside you to slash, a chest to open it, or yourself to wait.\n\nHover any tile to learn what is known about it."
                            : "Tap any tile to uncover it. Covered tiles hide what is on them until you click them; if something is in the way, you stay put and see what it was.\n\nTap an enemy beside you to slash, a chest to open it, or yourself to wait.");
                        break;
                }
                int incoming = Threats.DamageAt(_threats, run.Hero.Pos);
                if (incoming > 0) sb.AppendLine($"\n<color=#FF6B5E>Your tile is hit for {incoming} next turn!</color>");
                _inspectBody.text = sb.ToString();
                ShowInspectPortrait(null);
                return;
            }

            var p = _hover.Value;
            sb.Append(InspectTile(run, p, Catalog, out var title));

            // REL-42: only for ground the player has uncovered. Over a cover this printed "Danger -n next turn" about
            // a tile the panel had just called UNKNOWN.
            int damage = run.Floor[p].Knowledge == Knowledge.Revealed ? Threats.DamageAt(_threats, p) : 0;
            if (damage > 0) sb.AppendLine($"\n<color=#FF6B5E>Danger: -{damage} next turn to whoever stands here.</color>");

            _inspectTitle.text = title;
            _inspectBody.text = sb.ToString();
            ShowInspectPortrait(InspectPortraitKey(run, p));
        }

        /// <summary>
        /// The portrait art key for a hovered tile, or null. Only a monster the player can see has one: a sleeping monster is
        /// part of its cover (D-023 amendment).
        /// </summary>
        public static string InspectPortraitKey(RunState run, GridPos p)
        {
            var enemy = run.Floor.EnemyAt(p);
            return enemy != null && enemy.Awake ? ArtKeys.EnemyPortrait(enemy.DefId) : null;
        }

        /// <summary>Shows the portrait when the catalog has that art, and nothing otherwise.</summary>
        void ShowInspectPortrait(string key)
        {
            bool show = key != null && UiArt.Apply(_inspectPortrait, key);
            _inspectPortrait.gameObject.SetActive(show);
        }

        /// <summary>
        /// Inspect text for one hovered tile. A tile the player has not uncovered only ever reads as unknown, or as its sensed
        /// clue in Step by Step: terrain, content and sleeping monsters under a cover must not leak through hovering.
        /// </summary>
        public static string InspectTile(RunState run, GridPos p, ContentCatalog catalog, out string title)
        {
            var floor = run.Floor;
            var sb = new StringBuilder();
            var cell = floor[p];
            var enemy = floor.EnemyAt(p);

            if (p == run.Hero.Pos)
            {
                var hero = run.Hero;
                // MAINT-32: nine heroes play this game; only one of them is the mascot.
                title = Lines.HeroName(run, catalog).ToUpperInvariant();
                sb.AppendLine($"HP {hero.Hp}/{hero.MaxHp}   Slash {Lines.SlashRange(run, catalog)}   Potions {hero.Potions}");
                var heroClass = catalog.HeroClass(hero.ClassId);
                sb.AppendLine($"Mana {hero.Mana}/{hero.MaxMana}: shield costs {Mana.ShieldCost(run, heroClass)}, dash {Mana.DashCost(run, heroClass)}.");
                sb.AppendLine($"+{Mana.PerTurn} mana every turn, full on every new floor.");
                sb.AppendLine($"Turn {run.Turn + 1}. Key: {(hero.HasKey ? "yes" : "no")}{(hero.SpecialKeys > 0 ? $". Special keys: {hero.SpecialKeys}" : "")}.");
                sb.AppendLine(Goal(run, catalog));
                sb.AppendLine("Tap him to wait a turn.");
                var underfoot = UnderfootText(cell, floor, Board.ExitReadsOpen(run));
                if (underfoot != null) sb.AppendLine(underfoot);
            }
            else if (enemy != null && enemy.Awake)
            {
                var def = catalog.Enemy(enemy.DefId);
                title = def.DisplayName.ToUpperInvariant();
                sb.AppendLine($"HP {enemy.Hp}/{enemy.MaxHp}");
                // A bonus the player cannot see is a bonus they cannot plan around (D-072).
                if (def.Undead) sb.AppendLine("<color=#B8BCC4>Undead.</color> Holy damage answers it.");
                // REL-36: the same number the tile band shows, Fury included - the two disagreed on an enraged boss.
                sb.AppendLine(Lines.IntentExplain(enemy, def, Renown.Hit(run, catalog, 0) + EnemyAi.Fury(enemy)));
                var underfoot = UnderfootText(cell, floor, Board.ExitReadsOpen(run));
                if (underfoot != null) sb.AppendLine(underfoot);
            }
            // Nothing under a cover may leak through hovering: not terrain, content or a sleeping monster (D-021, D-023).
            else if (cell.Knowledge == Knowledge.Unseen)
            {
                title = "UNKNOWN";
                sb.AppendLine(run.Movement == MovementMode.Step
                    ? "Get within two steps to sense what is here."
                    : "Covered. Click it to find out what is here.");
            }
            else if (cell.Knowledge == Knowledge.Sensed)
            {
                title = "SENSED";
                sb.AppendLine(Lines.ClueExplain(Board.ClueAt(floor, p)));
                sb.AppendLine("Click it to uncover it.");
            }
            else if (cell.Terrain == Terrain.Wall)
            {
                title = "WALL";
                sb.AppendLine("Solid stone. Blocks movement and fire.");
            }
            else if (cell.Terrain == Terrain.Pit)
            {
                title = "PIT";
                sb.AppendLine(Board.CanFallThrough(run)
                    ? $"Step in to drop to the next floor for {catalog.Hazards.FallDamage} HP. You leave this floor's key and loot behind."
                    : "Nothing below this one. Nobody crosses, and fire flies right over it.");
            }
            else if (cell.Terrain == Terrain.Door)
            {
                title = "VAULT DOOR";
                sb.AppendLine(cell.IsOpenDoor
                    ? "Open. Step in for the treasure room: guards inside, and the way back is this door."
                    : "Locked. Find the pressure plate on this floor to open it.");
            }
            // REL-39: a sleeping mimic is drawn as a closed chest, so it must read as one too. Falling through to
            // STONE FLOOR / "Nothing here." identified every mimic on the floor without spending a turn.
            else if (Board.SleepingMimicAt(floor, p))
            {
                title = "CHEST";
                sb.AppendLine($"Tap it from its tile or beside it: {Chests.TapsToOpen(run, cell) - cell.ChestTaps} more tap(s), each a turn.");
            }
            else
            {
                title = "STONE FLOOR";
                if (cell.IsExit)
                {
                    // A vault's way out is the door the hero came in by, at the middle of the room - it leads back to the
                    // floor, not down to the next one, and the panel has to say which (D-064).
                    title = floor.IsVault ? "DOOR" : "EXIT";
                    sb.AppendLine(floor.IsVault ? "The way you came in. Step on it to go back out to the floor."
                        : floor.ExitUnlocked ? "Open. Step on it to descend."
                        : floor.IsBossFloor ? $"Sealed until {Lines.BossName(run, catalog)} falls."
                        : run.Hero.HasKey ? "Your key fits. Step on it to descend." : "Locked. Find the key first.");
                }
                if (cell.Hazard == HazardKind.Spikes)
                {
                    title = "SPIKES";
                    sb.AppendLine($"Stepping on costs {Renown.Trap(run, catalog, catalog.Hazards.SpikeDamage)} HP. Dash jumps over. Enemies avoid spikes.");
                }
                else if (cell.Hazard == HazardKind.Lava)
                {
                    title = "LAVA";
                    sb.AppendLine($"Wading through costs {Renown.Trap(run, catalog, catalog.Hazards.LavaDamage)} HP, every time. Shield does not help. Dash jumps over.");
                }
                else if (cell.Hazard == HazardKind.Bomb)
                {
                    title = cell.BombArmed ? "ARMED BOMB" : "BOMB";
                    sb.AppendLine(!cell.BombArmed
                        ? $"Step on it or slash it to arm it. It explodes after your next action, hitting everything in a 3x3 for {Renown.Trap(run, catalog, catalog.Hazards.BombDamage)}."
                        : cell.BombFuse == 0 ? "Explodes after your next action! Get two tiles away or Shield." : "Explodes in two turns.");
                }
                if (cell.Content == ContentKind.Key)
                {
                    title = "KEY";
                    sb.AppendLine("Opens this floor's exit. Walk over it.");
                }
                else if (cell.Content == ContentKind.Chest)
                {
                    title = cell.Premium ? "PREMIUM CHEST" : "CHEST";
                    sb.AppendLine(cell.ChestOpened
                        ? "Already opened."
                        : $"Tap it from its tile or beside it: {Chests.TapsToOpen(run, cell) - cell.ChestTaps} more tap(s), each a turn.");
                    if (cell.Premium && !cell.ChestOpened)
                        sb.AppendLine(run.Hero.SpecialKeys > 0
                            ? $"Your special key opens it for {Chests.RewardDraws(cell, catalog)} rewards."
                            : "Locked: it needs a special key.");
                }
                else if (cell.Content == ContentKind.Potion)
                {
                    title = "POTION";
                    sb.AppendLine("Walk over it to pick it up.");
                }
                else if (cell.Content == ContentKind.Fountain)
                {
                    title = "HEALING FOUNTAIN";
                    sb.AppendLine(cell.Used ? "Already drained." : $"Walk over it to heal {catalog.Hazards.FountainHeal}. It only works once.");
                }
                else if (cell.Content == ContentKind.Teleport)
                {
                    title = "TELEPORT PAD";
                    sb.AppendLine("Walk onto it to appear on the other pad. Free, and it costs no extra turn.");
                }
                else if (cell.Content == ContentKind.PressurePlate)
                {
                    title = "PRESSURE PLATE";
                    sb.AppendLine(cell.Used ? "Already pressed. The vault door is open." : "Step on it to open the vault door on this floor.");
                }
                if (sb.Length == 0) sb.AppendLine("Nothing here.");
            }

            return sb.ToString();
        }

        static string UnderfootText(CellState cell, FloorState floor, bool exitOpen)
        {
            if (cell.Knowledge != Knowledge.Revealed) return null;
            if (cell.Hazard == HazardKind.Spikes) return "Standing on spikes. They only hurt when stepped onto.";
            if (cell.BombArmed)
                return cell.BombFuse == 0
                    ? "<color=#FF9A2E>Standing on an armed bomb: it explodes after the next action!</color>"
                    : "<color=#FF9A2E>Standing on an armed bomb: it explodes in two turns.</color>";
            if (cell.IsExit)
                // The exit only triggers when entered, so a hero already on it (Blobert fell meanwhile) must step off and back on.
                return floor.IsVault ? "Standing in the vault's doorway. Step off and back on to leave."
                    : exitOpen ? "Standing on the open exit. Step off and back on to leave."
                    : floor.IsBossFloor ? "Standing on the sealed exit." : "Standing on the locked exit.";
            return null;
        }

        // ------------------------------------------------------------------ layout

        // The reference gameplay screen's slots, on a 1920 × 1080 canvas (its 1672-wide art scaled up): board frame from 132 to
        // 792 down, ability buttons under it, the INVENTORY / TALENTS / SHOP bar along the bottom.
        const float BoardY = 78f, BoardScale = 0.887f;

        // The reference gameplay screen's board: its stone frame's inner edge, in its own pixels, and the same on the canvas.
        static readonly Vector2 MatchedBoardCenter = new Vector2(840f * Scale - 960f, 540f - 405f * Scale);
        static readonly Vector2 MatchedBoardInterior = new Vector2(744f * Scale, 510f * Scale);

        void BuildTopLeft()
        {
            if (_matched)
            {
                BuildMatchedTopLeft();
                return;
            }
            var logo = UiFactory.Text(Root, "Logo", "ClickDungeon", 60, Palette.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            logo.horizontalOverflow = HorizontalWrapMode.Overflow;
            logo.rectTransform.Place(TopLeft, TopLeft, new Vector2(40f, -14f), new Vector2(480f, 96f));
            UiFactory.Outline(logo, Palette.Ink, 3f);
            UiFactory.Shadow(logo, new Color(0f, 0f, 0f, 0.8f), 5f);
            Icons.ReplaceTextWithArt(logo, ArtKeys.Logo);

            var portrait = UiFactory.Rect(Root, "Portrait");
            portrait.Place(TopLeft, TopLeft, new Vector2(572f, -12f), new Vector2(128f, 128f));
            _face = Icons.Portrait(portrait, 128f);
            _portraitArt = Icons.TryArtImage(portrait, SpeechPortraitKey(FaceId, Expression.Neutral), 128f);
            var portraitFrame = UiFactory.Image(portrait, "Frame", Palette.Gold, Shapes.Frame, true);
            portraitFrame.rectTransform.Stretch();
            UiArt.Apply(portraitFrame, ArtKeys.PortraitFrame);

            // The profile's level on the portrait's corner, as in the reference (D-027). A run never changes it: it is
            // what the player brought in.
            var badge = UiFactory.Image(portrait, "LevelBadge", Palette.Navy, Shapes.Rounded, true);
            badge.rectTransform.Place(new Vector2(0f, 0f), Center, new Vector2(8f, 12f), new Vector2(46f, 46f));
            var badgeEdge = UiFactory.Image(badge.rectTransform, "Edge", Palette.Gold, Shapes.Frame, true);
            badgeEdge.rectTransform.Stretch();
            _levelBadge = UiFactory.Text(badge.rectTransform, "Level", "1", 28, Palette.TextLight, TextAnchor.MiddleCenter, FontStyle.Bold);
            _levelBadge.rectTransform.Stretch();

            // HP and mana in the reference's two bar slots (D-032).
            BuildBars();

            _coins = PurseRow("Coins", -21f, ArtKeys.CoinIcon, Palette.Gold);
            _gems = PurseRow("Gems", -78f, ArtKeys.GemIcon, Palette.Summon);

            var plaque = UiFactory.Rect(Root, "FloorPlaque");
            plaque.Place(TopLeft, TopLeft, new Vector2(126f, -118f), new Vector2(306f, 84f));
            var plaqueShadow = UiFactory.Image(plaque, "Shadow", new Color(0f, 0f, 0f, 0.5f), Shapes.Rounded, true);
            plaqueShadow.rectTransform.Stretch(-4, 2, -8, -10);
            var plaqueBack = UiFactory.Image(plaque, "Back", Palette.Parchment, Shapes.Rounded, true);
            plaqueBack.rectTransform.Stretch();
            var plaqueBorder = UiFactory.Image(plaque, "Border", Palette.GoldDark, Shapes.Frame, true);
            plaqueBorder.rectTransform.Stretch();
            if (UiArt.ApplyPanel(plaqueBack, plaqueBorder, ArtKeys.FloorPlaque)) plaqueShadow.enabled = false;
            _floorTitle = UiFactory.Text(plaque, "Title", "", 34, Palette.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            _floorTitle.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(290f, 42f));
            _floorName = UiFactory.Text(plaque, "Name", "", 18, Palette.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            _floorName.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(290f, 28f));
        }

        /// <summary>HP and mana in the reference's two bar slots (D-032), drawn over the background's sample bars.</summary>
        void BuildBars()
        {
            var hp = Bar("Hp", 17f, Palette.Hp, ArtKeys.HpFill, out _hpFill, out _hpText);
            var heart = UiFactory.Rect(hp, "Heart");
            heart.Place(new Vector2(0f, 0.5f), Center, new Vector2(10f, 0f), new Vector2(58f, 58f));
            if (!Icons.TryArt(heart, ArtKeys.Heart, 58f))
            {
                var heartColor = Palette.Hp.Dim(1.2f);
                Icons.Shape(heart, Shapes.Circle, heartColor, new Vector2(-9f, 5f), new Vector2(30f, 30f));
                Icons.Shape(heart, Shapes.Circle, heartColor, new Vector2(9f, 5f), new Vector2(30f, 30f));
                Icons.Shape(heart, Shapes.Triangle, heartColor, new Vector2(0f, -9f), new Vector2(44f, 32f), 180f);
            }

            var mana = Bar("Mana", 67f, Palette.Mana, ArtKeys.ManaFill, out _manaFill, out _manaText);
            var orb = UiFactory.Rect(mana, "Orb");
            orb.Place(new Vector2(0f, 0.5f), Center, new Vector2(12f, 0f), new Vector2(52f, 52f));
            if (!Icons.TryArt(orb, ArtKeys.ManaIcon, 52f))
                Icons.Shape(orb, Shapes.Circle, Palette.Mana, Vector2.zero, new Vector2(40f, 40f));
        }

        static readonly Color PlaqueInk = new Color(0.16f, 0.1f, 0.05f);

        /// <summary>
        /// The reference's HUD, which the background already shows: the logo and the mascot's painted portrait are its own; the
        /// level shield, floor plaque and purse fields are cleaned patches with live text; the bars are drawn over its
        /// sample bars; the "+" buttons are taps on its own.
        /// </summary>
        void BuildMatchedTopLeft()
        {
            // The playing hero's face goes over the mascot painted into the reference.
            var portrait = AtRef(Root, "Portrait", 500f, 14f, 110f, 104f);
            _portraitRoot = portrait.gameObject;
            UiFactory.Image(portrait, "Back", new Color(0.06f, 0.07f, 0.1f), Shapes.Rounded, true).rectTransform.Stretch();
            _face = Icons.Portrait(portrait, 110f * Scale);
            _portraitArt = Icons.TryArtImage(portrait, SpeechPortraitKey(FaceId, Expression.Neutral), 110f * Scale);
            var portraitFrame = UiFactory.Image(portrait, "Frame", Palette.Gold, Shapes.Frame, true);
            portraitFrame.rectTransform.Stretch();
            UiArt.Apply(portraitFrame, ArtKeys.PortraitFrame);

            PatchAt(Root, ArtKeys.HudLevelBadge, 496f, 76f, 46f, 50f);
            _levelBadge = TextIn(AtRef(Root, "Level", 496f, 82f, 46f, 36f), "Level", 30, new Color(0.96f, 0.87f, 0.5f), TextAnchor.MiddleCenter);

            BuildBars();

            PatchAt(Root, ArtKeys.HudCoinField, 1030f, 17f, 145f, 40f);
            _coins = TextIn(AtRef(Root, "Coins", 1030f, 17f, 116f, 40f), "Coins", 29, Color.white, TextAnchor.MiddleRight);
            HotspotAt(Root, "CoinsPlus", 1174f, 15f, 46f, 44f, () => OpenPurse(true));
            PatchAt(Root, ArtKeys.HudGemField, 1030f, 67f, 145f, 40f);
            _gems = TextIn(AtRef(Root, "Gems", 1030f, 67f, 116f, 40f), "Gems", 29, Color.white, TextAnchor.MiddleRight);
            HotspotAt(Root, "GemsPlus", 1174f, 65f, 46f, 44f, () => OpenPurse(false));

            PatchAt(Root, ArtKeys.HudPlaque, 110f, 105f, 268f, 70f);
            _floorTitle = UiFactory.Text(AtRef(Root, "FloorTitle", 110f, 112f, 268f, 34f), "Title", "", 34, PlaqueInk, TextAnchor.MiddleCenter, FontStyle.Bold);
            _floorTitle.rectTransform.Stretch();
            _floorName = UiFactory.Text(AtRef(Root, "FloorName", 110f, 144f, 268f, 22f), "Name", "", 20, PlaqueInk, TextAnchor.MiddleCenter, FontStyle.Bold);
            _floorName.rectTransform.Stretch();
        }

        /// <summary>One of the two top bars: back, a fill sized by the caller, frame and the value as live text.</summary>
        RectTransform Bar(string name, float y, Color color, string fillKey, out RectTransform fillRect, out Text value)
        {
            var bar = AtRef(Root, name, 640f, y, 322f, 41f);
            var back = UiFactory.Image(bar, "Back", Palette.HpBack, Shapes.Rounded, true);
            back.rectTransform.Stretch();
            UiArt.Apply(back, ArtKeys.HpBack);
            var fillArea = UiFactory.Rect(bar, "FillArea");
            fillArea.Stretch(36, 7, 7, 7);
            var fill = UiFactory.Image(fillArea, "Fill", color, Shapes.Rounded, true);
            fillRect = fill.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            UiArt.Apply(fill, fillKey);
            var border = UiFactory.Image(bar, "Border", Palette.GoldDark, Shapes.Frame, true);
            border.rectTransform.Stretch();
            UiArt.Apply(border, ArtKeys.HpFrame);
            value = UiFactory.Text(bar, "Text", "", 30, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            value.rectTransform.Stretch(36, 0, 0, 0);
            UiFactory.Outline(value, new Color(0f, 0f, 0f, 0.8f), 2f);
            return bar;
        }

        /// <summary>
        /// One purse row from the reference: its icon over the left end of a dark field, the amount, and the "+" that opens the
        /// shop. Both numbers are real (D-025): banked coins and gems plus what this run has found.
        /// </summary>
        Text PurseRow(string name, float y, string iconKey, Color fallback)
        {
            var row = UiFactory.Rect(Root, name);
            row.Place(TopLeft, TopLeft, new Vector2(1137f, y), new Vector2(264f, 48f));
            var field = UiFactory.Image(row, "Field", new Color(0.03f, 0.04f, 0.06f, 0.92f), Shapes.Rounded, true);
            field.rectTransform.Stretch(22, 3, 54, 3);
            var edge = UiFactory.Image(field.rectTransform, "Edge", Palette.GoldDark.WithAlpha(0.45f), Shapes.Frame, true);
            edge.rectTransform.Stretch();

            var icon = UiFactory.Rect(row, "Icon");
            icon.Place(new Vector2(0f, 0.5f), Center, new Vector2(24f, 0f), new Vector2(48f, 48f));
            if (Icons.TryArtImage(icon, iconKey, 48f) == null)
                Icons.Shape(icon, Shapes.Circle, fallback, Vector2.zero, new Vector2(38f, 38f));

            var amount = UiFactory.Text(row, "Amount", "0", 28, Palette.TextLight, TextAnchor.MiddleRight, FontStyle.Bold);
            amount.rectTransform.Stretch(56, 0, 68, 0);
            UiFactory.Shadow(amount, Color.black, 2f);

            bool coins = name == "Coins";
            var plus = UiFactory.Button(row, "Plus", "+", Palette.GoldDark, 34, () => OpenPurse(coins));
            plus.Rect.Place(new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(50f, 50f));
            if (UiArt.ApplyPanel(plus.Background, plus.Border, ArtKeys.PlusButton)) plus.Label.enabled = false;
            return amount;
        }

        /// <summary>Settings and the menu (☰) in the reference's top-right slots. WHAT HAPPENED and HOW TO PLAY live in the menu.</summary>
        void BuildTopRight()
        {
            if (_matched)
            {
                // The reference's settings and menu buttons are the background's; these are their taps.
                HotspotAt(Root, "Settings", 1472f, 20f, 72f, 72f, () => Menus.OpenSettings(_modal, _modal.Hide, _app.ApplyTelemetrySetting));
                HotspotAt(Root, "Menu", 1561f, 20f, 72f, 72f, OpenPause);
                return;
            }
            var settings = UiFactory.Button(Root, "Settings", "", Palette.Navy, 10,
                () => Menus.OpenSettings(_modal, _modal.Hide, _app.ApplyTelemetrySetting));
            settings.Rect.Place(TopRight, Center, new Vector2(-188f, -67f), new Vector2(86f, 86f));
            // Settings art is the whole button (frame and gear); otherwise draw the procedural gear.
            if (!UiArt.ApplyPanel(settings.Background, settings.Border, ArtKeys.SettingsButton))
            {
                var gear = UiFactory.Rect(settings.Rect, "Gear");
                gear.Place(Center, Center, Vector2.zero, new Vector2(70f, 70f));
                Icons.Gear(gear, Palette.Gold, 58f);
            }

            var menu = UiFactory.Button(Root, "Menu", "", Palette.Navy, 10, OpenPause);
            menu.Rect.Place(TopRight, Center, new Vector2(-84f, -67f), new Vector2(86f, 86f));
            if (!UiArt.ApplyPanel(menu.Background, menu.Border, ArtKeys.MenuButton))
                for (int i = -1; i <= 1; i++)
                    Icons.Shape(menu.Rect, Shapes.Rounded, Palette.Gold, new Vector2(0f, i * 16f), new Vector2(46f, 8f));
        }

        Text BuildPanel(string name, Vector2 anchor, Vector2 pos, string title, out Text titleText)
        {
            var rt = UiFactory.Rect(Root, name);
            rt.Place(anchor, anchor, pos, new Vector2(380f, 600f));
            var back = UiFactory.Image(rt, "Back", Palette.Navy.WithAlpha(0.95f), Shapes.Rounded, true);
            back.rectTransform.Stretch();
            var border = UiFactory.Image(rt, "Border", Palette.GoldDark, Shapes.Frame, true);
            border.rectTransform.Stretch();
            UiArt.ApplyPanel(back, border, ArtKeys.Panel);

            titleText = UiFactory.Text(rt, "Title", title, 30, Palette.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            titleText.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(350f, 44f));
            UiFactory.Shadow(titleText, new Color(0f, 0f, 0f, 0.8f), 2f);
            var divider = UiFactory.Image(rt, "Divider", Palette.GoldDark, null);
            divider.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -64f), new Vector2(320f, 3f));

            var body = UiFactory.Text(rt, "Body", "", 23, Palette.TextLight, TextAnchor.UpperLeft);
            body.rectTransform.Stretch(22, 80, 22, 20);
            body.verticalOverflow = VerticalWrapMode.Truncate;
            body.lineSpacing = 1.08f;
            return body;
        }

        /// <summary>The reference gameplay screen's five ability buttons, in its own pixels (MOVE with its selected glow).</summary>
        static readonly Rect[] MatchedAbilityRects =
        {
            new Rect(440f, 686f, 142f, 142f), new Rect(599f, 686f, 143f, 142f), new Rect(761f, 686f, 143f, 142f),
            new Rect(924f, 686f, 143f, 142f), new Rect(1081f, 684f, 146f, 144f),
        };

        /// <summary>The sample art's ability buttons: 112 × 169 on the sheet, drawn here at the same proportions.</summary>
        static readonly Vector2 AbilityArtSize = new Vector2(110f, 166f);
        const float AbilityPitch = 150f;

        /// <summary>
        /// The skill strip (D-075): up to three buttons under the ability row. It is its own row rather than two more
        /// buttons on that one, because the ability bar is laid over the reference art's own five and has nowhere to
        /// put a sixth.
        /// </summary>
        void BuildSkillBar()
        {
            var bar = UiFactory.Rect(Root, "SkillBar");
            bar.Place(Center, Center, new Vector2(0f, -446f), new Vector2(600f, 96f));

            for (int i = 0; i < BoardRules.SkillSlots; i++)
            {
                int slot = i;
                var parts = UiFactory.Button(bar, "Skill" + slot, "", Palette.ShieldButton, 20, () => OnSkill(slot));
                parts.Rect.Place(Center, Center, new Vector2((slot - 1) * 196f, 0f), new Vector2(188f, 84f));
                UiArt.ApplyPanel(parts.Background, parts.Border, ArtKeys.AbilityButtonDefault);
                parts.Label.rectTransform.Place(Center, Center, new Vector2(0f, 8f), new Vector2(180f, 40f));

                var cost = UiFactory.Text(parts.Rect, "Cost", "", 16, Palette.ManaText, TextAnchor.LowerCenter, FontStyle.Bold);
                cost.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(180f, 22f));

                var selected = UiFactory.Image(parts.Rect, "Selected", Palette.Gold, Shapes.Frame, true);
                selected.rectTransform.Stretch(-3, -3, -3, -3);
                selected.enabled = false;

                _skills.Add(new SkillButton
                {
                    Parts = parts,
                    Selected = selected,
                    Cost = cost,
                    Group = parts.Rect.gameObject.AddComponent<CanvasGroup>(),
                });
            }
        }

        /// <summary>The skill in a slot, or null - the one place the screen asks, so it cannot disagree with the rules.</summary>
        SkillDefinition SkillAt(int slot) => Skills.InSlot(Run, Catalog, slot);

        void OnSkill(int slot)
        {
            if (Blocked || _app.AutomationMode) return;
            var skill = SkillAt(slot);
            if (skill == null) return;
            // A skill aimed at the hero needs no tile, so it goes straight in rather than making the player click
            // themselves. One that needs a target lights the tiles it can reach, exactly as SLASH and DASH do.
            if (skill.Target == SkillTarget.Self)
            {
                Submit(PlayerCommand.Skill(slot));
                return;
            }
            if (_mode == TargetMode.Skill && _skillSlot == slot)
            {
                SetMode(TargetMode.Move);
                return;
            }
            _skillSlot = slot;
            SetMode(TargetMode.Skill);
        }

        /// <summary>
        /// The tiles a skill can be used on. Asked of the rules for every tile rather than worked out again here, so the
        /// lit tiles and what the resolver will accept cannot drift apart - the reason Commands exists at all. Static so
        /// it can be tested without standing up a screen.
        /// </summary>
        public static HashSet<GridPos> SkillTargets(RunState run, ContentCatalog catalog, int slot)
        {
            var legal = new HashSet<GridPos>();
            if (Skills.InSlot(run, catalog, slot) == null) return legal;
            foreach (var p in Board.AllCells)
                if (Commands.Validate(run, PlayerCommand.Skill(slot, p), catalog, out _)) legal.Add(p);
            return legal;
        }

        HashSet<GridPos> SkillTargets(RunState run, int slot) => SkillTargets(run, Catalog, slot);

        void BuildAbilityBar()
        {
            var bar = UiFactory.Rect(Root, "AbilityBar");
            // Under the board frame, where the reference's row of five sits.
            bar.Place(Center, Center, new Vector2(0f, -331f), new Vector2(900f, 166f));

            var kinds = new[] { CommandKind.Move, CommandKind.Slash, CommandKind.Shield, CommandKind.Dash, CommandKind.Potion };
            var labels = new[] { "MOVE", "SLASH", "SHIELD", "DASH", "POTION" };
            var colors = new[] { Palette.MoveButton, Palette.SlashButton, Palette.ShieldButton, Palette.DashButton, Palette.PotionButton };

            for (int i = 0; i < kinds.Length; i++)
            {
                var kind = kinds[i];
                var parts = UiFactory.Button(bar, labels[i], labels[i], colors[i], 26, () => OnAbility(kind));
                var group = parts.Rect.gameObject.AddComponent<CanvasGroup>();

                // Matched: the reference gameplay screen's own button, laid exactly over the background's. Otherwise the sample
                // art's own button, at its tall proportions; without it, a bare frame keeps the drawn icon, label and hotkey.
                bool matched = _matched && UiArt.ApplyPanel(parts.Background, parts.Border, ArtKeys.HudAbility(kind));
                bool whole = matched || UiArt.ApplyPanel(parts.Background, parts.Border, ArtKeys.AbilityButton(kind));
                if (matched)
                {
                    parts.Rect.SetParent(Root, false);
                    var r = MatchedAbilityRects[i];
                    RefLayout.Place(parts.Rect, r.x, r.y, r.width, r.height);
                    parts.Label.enabled = false;
                }
                else if (whole)
                {
                    parts.Rect.Place(Center, Center, new Vector2((i - 2) * AbilityPitch, 0f), AbilityArtSize);
                    parts.Label.enabled = false;
                }
                else
                {
                    UiArt.ApplyPanel(parts.Background, parts.Border, ArtKeys.AbilityButtonDefault);
                    parts.Rect.Place(Center, Center, new Vector2((i - 2) * AbilityPitch, 0f), new Vector2(138f, 138f));
                    parts.Label.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(134f, 32f));
                    var icon = UiFactory.Rect(parts.Rect, "Icon");
                    icon.Place(Center, Center, new Vector2(0f, 16f), new Vector2(80f, 80f));
                    Icons.Ability(icon, kind);
                    var hint = UiFactory.Text(parts.Rect, "Hotkey", (i + 1).ToString(), 18, Palette.TextDim, TextAnchor.UpperLeft, FontStyle.Bold);
                    hint.rectTransform.Stretch(12, 8, 0, 0);
                }

                var selected = UiFactory.Image(parts.Rect, "Selected", Palette.Gold, Shapes.Frame, true);
                selected.pixelsPerUnitMultiplier = 1f;
                selected.rectTransform.Stretch(-7, -7, -7, -7);
                selected.enabled = false;
                UiArt.Apply(selected, ArtKeys.AbilitySelected);

                var badgeBack = UiFactory.Image(parts.Rect, "Badge", Palette.Navy, Shapes.Circle);
                badgeBack.rectTransform.Place(TopRight, Center, new Vector2(-12f, -12f), new Vector2(46f, 46f));
                var ring = UiFactory.Image(badgeBack.rectTransform, "Ring", Palette.Gold, Shapes.Ring);
                ring.rectTransform.Stretch();
                if (UiArt.Apply(badgeBack, ArtKeys.CountBadge)) ring.enabled = false;
                // The reference potion button has its own count badge (its sample "2" painted out): the count goes in it.
                if (matched && kind == CommandKind.Potion)
                {
                    RefLayout.Place(badgeBack.rectTransform, 102f, 4f, 28f, 31f);
                    badgeBack.color = Color.clear;
                    ring.enabled = false;
                }
                // The reference's MOVE is drawn selected (its gold glow); it dims while another ability is being aimed.
                if (matched && kind == CommandKind.Move) selected.gameObject.SetActive(false);
                var badge = UiFactory.Text(badgeBack.rectTransform, "Text", "", 26, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                badge.rectTransform.Stretch();

                _abilities[kind] = new AbilityButton { Parts = parts, Selected = selected, BadgeBack = badgeBack, Badge = badge, Group = group };
            }
        }

        /// <summary>
        /// The hero's running commentary and the floor's goal, on the left under the floor plaque. The reference has no
        /// strip along the bottom (its nav bar is there), so what he says sits beside the board instead.
        /// </summary>
        void BuildSpeechStrip()
        {
            var strip = UiFactory.Rect(Root, "Speech");
            strip.Place(TopLeft, TopLeft, new Vector2(40f, -222f), new Vector2(460f, 132f));
            var stripBack = UiFactory.Image(strip, "Back", Palette.Navy.WithAlpha(0.95f), Shapes.Rounded, true);
            stripBack.rectTransform.Stretch();
            var stripBorder = UiFactory.Image(strip, "Border", Palette.GoldDark, Shapes.Frame, true);
            stripBorder.rectTransform.Stretch();
            UiArt.ApplyPanel(stripBack, stripBorder, ArtKeys.SpeechStrip);

            var faceBack = UiFactory.Image(strip, "FaceBack", Palette.Parchment, Shapes.Circle);
            faceBack.rectTransform.Place(new Vector2(0f, 0.5f), Center, new Vector2(42f, 0f), new Vector2(54f, 54f));
            _speechFace = UiFactory.Text(faceBack.rectTransform, "Face", ":)", 24, Palette.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            _speechFace.rectTransform.Stretch();
            _speechFace.horizontalOverflow = HorizontalWrapMode.Overflow;

            _speech = UiFactory.Text(strip, "Line", "", 26, Palette.TextLight, TextAnchor.MiddleLeft, FontStyle.Italic);
            _speech.rectTransform.Stretch(84, 10, 18, 10);
            _speech.resizeTextForBestFit = true;
            _speech.resizeTextMinSize = 16;
            _speech.resizeTextMaxSize = 26;

            if (_matched)
            {
                // The reference keeps the banners beside the board clear: the hero's line shows for a moment, with his
                // face, and fades. The goal is on his INSPECT (hover him) and in the menu.
                _speechGroup = strip.gameObject.AddComponent<CanvasGroup>();
                _speechGroup.blocksRaycasts = false;
                faceBack.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                _speechPortrait = UiFactory.Image(faceBack.rectTransform, "Portrait", Color.white, null);
                _speechPortrait.rectTransform.Stretch();
                _speechPortrait.preserveAspect = true;
                _speechPortrait.enabled = false;
                return;
            }

            var goal = UiFactory.Rect(Root, "Goal");
            goal.Place(TopLeft, TopLeft, new Vector2(40f, -370f), new Vector2(460f, 104f));
            var goalBack = UiFactory.Image(goal, "Back", Palette.Navy.WithAlpha(0.95f), Shapes.Rounded, true);
            goalBack.rectTransform.Stretch();
            var goalBorder = UiFactory.Image(goal, "Border", Palette.GoldDark, Shapes.Frame, true);
            goalBorder.rectTransform.Stretch();
            UiArt.ApplyPanel(goalBack, goalBorder, ArtKeys.SpeechStrip);
            _goal = UiFactory.Text(goal, "Text", "", 24, Palette.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            _goal.rectTransform.Stretch(20, 10, 16, 46);
            _goal.resizeTextForBestFit = true;
            _goal.resizeTextMinSize = 16;
            _goal.resizeTextMaxSize = 24;
            _status = UiFactory.Text(goal, "Status", "", 19, Palette.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            _status.rectTransform.Stretch(20, 58, 16, 12);
            _status.resizeTextForBestFit = true;
            _status.resizeTextMinSize = 13;
            _status.resizeTextMaxSize = 19;
        }

        // The reference's bottom bar on a 1920-wide canvas, and where its three segments meet.
        static readonly Vector2 NavBarSize = new Vector2(1057f, 110f);
        static readonly float[] NavSegments = { 0f, 385f, 724f, 1057f };

        /// <summary>
        /// INVENTORY, TALENTS and SHOP along the bottom, as in the reference. The bar's art carries the icons and labels; each
        /// segment is a button over it. Changes made here are to the profile, so they outfit the next run.
        /// </summary>
        void BuildNavBar()
        {
            var bar = UiFactory.Rect(Root, "NavBar");
            bar.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(4f, 2f), NavBarSize);
            var art = UiFactory.Image(bar, "Art", Palette.Navy, Shapes.Rounded, true);
            art.rectTransform.Stretch();
            bool whole = UiArt.Apply(art, ArtKeys.NavBar);
            if (!whole)
            {
                var edge = UiFactory.Image(bar, "Border", Palette.GoldDark, Shapes.Frame, true);
                edge.rectTransform.Stretch();
            }

            var labels = new[] { "INVENTORY", "TALENTS", "SHOP" };
            var actions = new Action[] { OpenInventory, OpenTalents, OpenShop };
            for (int i = 0; i < labels.Length; i++)
            {
                float left = NavSegments[i], width = NavSegments[i + 1] - NavSegments[i];
                var segment = UiFactory.Button(bar, labels[i], labels[i], Palette.NavyLight, 30, actions[i]);
                segment.Rect.Place(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(left + 8f, 0f), new Vector2(width - 16f, NavBarSize.y - 26f));
                if (whole)
                {
                    // Invisible over the art, but still the tap target.
                    segment.Background.color = Color.clear;
                    segment.Border.enabled = false;
                    segment.Label.enabled = false;
                }
            }

            // The reference's red "!" over TALENTS, only while a talent point is free.
            var badge = Icons.TryArtImage(bar, ArtKeys.AlertBadge, 36f, new Vector2(154f, 25f));
            _talentsBadge = badge != null ? badge.gameObject
                : Icons.Shape(bar, Shapes.Circle, Palette.Danger, new Vector2(154f, 25f), new Vector2(28f, 28f)).gameObject;
            _talentsBadge.GetComponent<Image>().raycastTarget = false;
        }
    }

    /// <summary>Menus shared by the title and game screens.</summary>
    public static class Menus
    {
        public const string HelpText =
            "- Tiles are uncovered only by clicking them. FREE ROAM: tap any tile to go there; if a monster or a shut door is hiding under it, you stay put and it is revealed.\n" +
            "- STEP BY STEP: step to a lit tile next to you. Tiles two steps away are SENSED: red diamond ! = enemy, orange triangle ! = trap, K = key, E = exit, purple + = door, plate or teleport, $ = treasure, dot = safe.\n" +
            "- Tap an enemy beside you to SLASH - the Wizard and the Ranger shoot instead, down any straight line they have uncovered - a chest to open it (2-4 taps, each a turn), or your hero to wait.\n" +
            "- Uncovering an enemy wakes it. It shows its intent and only acts on the NEXT turn. Most must stand next to you to hit; Fire Imps shoot along a line and bosses slam from anywhere.\n" +
            "- Tiles marked -N will be hit next turn. Step off, SHIELD to block (staggers attackers), or DASH over traps (one tile, or two for the classes that dash far).\n" +
            "- SHIELD and DASH cost MANA (the blue bar; the price is on each button). You get 1 back every turn and a full bar on every new floor. Moving, slashing and potions are free.\n" +
            "- Find the KEY, reach the EXIT. Every fifth floor ends in a boss - beat it to heal fully - and Lord Blobert waits at the bottom.\n\n" +
            "Keys: WASD / arrows, Space = wait, 1-5 = abilities, Esc = menu, H = help.";

        public static (string label, Color color, Action action) B(string label, Color color, Action action) => (label, color, action);

        public static readonly Difficulty[] DifficultyOrder = { Difficulty.Easy, Difficulty.Medium, Difficulty.Hardcore };

        /// <summary>Difficulty picker. Names and descriptions come from content, so the menu always matches the rules.</summary>
        public static void OpenDifficulty(ModalOverlay modal, ContentCatalog catalog, Difficulty preselected, Action<Difficulty> choose, Action back)
        {
            var body = new StringBuilder();
            var buttons = new List<(string label, Color color, Action action)>();
            foreach (var tier in DifficultyOrder)
            {
                var info = catalog.DifficultyInfo(tier);
                if (body.Length > 0) body.Append("\n\n");
                body.Append($"<b>{info.DisplayName.ToUpperInvariant()}</b>\n{info.Tagline}");
                var color = tier == preselected ? Palette.PlayGreen : tier == Difficulty.Hardcore ? Palette.QuitRed : Palette.NavyLight;
                buttons.Add(B(info.DisplayName.ToUpperInvariant(), color, () => choose(tier)));
            }
            buttons.Add(B("CANCEL", Palette.NavyLight, back));
            modal.Show("CHOOSE YOUR FATE", body.ToString(), back, buttons.ToArray());
        }

        public static string MovementName(MovementMode mode) => mode == MovementMode.Step ? "Step by Step" : "Free Roam";

        /// <summary>Buys one item and writes the profile. False means the coins were not there and nothing changed.</summary>
        public static bool Buy(GameSession session, ShopItem item)
        {
            if (session?.Profile == null || !Shop.TryBuy(session.Profile, item)) return false;
            session.SaveProfile();
            return true;
        }

        public static void OpenSettings(ModalOverlay modal, Action back, Action changed = null)
        {
            modal.Show("SETTINGS",
                "MOVEMENT applies to your next new run; a run in progress keeps its own.\n" +
                "Free Roam: tap any tile. Step by Step: one neighbouring tile at a time, with nearby hints.\n" +
                "The other settings never change gameplay. The playtest log stays on this device and is never sent anywhere.", back,
                B($"MOVEMENT: {MovementName(UserPrefs.Movement).ToUpperInvariant()}", Palette.NavyLight, () =>
                {
                    UserPrefs.Movement = UserPrefs.Movement == MovementMode.Step ? MovementMode.Free : MovementMode.Step;
                    OpenSettings(modal, back, changed);
                }),
                B($"REDUCED MOTION: {(UserPrefs.ReducedMotion ? "ON" : "OFF")}", Palette.NavyLight, () =>
                {
                    UserPrefs.ReducedMotion = !UserPrefs.ReducedMotion;
                    OpenSettings(modal, back, changed);
                }),
                B($"SCREEN SHAKE: {(UserPrefs.ScreenShake ? "ON" : "OFF")}", Palette.NavyLight, () =>
                {
                    UserPrefs.ScreenShake = !UserPrefs.ScreenShake;
                    OpenSettings(modal, back, changed);
                }),
                B($"PLAYTEST LOG: {(UserPrefs.PlaytestLog ? "ON" : "OFF")}", Palette.NavyLight, () =>
                {
                    UserPrefs.PlaytestLog = !UserPrefs.PlaytestLog;
                    changed?.Invoke();
                    OpenSettings(modal, back, changed);
                }),
                B("BACK", Palette.PlayGreen, back));
        }
    }

    /// <summary>Torch-lit stone wall behind every screen. Kept low-contrast so the board stays dominant.</summary>
    public static class Backdrop
    {
        public static void Build(RectTransform root, string backgroundKey, Vector2[] torches)
        {
            var wall = UiFactory.Rect(root, "Backdrop");
            wall.Stretch();

            // Production background art replaces the procedural wall and torches.
            if (Art.TryGet(backgroundKey, out var background))
            {
                var sprite = background.Frames[0];
                // A darker copy reaches past the stage, filling a window that is wider or taller than 16:9 with the room.
                var bleed = UiFactory.Image(wall, "Bleed " + backgroundKey, new Color(0.4f, 0.4f, 0.42f), sprite);
                bleed.raycastTarget = false;
                var reach = RefLayout.Bleed * 0.5f;
                bleed.rectTransform.Stretch(-reach * 1920f / 1080f, -reach, -reach * 1920f / 1080f, -reach);
                var image = UiFactory.Image(wall, "Art " + backgroundKey, Color.white, sprite);
                image.rectTransform.Stretch();
                var fitter = image.gameObject.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fitter.aspectRatio = sprite.rect.width / sprite.rect.height;
                return;
            }
            const float brickWidth = 150f;
            const float brickHeight = 64f;
            for (int row = -9; row <= 9; row++)
            for (int col = -9; col <= 9; col++)
            {
                float x = col * brickWidth + (row % 2 == 0 ? 0f : brickWidth * 0.5f);
                float y = row * brickHeight;
                float shade = 0.85f + 0.3f * Mathf.PerlinNoise(col * 0.37f + 3.1f, row * 0.53f + 7.7f);
                Icons.Shape(wall, Shapes.Rounded, Palette.StoneDark.Dim(shade).WithAlpha(0.6f), new Vector2(x, y), new Vector2(brickWidth - 8f, brickHeight - 8f));
            }
            foreach (var torch in torches) Torch(wall, torch);
        }

        public static void Torch(Transform parent, Vector2 pos)
        {
            Icons.Shape(parent, Shapes.Circle, Palette.Fuse.WithAlpha(0.08f), pos + new Vector2(0f, 24f), new Vector2(260f, 260f));
            Icons.Shape(parent, Shapes.Circle, Palette.Fuse.WithAlpha(0.16f), pos + new Vector2(0f, 24f), new Vector2(120f, 120f));
            Icons.Shape(parent, Shapes.Square, Palette.ChestWood.Dim(0.7f), pos + new Vector2(0f, -30f), new Vector2(14f, 60f));
            Icons.Shape(parent, Shapes.Rounded, Palette.GoldDark, pos + new Vector2(0f, -2f), new Vector2(44f, 18f));
            Icons.Shape(parent, Shapes.Triangle, Palette.Fuse, pos + new Vector2(0f, 26f), new Vector2(36f, 50f));
            Icons.Shape(parent, Shapes.Triangle, Palette.Gold, pos + new Vector2(0f, 18f), new Vector2(18f, 26f));
        }
    }
}
