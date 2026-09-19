using System.Collections;
using System.Collections.Generic;
using System.IO;
using ClickDungeon.Application;
using ClickDungeon.Content;
using ClickDungeon.Simulation;
using ClickDungeon.Unity.Screens;
using ClickDungeon.Unity.Ui;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace ClickDungeon.Unity
{
    /// <summary>Composition root: canvas, input, session and screen switching. Holds no gameplay rules.</summary>
    public sealed class ClickDungeonApp : MonoBehaviour
    {
        public const float ReferenceWidth = 1920f;
        public const float ReferenceHeight = 1080f;

        TitleScreen _title;
        GameScreen _game;

        /// <summary>Content tuned for the current run's difficulty.</summary>
        public ContentCatalog Catalog => Session.Catalog;
        public FileSaveStore Store { get; private set; }
        public GameSession Session { get; private set; }
        public RectTransform ScreenRoot { get; private set; }

        /// <summary>
        /// True when launched with -cdShot (automated screenshots) or -cdWatch (the bot plays on screen). Uses a separate
        /// save folder and a profile in memory, so neither ever touches a real player's saves.
        /// </summary>
        public bool AutomationMode { get; private set; }

        void Awake()
        {
            UnityEngine.Application.targetFrameRate = 60;
            AutomationMode = ArgValue("-cdShot") != null || ArgValue("-cdWatch") != null;
            _automationTelemetryDir = AutomationMode ? ArgValue("-cdTelemetryDir") : null;
            Store = new FileSaveStore(Path.Combine(UnityEngine.Application.persistentDataPath, AutomationMode ? "saves-automation" : "saves"));
            // Automation keeps its coins in memory, so screenshot runs never spend or bank a real player's profile.
            var profiles = AutomationMode
                ? (IProfileStore)new MemoryProfileStore()
                : new FileProfileStore(Path.Combine(UnityEngine.Application.persistentDataPath, "saves"));
            Session = new GameSession(ContentCatalog.CreateDefault(), Store, null, profiles);
            // Screenshots of the between-runs screens need something in them. Automation only: its profile is in memory.
            if (AutomationMode && ArgValue("-cdDemoProfile") != null) FillDemoProfile(Session.Profile);
            ApplyTelemetrySetting();

            EnsureCamera();
            EnsureEventSystem();
            RefLayout.Portrait = ScreenIsPortrait;
            ScreenRoot = CreateCanvas();
            _title = new TitleScreen(this, ScreenRoot);
            ShowTitle();
        }

        void Start()
        {
            if (!AutomationMode) return;
            // An exception would kill the automation coroutine before it quits and leave the caller waiting forever.
            UnityEngine.Application.logMessageReceived += (message, stack, type) =>
            {
                if (type != LogType.Exception) return;
                Debug.LogError("[ClickDungeon] Automation failed, quitting: " + message);
                UnityEngine.Application.Quit(1);
            };
            StartCoroutine(ArgValue("-cdWatch") != null ? WatchBot() : AutomationShot());
        }

        /// <summary>
        /// Dev automation: -cdShot path.png [-cdScreen title|game] [-cdSeed n] [-cdTurns n] [-cdDifficulty easy|medium|hardcore]
        /// [-cdMovement free|step] [-cdBot smart|casual|random] [-cdBlind 1] [-cdTelemetryDir dir] [-cdOverlay name].
        /// -cdBlind makes the bot decide on what the player can see, so a demo run explores instead of walking
        /// straight to a key it should not know about (D-021). It takes a value, so pass "-cdBlind 1".
        /// Overlays: game pause|help|chest|chestburst|banner|bossbanner|victory|defeat, title settings|rules|difficulty.
        /// Telemetry stays off in automation unless -cdTelemetryDir is given, so bot runs never mix with playtest logs.
        /// Plays turns through the normal input path (AutoPlayer by default, or random legal steps), captures a screenshot and quits.
        /// </summary>
        IEnumerator AutomationShot()
        {
            var path = ArgValue("-cdShot");
            yield return null;
            if (ArgValue("-cdScreen") != "title")
            {
                Store.Delete();
                ulong seed = ulong.TryParse(ArgValue("-cdSeed"), out var s) ? s : 20260914UL;
                OpenGame(Session.StartNewRun(seed, LaunchOptions.ParseDifficulty(ArgValue("-cdDifficulty")),
                    LaunchOptions.ParseMovement(ArgValue("-cdMovement")),
                    LaunchOptions.ParseHero(ArgValue("-cdHero"), Catalog, HeroChoice())), null);
                int turns = int.TryParse(ArgValue("-cdTurns"), out var t) ? t : 0;
                bool randomBot = ArgValue("-cdBot") == "random";
                var rng = new DeterministicRng(seed);
                var bot = new AutoPlayer(ArgValue("-cdBot") == "casual" ? AutoPlayer.CasualMistakeRate : 0.0,
                    ArgValue("-cdBlind") != null);
                for (int i = 0; i < turns && Session.Run.Status == Domain.RunStatus.InProgress; i++)
                {
                    if (randomBot)
                    {
                        var moves = Commands.LegalTargets(Session.Run, Domain.CommandKind.Move, Catalog);
                        var slashes = Commands.LegalTargets(Session.Run, Domain.CommandKind.Slash, Catalog);
                        if (slashes.Count > 0) _game.AutomationSubmit(PlayerCommand.Slash(slashes[0]));
                        else if (moves.Count > 0) _game.AutomationSubmit(PlayerCommand.Move(moves[rng.Next(moves.Count)]));
                        else _game.AutomationSubmit(PlayerCommand.Wait());
                    }
                    else
                    {
                        _game.AutomationSubmit(bot.Choose(Session.Run, Catalog, seed * 7919UL + (ulong)i));
                    }
                    for (int f = 0; f < 3; f++) yield return null;
                }
            }
            var overlay = ArgValue("-cdOverlay");
            if (overlay != null)
            {
                if (_game != null) _game.AutomationOverlay(overlay);
                else _title.AutomationOverlay(overlay);
            }
            for (int i = 0; i < 40; i++) yield return null;
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSecondsRealtime(1f);
            Store.Delete();
            UnityEngine.Application.Quit();
        }

        /// <summary>
        /// Watch mode: -cdWatch [seconds per move, default 0.5] [-cdRuns n, default 5] [-cdHero id] [-cdLevel n] [-cdDifficulty ..]
        /// [-cdMovement ..] [-cdBot smart|casual] [-cdSighted 1]. The bot plays whole runs on screen at a pace a person can
        /// follow, through the same input path as a player: it decides blind (only on what is uncovered) unless -cdSighted,
        /// chest reveals and floor banners play out, and each end panel stays up a moment. The profile carries from run to
        /// run, so coins, gear, levels and talents build up; between runs the bot collects its mail and learns the first
        /// talent it can. Without -cdHero it alternates the playable heroes. Esc quits. Results go to the player log.
        /// </summary>
        IEnumerator WatchBot()
        {
            float pace = float.TryParse(ArgValue("-cdWatch"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var p) && p > 0f ? p : 0.5f;
            int runs = int.TryParse(ArgValue("-cdRuns"), out var r) && r > 0 ? r : 5;
            var heroes = new List<string>(Catalog.HeroIdentities.Keys);
            string fixedHero = ArgValue("-cdHero");
            var caption = UiFactory.Text(ScreenRoot, "WatchCaption", "", 24, Palette.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            caption.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 118f), new Vector2(1200f, 34f));
            caption.raycastTarget = false;
            UiFactory.Outline(caption, Color.black, 2f);
            int won = 0;
            var log = new System.Text.StringBuilder();
            // -cdLevel N: the watched hero starts at level N, so the deeper talents come into play within a few runs.
            if (int.TryParse(ArgValue("-cdLevel"), out int startLevel) && startLevel > 1)
                Session.Profile.Xp = Progression.XpForLevel(startLevel);

            for (int run = 1; run <= runs; run++)
            {
                var profile = Session.Profile;
                Mailbox.CollectAll(profile);
                string heroId = fixedHero != null ? LaunchOptions.ParseHero(fixedHero, Catalog, heroes[0]) : heroes[(run - 1) % heroes.Count];
                string classId = Progression.ClassOf(Catalog, heroId);
                var learned = AutoPlayer.LearnTalents(profile, Catalog, classId);
                if (learned.Count > 0) log.AppendLine($"before run {run}, learned: {string.Join(", ", learned)}");

                Store.Delete();
                ulong seed = 20260920UL + (ulong)run * 7919UL;
                OpenGame(Session.StartNewRun(seed, LaunchOptions.ParseDifficulty(ArgValue("-cdDifficulty")),
                    LaunchOptions.ParseMovement(ArgValue("-cdMovement")), heroId), null);
                var bot = new AutoPlayer(ArgValue("-cdBot") == "casual" ? AutoPlayer.CasualMistakeRate : 0.0,
                    blind: ArgValue("-cdSighted") == null);
                caption.transform.SetAsLastSibling();
                caption.text = $"BOT WATCH  ·  run {run}/{runs}  ·  level {Progression.Level(profile)}"
                               + (learned.Count > 0 ? $"  ·  learned {string.Join(", ", learned)}" : "");
                yield return new WaitForSecondsRealtime(learned.Count > 0 ? 3.5f : 2f);

                for (int i = 0; i < 1500 && Session.Run.Status == Domain.RunStatus.InProgress; i++)
                {
                    if (Keyboard.current != null && Keyboard.current.escapeKey.isPressed) { UnityEngine.Application.Quit(); yield break; }
                    var hero = Session.Run.Hero;
                    caption.text = $"BOT WATCH  ·  run {run}/{runs}  ·  {Catalog.HeroIdentity(heroId).DisplayName}  ·  floor {Session.Run.Floor.FloorIndex}"
                                   + $"  ·  turn {Session.Run.Turn + 1}  ·  {hero.Hp}/{hero.MaxHp} hearts  ·  Esc quits";
                    int floor = Session.Run.Floor.FloorIndex;
                    _game.AutomationSubmit(bot.Choose(Session.Run, Catalog, seed * 7919UL + (ulong)i));
                    yield return new WaitForSecondsRealtime(pace);
                    // A chest reveal plays out tap by tap; a new floor's banner gets its moment.
                    while (_game != null && _game.AutomationChestOpen)
                    {
                        yield return new WaitForSecondsRealtime(0.7f);
                        _game.AutomationTapChest();
                    }
                    if (Session.Run.Floor.FloorIndex != floor) yield return new WaitForSecondsRealtime(1.2f);
                }

                var end = Session.Run;
                if (end.Status == Domain.RunStatus.InProgress) Session.Abandon();
                if (end.Status == Domain.RunStatus.Won) won++;
                string line = $"run {run}: {Catalog.HeroIdentity(heroId).DisplayName} seed {seed} {end.Status} on floor {end.Floor.FloorIndex} " +
                              $"after {end.Turn} turns, {end.Hero.Hp}/{end.Hero.MaxHp} hearts, +{end.CoinsFound} coins, +{end.GemsFound} gems, " +
                              $"+{end.XpEarned} XP; level {Progression.Level(Session.Profile)}, {Session.Profile.Items.Count} items";
                log.AppendLine(line);
                Debug.Log("[ClickDungeon] Watch " + line);
                caption.text = $"BOT WATCH  ·  run {run}/{runs}: {end.Status.ToString().ToUpperInvariant()}  ·  {won} won so far";
                yield return new WaitForSecondsRealtime(4f);
            }
            Debug.Log($"[ClickDungeon] Watch finished: won {won} of {runs}.\n{log}");
            caption.text = $"BOT WATCH finished: won {won} of {runs}";
            yield return new WaitForSecondsRealtime(3f);
            Store.Delete();
            UnityEngine.Application.Quit();
        }

        static string ArgValue(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        void Update()
        {
            if (ScreenIsPortrait != RefLayout.Portrait) Relayout();
            if (_game != null) _game.Tick();
            else _title?.Tick();
        }

        static bool ScreenIsPortrait => Screen.height > Screen.width;

        /// <summary>
        /// The device turned (or the window became taller than wide): both screens are built again for the new shape. The
        /// run and the profile live in the session, so nothing is lost; an open menu closes.
        /// </summary>
        void Relayout()
        {
            RefLayout.Portrait = ScreenIsPortrait;
            _scaler.referenceResolution = ReferenceFor(RefLayout.Portrait);
            bool playing = _game != null;
            Destroy(_title.Root.gameObject);
            _title = new TitleScreen(this, ScreenRoot);
            if (playing)
            {
                Destroy(_game.Root.gameObject);
                _title.Root.gameObject.SetActive(false);
                _game = new GameScreen(this, ScreenRoot);
                _game.Reopen();
            }
            else
            {
                _title.Refresh();
            }
        }

        static Vector2 ReferenceFor(bool portrait) =>
            portrait ? new Vector2(RefLayout.PortraitWidth, RefLayout.PortraitHeight) : new Vector2(ReferenceWidth, ReferenceHeight);

        CanvasScaler _scaler;

        JsonlTelemetrySink _telemetrySink;
        string _automationTelemetryDir;

        static void FillDemoProfile(Domain.ProfileState profile)
        {
            profile.Coins = 1248;
            profile.Gems = 152;
            profile.Xp = Progression.XpForLevel(7);
            profile.Items.AddRange(new[] { "steel_sword", "iron_shield", "royal_plate", "healing_charm", "lucky_wand" });
            foreach (var id in new[] { "steel_sword", "iron_shield", "royal_plate", "healing_charm" })
                Inventory.Equip(profile, ContentCatalog.CreateDefault(), id);
            var demo = ContentCatalog.CreateDefault();
            foreach (var id in new[] { "k_opening_strike", "k_opening_strike", "k_sturdy", "k_cleave", "k_executioner" })
                Progression.TryLearn(profile, demo, id);
            profile.RunsFinished = 14;
            profile.RunsWon = 2;
            profile.MonstersSlain = 61;
            profile.ChestsOpened = 23;
            profile.DeepestFloor = 5;
            profile.CoinsEarned = 1630;
            var catalog = ContentCatalog.CreateDefault();
            Achievements.Check(profile, catalog);
            // Some letters read and collected, so the mail shows both kinds.
            for (int i = 0; i < profile.Mail.Count - 3; i++) Mailbox.Collect(profile, profile.Mail[i].Id);
        }

        public string TelemetryDirectory => Path.Combine(UnityEngine.Application.persistentDataPath, "telemetry");
        public bool TelemetryActive => _telemetrySink != null;

        /// <summary>Starts or stops the local playtest log to match the user setting (decision D-015).</summary>
        public void ApplyTelemetrySetting()
        {
            bool wanted = AutomationMode ? _automationTelemetryDir != null : UserPrefs.PlaytestLog;
            if (wanted && _telemetrySink == null)
            {
                try
                {
                    _telemetrySink = new JsonlTelemetrySink(_automationTelemetryDir ?? TelemetryDirectory, System.DateTime.UtcNow);
                    Session.Telemetry = new TelemetryRecorder(_telemetrySink, Catalog);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[ClickDungeon] Playtest log disabled: {ex.Message}");
                    _telemetrySink = null;
                    Session.Telemetry = null;
                }
            }
            else if (!wanted && _telemetrySink != null)
            {
                Session.Telemetry = null;
                _telemetrySink.Dispose();
                _telemetrySink = null;
            }
        }

        void OnDestroy()
        {
            _telemetrySink?.Dispose();
            _telemetrySink = null;
        }

        public void ShowTitle()
        {
            if (_game != null)
            {
                Destroy(_game.Root.gameObject);
                _game = null;
            }
            _title.Root.gameObject.SetActive(true);
            _title.Refresh();
        }

        /// <summary>New run at the difficulty of the run just played (victory / defeat "NEW RUN").</summary>
        /// <summary>The saved hero, or the default one when that identity is no longer in the catalog.</summary>
        string HeroChoice()
        {
            string id = UserPrefs.Hero;
            return Catalog.HeroIdentities.ContainsKey(id) ? id : Content.ContentCatalog.DefaultHeroId;
        }

        public void StartNewRun() => StartNewRun(Session.Catalog.Difficulty);

        public void StartNewRun(Domain.Difficulty difficulty)
        {
            UserPrefs.LastDifficulty = difficulty;
            var events = Session.StartNewRun(LaunchOptions.NewRunSeed(), difficulty, UserPrefs.Movement, HeroChoice());
            OpenGame(events, null);
        }

        public void ContinueRun()
        {
            if (!Session.TryContinue(out var message))
            {
                _title.Refresh();
                _title.Flash(message ?? "There is no run to continue.");
                return;
            }
            OpenGame(null, message);
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            UnityEngine.Application.Quit();
#endif
        }

        void OpenGame(List<GameEvent> events, string notice)
        {
            _title.Root.gameObject.SetActive(false);
            if (_game != null) Destroy(_game.Root.gameObject);
            _game = new GameScreen(this, ScreenRoot);
            _game.Begin(events, notice);
        }

        static void EnsureCamera()
        {
            if (Camera.main != null) return;
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Palette.Background;
            cam.orthographic = true;
        }

        static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
            var module = go.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }

        RectTransform CreateCanvas()
        {
            var go = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.layer = 5;
            go.transform.SetParent(transform, false);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Expand keeps the whole layout visible on every aspect ratio: 1920×1080 in landscape, 1080×1920 in portrait.
            var scaler = go.GetComponent<CanvasScaler>();
            _scaler = scaler;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceFor(RefLayout.Portrait);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var background = UiFactory.Image(go.transform, "Background", Palette.Background);
            background.rectTransform.Stretch();

            var safe = UiFactory.Rect(go.transform, "SafeArea");
            safe.Stretch();
            safe.gameObject.AddComponent<SafeAreaFitter>();
            return safe;
        }
    }

    /// <summary>Keeps UI inside notches and rounded corners on phones.</summary>
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        Rect _applied;
        Vector2Int _screen;

        void OnEnable() => Apply();

        void Update()
        {
            if (Screen.safeArea != _applied || Screen.width != _screen.x || Screen.height != _screen.y) Apply();
        }

        void Apply()
        {
            _applied = Screen.safeArea;
            _screen = new Vector2Int(Screen.width, Screen.height);
            if (Screen.width <= 0 || Screen.height <= 0) return;
            var rt = (RectTransform)transform;
            var min = _applied.position;
            var max = _applied.position + _applied.size;
            rt.anchorMin = new Vector2(min.x / Screen.width, min.y / Screen.height);
            rt.anchorMax = new Vector2(max.x / Screen.width, max.y / Screen.height);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }

    /// <summary>
    /// Per-device preferences. Never read by simulation: the ones that shape a run (difficulty, movement) are handed to a new
    /// run when it starts, and the run keeps them in its save from then on.
    /// </summary>
    public static class UserPrefs
    {
        /// <summary>Movement mode for the next new run (D-021). A run in progress keeps the mode it was started with.</summary>
        public static Domain.MovementMode Movement
        {
            get => PlayerPrefs.GetInt("cd.movementMode", (int)Domain.MovementMode.Free) == (int)Domain.MovementMode.Step
                ? Domain.MovementMode.Step
                : Domain.MovementMode.Free;
            set
            {
                PlayerPrefs.SetInt("cd.movementMode", (int)value);
                PlayerPrefs.Save();
            }
        }

        /// <summary>The hero for the next new run (D-024). A run in progress keeps the hero it was started with.</summary>
        public static string Hero
        {
            get => PlayerPrefs.GetString("cd.hero", Content.ContentCatalog.DefaultHeroId);
            set
            {
                PlayerPrefs.SetString("cd.hero", value);
                PlayerPrefs.Save();
            }
        }

        public static bool ReducedMotion
        {
            get => PlayerPrefs.GetInt("cd.reducedMotion", 0) == 1;
            set => SetBool("cd.reducedMotion", value);
        }

        public static bool ScreenShake
        {
            get => PlayerPrefs.GetInt("cd.screenShake", 1) == 1;
            set => SetBool("cd.screenShake", value);
        }

        /// <summary>Local-only playtest telemetry. On by default in prototype builds.</summary>
        public static bool PlaytestLog
        {
            get => PlayerPrefs.GetInt("cd.playtestLog", 1) == 1;
            set => SetBool("cd.playtestLog", value);
        }

        public static bool SeenHelp
        {
            get => PlayerPrefs.GetInt("cd.seenHelp", 0) == 1;
            set => SetBool("cd.seenHelp", value);
        }

        /// <summary>Preselects the difficulty picker (Squire's Stroll for new players). Each run stores its own tier.</summary>
        public static Domain.Difficulty LastDifficulty
        {
            get => (Domain.Difficulty)PlayerPrefs.GetInt("cd.lastDifficulty", (int)Domain.Difficulty.Easy);
            set
            {
                PlayerPrefs.SetInt("cd.lastDifficulty", (int)value);
                PlayerPrefs.Save();
            }
        }

        static void SetBool(string key, bool value)
        {
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
