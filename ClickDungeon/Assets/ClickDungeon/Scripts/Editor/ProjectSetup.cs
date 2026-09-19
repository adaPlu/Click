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
            // Phones turn freely between landscape and upright portrait; each has its own layout.
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            ApplyAppIcon();
        }

        public const string AppIconFolder = "Assets/ClickDungeon/Art/AppIcon";

        /// <summary>
        /// The app icon from the ClickDungeon wordmark (tools/art-slicer/make_app_icon.py): the full icon for Windows and as
        /// the default, and on Android the legacy and round icons plus the adaptive icon's background and foreground layers.
        /// Android icon kinds are found by name, so this compiles without the Android module installed.
        /// </summary>
        public static void ApplyAppIcon()
        {
            Texture2D Load(string name)
            {
                string path = $"{AppIconFolder}/{name}.png";
                if (AssetImporter.GetAtPath(path) is TextureImporter importer &&
                    (importer.mipmapEnabled || importer.textureCompression != TextureImporterCompression.Uncompressed || importer.maxTextureSize < 1024))
                {
                    importer.textureType = TextureImporterType.Default;
                    importer.mipmapEnabled = false;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.maxTextureSize = 1024;
                    importer.SaveAndReimport();
                }
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }

            var icon = Load("app_icon");
            var background = Load("app_icon_background");
            var foreground = Load("app_icon_foreground");
            if (icon == null) return;
            PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);

            var android = UnityEditor.Build.NamedBuildTarget.Android;
            foreach (var kind in PlayerSettings.GetSupportedIconKinds(android))
            {
                var icons = PlayerSettings.GetPlatformIcons(android, kind);
                foreach (var platformIcon in icons)
                {
                    if (kind.ToString() == "Adaptive" && background != null && foreground != null)
                        platformIcon.SetTextures(background, foreground);
                    else
                        platformIcon.SetTexture(icon);
                }
                PlayerSettings.SetPlatformIcons(android, kind, icons);
            }
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

        public const string AndroidBuildPath = "Builds/Android/ClickDungeon.apk";

        /// <summary>
        /// An APK to sideload on a phone: landscape or upright portrait, 64-bit ARM (IL2CPP), signed with Unity's debug key.
        /// Uses the SDK, NDK and JDK that ship with the editor's Android module.
        /// </summary>
        [MenuItem("ClickDungeon/Build Android APK")]
        public static void BuildAndroid()
        {
            if (!File.Exists(ScenePath)) CreateMainScene();
            ApplyPlayerSettings();
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.clickd.clickdungeon");
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            EditorUserBuildSettings.buildAppBundle = false;

            var buildDir = Path.GetDirectoryName(AndroidBuildPath);
            if (Directory.Exists(buildDir)) Directory.Delete(buildDir, true);
            Directory.CreateDirectory(buildDir);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = AndroidBuildPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            bool succeeded = report.summary.result == BuildResult.Succeeded;
            if (succeeded) File.WriteAllText(Path.Combine(buildDir, BuildStampFile), GitVersion() + "\n");
            Debug.Log($"[ClickDungeon] Android build {report.summary.result}: {report.summary.totalErrors} errors -> {AndroidBuildPath}");
            if (UnityEngine.Application.isBatchMode) EditorApplication.Exit(succeeded ? 0 : 1);
        }

        public const string IosBuildPath = "Builds/iOS";

        /// <summary>
        /// The iOS player as an Xcode project (Unity exports iOS this way on every OS): landscape or upright portrait, iOS 13
        /// and later. Building, signing and installing it needs a Mac with Xcode and an Apple developer account.
        /// </summary>
        [MenuItem("ClickDungeon/Build iOS Xcode Project")]
        public static void BuildIos()
        {
            if (!File.Exists(ScenePath)) CreateMainScene();
            ApplyPlayerSettings();
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.iOS, "com.clickd.clickdungeon");
            PlayerSettings.iOS.targetOSVersionString = "13.0";
            PlayerSettings.iOS.requiresFullScreen = false;

            if (Directory.Exists(IosBuildPath)) Directory.Delete(IosBuildPath, true);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = IosBuildPath,
                target = BuildTarget.iOS,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            bool succeeded = report.summary.result == BuildResult.Succeeded;
            if (succeeded) File.WriteAllText(Path.Combine(IosBuildPath, BuildStampFile), GitVersion() + "\n");
            Debug.Log($"[ClickDungeon] iOS build {report.summary.result}: {report.summary.totalErrors} errors -> {IosBuildPath}");
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
