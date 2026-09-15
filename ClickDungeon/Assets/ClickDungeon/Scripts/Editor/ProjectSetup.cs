using System.IO;
using ClickDungeon.Unity;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ClickDungeon.EditorTools
{
    /// <summary>Reproducible project setup so a clean checkout can be opened and built without manual steps.</summary>
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/ClickDungeon/Scenes/Main.unity";
        public const string WindowsBuildPath = "Builds/Windows/ClickDungeon.exe";

        [MenuItem("ClickDungeon/Rebuild Main Scene")]
        public static void CreateMainScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.078f, 0.1f);
            camera.orthographic = true;

            new GameObject("ClickDungeon").AddComponent<ClickDungeonApp>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            ApplyPlayerSettings();
            AssetDatabase.SaveAssets();
            Debug.Log("[ClickDungeon] Main scene ready: " + ScenePath);
        }

        public static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "Clickd";
            PlayerSettings.productName = "ClickDungeon";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
        }

        /// <summary>Written next to the player; make-kit reads it to label the kit with the code the player was built from.</summary>
        public const string BuildStampFile = "BUILD-VERSION.txt";

        [MenuItem("ClickDungeon/Build Windows")]
        public static void BuildWindows()
        {
            if (!File.Exists(ScenePath)) CreateMainScene();
            // The playtest log folder (and collect-logs) depends on the company and product names set here.
            ApplyPlayerSettings();

            // Start from an empty folder, so a failed build can never leave an older player behind to be packaged.
            var buildDir = Path.GetDirectoryName(WindowsBuildPath);
            if (Directory.Exists(buildDir)) Directory.Delete(buildDir, true);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = WindowsBuildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            bool succeeded = report.summary.result == BuildResult.Succeeded;
            if (succeeded) File.WriteAllText(Path.Combine(buildDir, BuildStampFile), GitVersion() + "\n");
            Debug.Log($"[ClickDungeon] Windows build {report.summary.result}: {report.summary.totalErrors} errors -> {WindowsBuildPath}");
            if (UnityEngine.Application.isBatchMode) EditorApplication.Exit(succeeded ? 0 : 1);
        }

        /// <summary>git describe of the working tree, marked -dirty for uncommitted or untracked files; "unknown" without git.</summary>
        static string GitVersion()
        {
            try
            {
                string version = Git("describe --always --dirty");
                if (version.Length == 0) return "unknown";
                if (!version.EndsWith("-dirty") && Git("status --porcelain --untracked-files=normal").Length > 0) version += "-dirty";
                return version;
            }
            catch (System.Exception)
            {
                return "unknown";
            }
        }

        static string Git(string arguments)
        {
            var start = new System.Diagnostics.ProcessStartInfo("git", arguments)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using (var process = System.Diagnostics.Process.Start(start))
            {
                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                return process.ExitCode == 0 ? output : "";
            }
        }
    }
}
