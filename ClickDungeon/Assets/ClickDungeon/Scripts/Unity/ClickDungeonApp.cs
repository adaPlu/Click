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

        public ContentCatalog Catalog { get; private set; }
        public FileSaveStore Store { get; private set; }
        public GameSession Session { get; private set; }
        public RectTransform ScreenRoot { get; private set; }

        /// <summary>True when launched with -cdShot (automated screenshots). Uses a separate save folder.</summary>
        public bool AutomationMode { get; private set; }

        void Awake()
        {
            UnityEngine.Application.targetFrameRate = 60;
            AutomationMode = ArgValue("-cdShot") != null;
            _automationTelemetryDir = AutomationMode ? ArgValue("-cdTelemetryDir") : null;
            Catalog = ContentCatalog.CreateDefault();
            Store = new FileSaveStore(Path.Combine(UnityEngine.Application.persistentDataPath, AutomationMode ? "saves-automation" : "saves"));
            Session = new GameSession(Catalog, Store);
            ApplyTelemetrySetting();

            EnsureCamera();
            EnsureEventSystem();
            ScreenRoot = CreateCanvas();
            _title = new TitleScreen(this, ScreenRoot);
            ShowTitle();
        }

        void Start()
        {
            if (AutomationMode) StartCoroutine(AutomationShot());
        }

        /// <summary>
        /// Dev automation: -cdShot path.png [-cdScreen title|game] [-cdSeed n] [-cdTurns n] [-cdTelemetryDir dir] [-cdOverlay name].
        /// Overlays: game pause|help|chest|victory|defeat, title settings|rules.
        /// Telemetry stays off in automation unless -cdTelemetryDir is given, so bot runs never mix with playtest logs.
        /// Plays random legal turns through the normal input path, captures a screenshot and quits.
        /// </summary>
        IEnumerator AutomationShot()
        {
            var path = ArgValue("-cdShot");
            yield return null;
            if (ArgValue("-cdScreen") != "title")
            {
                Store.Delete();
                ulong seed = ulong.TryParse(ArgValue("-cdSeed"), out var s) ? s : 20260914UL;
                OpenGame(Session.StartNewRun(seed), null);
                int turns = int.TryParse(ArgValue("-cdTurns"), out var t) ? t : 0;
                var rng = new DeterministicRng(seed);
                for (int i = 0; i < turns && Session.Run.Status == Domain.RunStatus.InProgress; i++)
                {
                    var moves = Commands.LegalTargets(Session.Run, Domain.CommandKind.Move, Catalog);
                    var slashes = Commands.LegalTargets(Session.Run, Domain.CommandKind.Slash, Catalog);
                    if (slashes.Count > 0) _game.AutomationSubmit(PlayerCommand.Slash(slashes[0]));
                    else if (moves.Count > 0) _game.AutomationSubmit(PlayerCommand.Move(moves[rng.Next(moves.Count)]));
                    else _game.AutomationSubmit(PlayerCommand.Wait());
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

        static string ArgValue(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        void Update()
        {
            if (_game != null) _game.Tick();
            else _title?.Tick();
        }

        JsonlTelemetrySink _telemetrySink;
        string _automationTelemetryDir;

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

        public void StartNewRun()
        {
            ulong seed = (ulong)System.DateTime.UtcNow.Ticks ^ ((ulong)(uint)System.Environment.TickCount << 32);
            var events = Session.StartNewRun(seed);
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

            // Expand keeps the whole 1920×1080 landscape layout visible on every aspect ratio.
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
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

    /// <summary>Per-device presentation preferences. Never read by simulation.</summary>
    public static class UserPrefs
    {
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

        static void SetBool(string key, bool value)
        {
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
