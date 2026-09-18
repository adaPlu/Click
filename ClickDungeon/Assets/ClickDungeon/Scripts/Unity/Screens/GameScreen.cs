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
        enum TargetMode { Move, Slash, Dash }

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
        readonly List<string> _log = new List<string>();

        Text _face;
        Image _portraitArt;
        Text _speechFace;
        Text _speech;
        Text _hpText;
        RectTransform _hpFill;
        Text _floorTitle;
        Text _floorName;
        Text _keyChip;
        Text _slashChip;
        Text _turnChip;
        Text _logText;
        Text _inspectTitle;
        Text _inspectBody;
        Image _inspectPortrait;

        TargetMode _mode = TargetMode.Move;
        GridPos? _hover;
        List<Threat> _threats = new List<Threat>();
        string _lastDamageSource;

        public GameScreen(ClickDungeonApp app, RectTransform parent)
        {
            _app = app;
            Root = UiFactory.Rect(parent, "GameScreen");
            Root.Stretch();

            Backdrop.Build(Root, ArtKeys.GameplayBackground, new[] { new Vector2(-420f, 220f), new Vector2(420f, 220f), new Vector2(-420f, -120f), new Vector2(420f, -120f) });
            BuildTopLeft();
            BuildTopRight();
            _logText = BuildPanel("WhatHappened", new Vector2(0f, 0.5f), new Vector2(36f, -60f), "WHAT HAPPENED", out _);
            _logText.text = "<color=#A69F93>Nothing yet.\n\nEvery hit, discovery and wake-up will be explained here, newest first.</color>";
            _inspectBody = BuildPanel("Inspect", new Vector2(1f, 0.5f), new Vector2(-36f, -60f), "INSPECT", out _inspectTitle);
            _inspectPortrait = UiFactory.Image((RectTransform)_inspectBody.transform.parent, "Portrait", Color.white, null);
            _inspectPortrait.preserveAspect = true;
            _inspectPortrait.rectTransform.Place(new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -14f), new Vector2(44f, 44f));
            _inspectPortrait.gameObject.SetActive(false);

            _board = new BoardView(Root, app, new Vector2(0f, 66f));
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
            BuildSpeechStrip();

            // Above the board, over the top HUD band, so it never hides board tiles.
            _floorBanner = new FloorBanner(Root, app, new Vector2(0f, 482f));
            _chest = new ChestOverlay(Root, app);
            _modal = new ModalOverlay(Root, app);
        }

        public RectTransform Root { get; }

        RunState Run => _app.Session.Run;
        ContentCatalog Catalog => _app.Catalog;
        bool Blocked => _modal.IsOpen || _chest.IsOpen || Run == null || Run.Status != RunStatus.InProgress;

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
                _logText.text = "Run resumed.";
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
            var kb = Keyboard.current;
            if (kb == null) return;

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
        }

        /// <summary>Automation hook (screenshots/smoke runs): same path as a player tap.</summary>
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
                default:
                    if (Commands.TryContextual(Run, hero.Step(dir), out var command)) Submit(command);
                    break;
            }
        }

        void OnCellClicked(GridPos p)
        {
            if (Blocked) return;
            switch (_mode)
            {
                case TargetMode.Slash:
                    Submit(PlayerCommand.Slash(p));
                    break;
                case TargetMode.Dash:
                    Submit(PlayerCommand.Dash(p));
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
            if (Blocked) return;
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
                Say(Run.Hero.DashCooldown > 0 ? $"Dash is recharging ({Run.Hero.DashCooldown})." : "No room to dash.", Expression.Worried);
                mode = TargetMode.Move;
            }
            else if (mode == TargetMode.Slash)
            {
                Say("SLASH: pick a lit tile next to you.", Expression.Confident);
            }
            else if (mode == TargetMode.Dash)
            {
                Say("DASH: leap one or two tiles in a straight line, diagonals too, clearing traps.", Expression.Confident);
            }

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
                    $"Lord Blobert is defeated. Again.\n\nDifficulty: {DifficultyName(run)}\nTurns taken: {run.Turn}\nChests opened: {Chests.ChestsOpened(run.Rewards)} ({run.Rewards.Count} rewards)\n{Purse(run)}\n\nSir Clickington: \"Victory! Snacks for everyone!\"",
                    () => { },
                    Menus.B("NEW RUN", Palette.PlayGreen, _app.StartNewRun),
                    Menus.B("TITLE", Palette.NavyLight, _app.ShowTitle));
            }
            else
            {
                _modal.Show(ModalStyle.Defeat, "DEFEATED",
                    $"Fell on floor {run.Floor.FloorIndex}: {floorName}\nFinal blow: {Lines.SourceName(_lastDamageSource ?? "?", Catalog)}\nDifficulty: {DifficultyName(run)}\nTurns survived: {run.Turn}\n{Purse(run)}\n\nThe WHAT HAPPENED log shows every hit.\n\nSir Clickington: \"Tell my horse... wait. I don't have a horse.\"",
                    () => { },
                    Menus.B("NEW RUN", Palette.PlayGreen, _app.StartNewRun),
                    Menus.B("TITLE", Palette.NavyLight, _app.ShowTitle));
            }
        }

        /// <summary>What the run carried out, which the profile has just banked (D-025).</summary>
        static string Purse(RunState run)
        {
            string gems = run.GemsFound > 0 ? $" and {run.GemsFound} gem{(run.GemsFound == 1 ? "" : "s")}" : "";
            return $"Carried out: {run.CoinsFound} coins{gems}";
        }

        // ------------------------------------------------------------------ menus

        void OpenPause()
        {
            if (_chest.IsOpen || Run == null) return;
            var run = Run;
            _modal.Show("PAUSED",
                $"Floor {run.Floor.FloorIndex}: {Catalog.ProfileFor(run.Floor.FloorIndex).Name}\nDifficulty: {DifficultyName(run)}\nMovement: {Menus.MovementName(run.Movement)}\nTurn {run.Turn + 1}    Seed {run.RunSeed}\nYour run is saved after every turn.{(_app.TelemetryActive ? "\nPlaytest log is on (saved on this device only)." : "")}",
                _modal.Hide,
                Menus.B("RESUME", Palette.PlayGreen, _modal.Hide),
                Menus.B("HOW TO PLAY", Palette.NavyLight, OpenHelp),
                Menus.B("SETTINGS", Palette.NavyLight, () => Menus.OpenSettings(_modal, OpenPause, _app.ApplyTelemetrySetting)),
                Menus.B("ABANDON RUN", Palette.QuitRed, ConfirmAbandon),
                Menus.B("QUIT TO TITLE", Palette.NavyLight, _app.ShowTitle));
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
                    _app.Session.Abandon();
                    _app.ShowTitle();
                }),
                Menus.B("KEEP PLAYING", Palette.PlayGreen, _modal.Hide));
        }

        // ------------------------------------------------------------------ rendering

        void Say(string line, Expression face)
        {
            _speech.text = line;
            _speechFace.text = Lines.Face(face);
            _face.text = Lines.Face(face);
            // This hero's face for the expression, then its neutral one: a hero with few portraits must not borrow another's face.
            string heroId = Run?.Hero.IdentityId ?? ArtKeys.HeroId;
            if (_portraitArt != null && (Art.TryGetSprite(ArtKeys.Portrait(heroId, face.ToString()), out var portrait)
                    || Art.TryGetSprite(ArtKeys.Portrait(heroId, "neutral"), out portrait)
                    || Art.TryGetSprite(ArtKeys.Portrait(ArtKeys.HeroId, face.ToString()), out portrait)))
                _portraitArt.sprite = portrait;
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
            _logText.text = string.Join("\n", _log);
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
            _logText.text = string.Join("\n", _log);
        }

        string _saveWarning;

        void Refresh(bool animate)
        {
            var run = Run;
            if (run == null) return;
            WarnIfSaveFailed();
            var hero = run.Hero;

            _hpText.text = $"{hero.Hp} / {hero.MaxHp}";
            _hpFill.anchorMax = new Vector2(Mathf.Clamp01(hero.Hp / (float)hero.MaxHp), 1f);
            _keyChip.text = run.Floor.IsBossFloor ? "BOSS FLOOR" : hero.HasKey ? "KEY: YES" : "KEY: NO";
            _keyChip.color = hero.HasKey || run.Floor.IsBossFloor ? Palette.Gold : Palette.TextLight;
            _slashChip.text = $"SLASH {hero.SlashDamage}";
            _turnChip.text = $"TURN {run.Turn + 1}";
            _floorTitle.text = $"FLOOR {run.Floor.FloorIndex}";
            _floorName.text = run.Floor.IsVault
                ? "THE VAULT"
                : (Catalog.ProfileFor(run.Floor.FloorIndex).Name ?? "").ToUpperInvariant();

            bool live = run.Status == RunStatus.InProgress;
            SetAbility(CommandKind.Move, _mode == TargetMode.Move, live, null);
            SetAbility(CommandKind.Slash, _mode == TargetMode.Slash, live && Commands.LegalTargets(run, CommandKind.Slash, Catalog).Count > 0, null);
            SetAbility(CommandKind.Shield, false, live && hero.ShieldCooldown == 0, hero.ShieldCooldown > 0 ? hero.ShieldCooldown.ToString() : null);
            SetAbility(CommandKind.Dash, _mode == TargetMode.Dash, live && Commands.LegalTargets(run, CommandKind.Dash, Catalog).Count > 0,
                hero.DashCooldown > 0 ? hero.DashCooldown.ToString() : null);
            SetAbility(CommandKind.Potion, false, live && Commands.Validate(run, PlayerCommand.Potion(), Catalog, out _), hero.Potions.ToString());

            _threats = Threats.Compute(run, Catalog);
            RenderBoard(animate);
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

        void SetAbility(CommandKind kind, bool selected, bool usable, string badge)
        {
            var ability = _abilities[kind];
            ability.Selected.enabled = selected;
            ability.Group.alpha = usable || selected ? 1f : 0.45f;
            ability.BadgeBack.gameObject.SetActive(badge != null);
            if (badge != null) ability.Badge.text = badge;
        }

        void UpdateInspector()
        {
            var run = Run;
            var floor = run.Floor;
            var sb = new StringBuilder();

            if (!_hover.HasValue)
            {
                _inspectTitle.text = "INSPECT";
                if (floor.IsBossFloor && !floor.ExitUnlocked) sb.AppendLine("Goal: defeat Lord Blobert to open the exit.");
                else if (floor.ExitUnlocked || run.Hero.HasKey) sb.AppendLine("Goal: reach the EXIT.");
                else sb.AppendLine("Goal: find the KEY, then reach the EXIT.");
                sb.AppendLine();
                switch (_mode)
                {
                    case TargetMode.Slash: sb.AppendLine("SLASH: tap a lit tile next to you. Bombs can be slashed to arm them."); break;
                    case TargetMode.Dash: sb.AppendLine("DASH: tap a lit tile one or two steps away in a straight line. You jump over the middle tile."); break;
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

            int damage = Threats.DamageAt(_threats, p);
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
                title = "SIR CLICKINGTON";
                sb.AppendLine($"HP {hero.Hp}/{hero.MaxHp}   Slash {hero.SlashDamage}   Potions {hero.Potions}");
                sb.AppendLine(hero.ShieldCooldown > 0 ? $"Shield recharging: {hero.ShieldCooldown}" : "Shield ready.");
                sb.AppendLine(hero.DashCooldown > 0 ? $"Dash recharging: {hero.DashCooldown}" : "Dash ready.");
                sb.AppendLine("Tap him to wait a turn.");
                var underfoot = UnderfootText(cell, floor, Board.ExitReadsOpen(run));
                if (underfoot != null) sb.AppendLine(underfoot);
            }
            else if (enemy != null && enemy.Awake)
            {
                var def = catalog.Enemy(enemy.DefId);
                title = def.DisplayName.ToUpperInvariant();
                sb.AppendLine($"HP {enemy.Hp}/{enemy.MaxHp}");
                sb.AppendLine(Lines.IntentExplain(enemy, def));
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
            else
            {
                title = "STONE FLOOR";
                if (cell.IsExit)
                {
                    title = "EXIT";
                    sb.AppendLine(floor.ExitUnlocked ? "Open. Step on it to descend."
                        : floor.IsBossFloor ? "Sealed until Lord Blobert falls."
                        : run.Hero.HasKey ? "Your key fits. Step on it to descend." : "Locked. Find the key first.");
                }
                if (cell.Hazard == HazardKind.Spikes)
                {
                    title = "SPIKES";
                    sb.AppendLine($"Stepping on costs {catalog.Hazards.SpikeDamage} HP. Dash jumps over. Enemies avoid spikes.");
                }
                else if (cell.Hazard == HazardKind.Lava)
                {
                    title = "LAVA";
                    sb.AppendLine($"Wading through costs {catalog.Hazards.LavaDamage} HP, every time. Shield does not help. Dash jumps over.");
                }
                else if (cell.Hazard == HazardKind.Bomb)
                {
                    title = cell.BombArmed ? "ARMED BOMB" : "BOMB";
                    sb.AppendLine(!cell.BombArmed
                        ? $"Step on it or slash it to arm it. It explodes after your next action, hitting everything in a 3x3 for {catalog.Hazards.BombDamage}."
                        : cell.BombFuse == 0 ? "Explodes after your next action! Get two tiles away or Shield." : "Explodes in two turns.");
                }
                if (cell.Content == ContentKind.Key)
                {
                    title = "KEY";
                    sb.AppendLine("Opens this floor's exit. Walk over it.");
                }
                else if (cell.Content == ContentKind.Chest)
                {
                    title = "CHEST";
                    sb.AppendLine(cell.ChestOpened
                        ? "Already opened."
                        : $"Tap it from its tile or beside it: {Chests.TapsToOpen(cell.Quality) - cell.ChestTaps} more tap(s), each a turn.");
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
                return exitOpen ? "Standing on the open exit. Step off and back on to leave."
                    : floor.IsBossFloor ? "Standing on the sealed exit." : "Standing on the locked exit.";
            return null;
        }

        // ------------------------------------------------------------------ layout

        void BuildTopLeft()
        {
            var logo = UiFactory.Text(Root, "Logo", "ClickDungeon", 60, Palette.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            logo.horizontalOverflow = HorizontalWrapMode.Overflow;
            logo.rectTransform.Place(TopLeft, TopLeft, new Vector2(34f, -14f), new Vector2(430f, 96f));
            UiFactory.Outline(logo, Palette.Ink, 3f);
            UiFactory.Shadow(logo, new Color(0f, 0f, 0f, 0.8f), 5f);
            Icons.ReplaceTextWithArt(logo, ArtKeys.Logo);

            var portrait = UiFactory.Rect(Root, "Portrait");
            portrait.Place(TopLeft, TopLeft, new Vector2(470f, -10f), new Vector2(104f, 104f));
            _face = Icons.Portrait(portrait, 104f);
            _portraitArt = Icons.TryArtImage(portrait, ArtKeys.Portrait(ArtKeys.HeroId, "neutral"), 104f);
            var portraitFrame = UiFactory.Image(portrait, "Frame", Palette.Gold, Shapes.Frame, true);
            portraitFrame.rectTransform.Stretch();
            UiArt.Apply(portraitFrame, ArtKeys.PortraitFrame);

            var hp = UiFactory.Rect(Root, "Hp");
            hp.Place(TopLeft, TopLeft, new Vector2(592f, -28f), new Vector2(440f, 54f));
            var hpBack = UiFactory.Image(hp, "Back", Palette.HpBack, Shapes.Rounded, true);
            hpBack.rectTransform.Stretch();
            UiArt.Apply(hpBack, ArtKeys.HpBack);
            var fillArea = UiFactory.Rect(hp, "FillArea");
            fillArea.Stretch(40, 7, 7, 7);
            var fill = UiFactory.Image(fillArea, "Fill", Palette.Hp, Shapes.Rounded, true);
            _hpFill = fill.rectTransform;
            _hpFill.anchorMin = Vector2.zero;
            _hpFill.anchorMax = Vector2.one;
            _hpFill.offsetMin = Vector2.zero;
            _hpFill.offsetMax = Vector2.zero;
            UiArt.Apply(fill, ArtKeys.HpFill);
            var hpBorder = UiFactory.Image(hp, "Border", Palette.GoldDark, Shapes.Frame, true);
            hpBorder.rectTransform.Stretch();
            UiArt.Apply(hpBorder, ArtKeys.HpFrame);

            var heart = UiFactory.Rect(hp, "Heart");
            heart.Place(new Vector2(0f, 0.5f), Center, new Vector2(10f, 0f), new Vector2(64f, 64f));
            if (!Icons.TryArt(heart, ArtKeys.Heart, 64f))
            {
                var heartColor = Palette.Hp.Dim(1.2f);
                Icons.Shape(heart, Shapes.Circle, heartColor, new Vector2(-10f, 6f), new Vector2(34f, 34f));
                Icons.Shape(heart, Shapes.Circle, heartColor, new Vector2(10f, 6f), new Vector2(34f, 34f));
                Icons.Shape(heart, Shapes.Triangle, heartColor, new Vector2(0f, -10f), new Vector2(50f, 36f), 180f);
            }

            _hpText = UiFactory.Text(hp, "Text", "", 32, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            _hpText.rectTransform.Stretch(40, 0, 0, 0);
            UiFactory.Outline(_hpText, new Color(0f, 0f, 0f, 0.8f), 2f);

            _keyChip = Chip("KeyChip", new Vector2(1050f, -32f), 190f);
            _slashChip = Chip("SlashChip", new Vector2(1254f, -32f), 170f);
            _turnChip = Chip("TurnChip", new Vector2(1438f, -32f), 170f);

            var plaque = UiFactory.Rect(Root, "FloorPlaque");
            plaque.Place(TopLeft, TopLeft, new Vector2(100f, -126f), new Vector2(380f, 104f));
            var plaqueShadow = UiFactory.Image(plaque, "Shadow", new Color(0f, 0f, 0f, 0.5f), Shapes.Rounded, true);
            plaqueShadow.rectTransform.Stretch(-4, 2, -8, -10);
            var plaqueBack = UiFactory.Image(plaque, "Back", Palette.Parchment, Shapes.Rounded, true);
            plaqueBack.rectTransform.Stretch();
            var plaqueBorder = UiFactory.Image(plaque, "Border", Palette.GoldDark, Shapes.Frame, true);
            plaqueBorder.rectTransform.Stretch();
            if (UiArt.ApplyPanel(plaqueBack, plaqueBorder, ArtKeys.FloorPlaque)) plaqueShadow.enabled = false;
            _floorTitle = UiFactory.Text(plaque, "Title", "", 42, Palette.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            _floorTitle.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(360f, 54f));
            _floorName = UiFactory.Text(plaque, "Name", "", 22, Palette.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            _floorName.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(360f, 34f));
        }

        Text Chip(string name, Vector2 pos, float width)
        {
            var rt = UiFactory.Rect(Root, name);
            rt.Place(TopLeft, TopLeft, pos, new Vector2(width, 46f));
            var back = UiFactory.Image(rt, "Back", Palette.Navy, Shapes.Rounded, true);
            back.rectTransform.Stretch();
            var border = UiFactory.Image(rt, "Border", Palette.GoldDark, Shapes.Frame, true);
            border.rectTransform.Stretch();
            UiArt.ApplyPanel(back, border, ArtKeys.Chip);
            var text = UiFactory.Text(rt, "Text", "", 24, Palette.TextLight, TextAnchor.MiddleCenter, FontStyle.Bold);
            text.rectTransform.Stretch(6, 2, 6, 2);
            return text;
        }

        void BuildTopRight()
        {
            var pause = UiFactory.Button(Root, "Settings", "", Palette.Navy, 10, OpenPause);
            pause.Rect.Place(TopRight, TopRight, new Vector2(-28f, -16f), new Vector2(98f, 98f));
            // Settings art is the whole button (frame and gear); otherwise draw the procedural gear.
            if (!UiArt.ApplyPanel(pause.Background, pause.Border, ArtKeys.SettingsButton))
            {
                var gear = UiFactory.Rect(pause.Rect, "Gear");
                gear.Place(Center, Center, Vector2.zero, new Vector2(80f, 80f));
                Icons.Gear(gear, Palette.Gold, 66f);
            }

            var help = UiFactory.Button(Root, "Help", "?", Palette.Navy, 58, OpenHelp);
            help.Rect.Place(TopRight, TopRight, new Vector2(-140f, -16f), new Vector2(98f, 98f));
            help.Label.color = Palette.Gold;
            UiArt.ApplyPanel(help.Background, help.Border, ArtKeys.HelpButton);
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

        void BuildAbilityBar()
        {
            var bar = UiFactory.Rect(Root, "AbilityBar");
            bar.Place(Center, Center, new Vector2(0f, -378f), new Vector2(900f, 150f));

            var kinds = new[] { CommandKind.Move, CommandKind.Slash, CommandKind.Shield, CommandKind.Dash, CommandKind.Potion };
            var labels = new[] { "MOVE", "SLASH", "SHIELD", "DASH", "POTION" };
            var colors = new[] { Palette.MoveButton, Palette.SlashButton, Palette.ShieldButton, Palette.DashButton, Palette.PotionButton };

            for (int i = 0; i < kinds.Length; i++)
            {
                var kind = kinds[i];
                var parts = UiFactory.Button(bar, labels[i], labels[i], colors[i], 26, () => OnAbility(kind));
                parts.Rect.Place(Center, Center, new Vector2((i - 2) * 178f, 0f), new Vector2(162f, 144f));
                parts.Label.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(156f, 34f));
                var group = parts.Rect.gameObject.AddComponent<CanvasGroup>();
                // Button art is frame and fill only; icon, label, hotkey and badge still draw on top.
                UiArt.ApplyPanel(parts.Background, parts.Border, ArtKeys.AbilityButton(kind), ArtKeys.AbilityButtonDefault);

                var icon = UiFactory.Rect(parts.Rect, "Icon");
                icon.Place(Center, Center, new Vector2(0f, 18f), new Vector2(90f, 90f));
                Icons.Ability(icon, kind);

                var hint = UiFactory.Text(parts.Rect, "Hotkey", (i + 1).ToString(), 18, Palette.TextDim, TextAnchor.UpperLeft, FontStyle.Bold);
                hint.rectTransform.Stretch(12, 8, 0, 0);

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
                var badge = UiFactory.Text(badgeBack.rectTransform, "Text", "", 26, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                badge.rectTransform.Stretch();

                _abilities[kind] = new AbilityButton { Parts = parts, Selected = selected, BadgeBack = badgeBack, Badge = badge, Group = group };
            }
        }

        void BuildSpeechStrip()
        {
            var strip = UiFactory.Rect(Root, "Speech");
            strip.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(1060f, 80f));
            var stripBack = UiFactory.Image(strip, "Back", Palette.Navy.WithAlpha(0.95f), Shapes.Rounded, true);
            stripBack.rectTransform.Stretch();
            var stripBorder = UiFactory.Image(strip, "Border", Palette.GoldDark, Shapes.Frame, true);
            stripBorder.rectTransform.Stretch();
            UiArt.ApplyPanel(stripBack, stripBorder, ArtKeys.SpeechStrip);

            var faceBack = UiFactory.Image(strip, "FaceBack", Palette.Parchment, Shapes.Circle);
            faceBack.rectTransform.Place(new Vector2(0f, 0.5f), Center, new Vector2(48f, 0f), new Vector2(64f, 64f));
            _speechFace = UiFactory.Text(faceBack.rectTransform, "Face", ":)", 24, Palette.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            _speechFace.rectTransform.Stretch();
            _speechFace.horizontalOverflow = HorizontalWrapMode.Overflow;

            _speech = UiFactory.Text(strip, "Line", "", 28, Palette.TextLight, TextAnchor.MiddleLeft, FontStyle.Italic);
            _speech.rectTransform.Stretch(96, 4, 24, 4);
            _speech.resizeTextForBestFit = true;
            _speech.resizeTextMinSize = 18;
            _speech.resizeTextMaxSize = 28;
        }
    }

    /// <summary>Menus shared by the title and game screens.</summary>
    public static class Menus
    {
        public const string HelpText =
            "- Tiles are uncovered only by clicking them. FREE ROAM: tap any tile to go there; if a monster or a shut door is hiding under it, you stay put and it is revealed.\n" +
            "- STEP BY STEP: step to a lit tile next to you. Tiles two steps away are SENSED: red diamond ! = enemy, orange triangle ! = trap, K = key, E = exit, purple + = door, plate or teleport, $ = treasure, dot = safe.\n" +
            "- Tap an enemy beside you to SLASH, a chest to open it (2-4 taps, each a turn), or Sir Clickington to wait.\n" +
            "- Uncovering an enemy wakes it. It shows its intent and only acts on the NEXT turn. Most must stand next to you to hit; Fire Imps shoot along a line and Lord Blobert slams from anywhere.\n" +
            "- Tiles marked -N will be hit next turn. Step off, SHIELD to block (staggers attackers), or DASH one or two tiles over traps.\n" +
            "- Find the KEY, reach the EXIT. Floor 5: defeat Lord Blobert.\n\n" +
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

        /// <summary>
        /// Picks the hero for the next new run (D-024). A run in progress keeps the hero it was started with, so this only
        /// ever changes what the next run starts as.
        /// </summary>
        public static void OpenHeroSelect(ModalOverlay modal, ContentCatalog catalog, string current, Action<string> pick, Action back)
        {
            var buttons = new List<(string, Color, Action)>();
            foreach (var identity in catalog.HeroIdentities.Values)
            {
                string id = identity.Id;
                bool chosen = id == current;
                buttons.Add(($"{(chosen ? "> " : "")}{identity.DisplayName.ToUpperInvariant()}: {HeroSummary(catalog, identity)}",
                    chosen ? Palette.PlayGreen : Palette.NavyLight, () => pick(id)));
            }
            buttons.Add(B("BACK", Palette.NavyLight, back));
            modal.Show("HERO SELECT",
                "Who takes the next run down? A run already in progress keeps its own hero.\n" +
                HeroLines(catalog, current), back, buttons.ToArray());
        }

        /// <summary>One line of numbers for a hero, so the pick is made on what actually changes.</summary>
        static string HeroSummary(ContentCatalog catalog, HeroIdentityDefinition identity)
        {
            var hero = catalog.HeroClass(identity.ClassId);
            return $"{hero.MaxHp} HP, slash {hero.SlashDamage}, {hero.StartingPotions} potions";
        }

        static string HeroLines(ContentCatalog catalog, string current)
        {
            var sb = new StringBuilder();
            foreach (var identity in catalog.HeroIdentities.Values)
            {
                var hero = catalog.HeroClass(identity.ClassId);
                sb.AppendLine();
                sb.AppendLine($"<color=#F2C14E>{identity.DisplayName}</color> ({hero.DisplayName}){(identity.Id == current ? "  — chosen" : "")}");
                sb.AppendLine(identity.Tagline);
                sb.AppendLine($"{hero.MaxHp} hearts, slash {hero.SlashDamage}, potion heals {hero.PotionHeal}, " +
                    $"{hero.StartingPotions} potions, shield every {hero.ShieldCooldown}, dash {hero.DashDistance} " +
                    $"{(hero.DashDistance == 1 ? "tile" : "tiles")} every {hero.DashCooldown}.");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Spends what runs carried out (D-025). Provisions outfit the next run, so buying mid-run is not possible: the shop
        /// only opens from the title.
        /// </summary>
        public static void OpenShop(ModalOverlay modal, ContentCatalog catalog, ProfileState profile, Action<ShopItem> buy, Action back)
        {
            var buttons = new List<(string, Color, Action)>();
            foreach (ShopItem item in new[] { ShopItem.PotionRation, ShopItem.HeartToken })
            {
                var stock = item;
                bool afford = Shop.CanAfford(profile, stock);
                buttons.Add(($"{Shop.DisplayName(stock)} — {Shop.Price(stock)} COINS{(afford ? "" : " (NOT ENOUGH)")}",
                    afford ? Palette.PlayGreen : Palette.NavyLight, () => buy(stock)));
            }
            buttons.Add(B("DONE", Palette.NavyLight, back));

            var sb = new StringBuilder();
            sb.AppendLine($"<color=#F2C14E>{profile.Coins} coins</color>   <color=#B06BE6>{profile.Gems} gems</color>");
            sb.AppendLine();
            foreach (ShopItem item in new[] { ShopItem.PotionRation, ShopItem.HeartToken })
                sb.AppendLine($"{Shop.DisplayName(item)}: {Shop.Describe(item, catalog)}");
            sb.AppendLine();
            if (profile.PotionRations > 0 || profile.HeartTokens > 0)
                sb.AppendLine($"Waiting for your next run: {profile.PotionRations} potion rations, {profile.HeartTokens} heart tokens.");
            sb.AppendLine("Coins come out of chests and off every floor you finish. Gems are Lord Blobert's alone.");
            modal.Show("SHOP", sb.ToString(), back, buttons.ToArray());
        }

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
