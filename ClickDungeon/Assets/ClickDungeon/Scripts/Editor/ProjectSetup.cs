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

        [MenuItem("ClickDungeon/Build Windows")]
        public static void BuildWindows()
        {
            if (!File.Exists(ScenePath)) CreateMainScene();
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = WindowsBuildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[ClickDungeon] Windows build {report.summary.result}: {report.summary.totalErrors} errors -> {WindowsBuildPath}");
            if (UnityEngine.Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
