using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tactix.EditorTools
{
    /// <summary>
    /// Batch-mode entry points:
    ///   -executeMethod Tactix.EditorTools.BuildTools.CreateMainScene
    ///   -executeMethod Tactix.EditorTools.BuildTools.BuildWindows
    /// The scene is empty on purpose — Bootstrap spawns the entire game at runtime.
    /// </summary>
    public static class BuildTools
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Tactix/Create Main Scene")]
        public static void CreateMainScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            if (!EditorBuildSettings.scenes.Any(s => s.path == ScenePath))
            {
                EditorBuildSettings.scenes = EditorBuildSettings.scenes
                    .Concat(new[] { new EditorBuildSettingsScene(ScenePath, true) })
                    .ToArray();
            }
            Debug.Log($"Main scene ready at {ScenePath}");
        }

        /// <summary>
        /// Window behaviour for the standalone player: a resizable (and therefore
        /// maximizable) window that starts windowed at 1280x720.
        /// </summary>
        [MenuItem("Tactix/Apply Player Settings")]
        public static void ApplyPlayerSettings()
        {
            PlayerSettings.resizableWindow = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.runInBackground = true;
            PlayerSettings.forceSingleInstance = false;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, true);
        }

        [MenuItem("Tactix/Build Windows")]
        public static void BuildWindows()
        {
            CreateMainScene();
            ApplyPlayerSettings();
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Windows/Tactix.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new BuildFailedException($"Build failed: {report.summary.result}");
            Debug.Log($"Build succeeded: {report.summary.outputPath} ({report.summary.totalSize / (1024 * 1024)} MB)");
        }
    }

    public sealed class BuildFailedException : System.Exception
    {
        public BuildFailedException(string message) : base(message) { }
    }
}
