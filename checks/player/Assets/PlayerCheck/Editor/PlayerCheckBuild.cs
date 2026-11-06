using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fomoxa.Rapier.PlayerCheck.Editor
{
    public static class PlayerCheckBuild
    {
        private const string GeneratedFolder = "Assets/Generated";
        private const string ScenePath = GeneratedFolder + "/PlayerCheck.unity";
        private const string GoldenPath = GeneratedFolder + "/Resources/golden.txt";
        private const string Product = "RapierPlayerCheck";
        private const string AndroidIdentifier = "com.fomoxa.rapier.playercheck";

        public static void Run()
        {
            try
            {
                string target = Argument("-checkTarget");
                PrepareAssets(Argument("-checkGolden"));
                PlayerSettings.companyName = "Fomoxa";
                PlayerSettings.productName = Product;
                BuildPlayerOptions options = Configure(target, Argument("-checkOutput"));
                options.scenes = new[] { ScenePath };
                BuildReport report = BuildPipeline.BuildPlayer(options);
                Debug.Log($"[player-check-build] {target} {report.summary.result} {report.summary.outputPath}");
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
            }
            catch (Exception failure)
            {
                Debug.LogException(failure);
                EditorApplication.Exit(1);
            }
        }

        private static void PrepareAssets(string goldenSource)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(GoldenPath));
            File.Copy(goldenSource, GoldenPath, true);
            AssetDatabase.ImportAsset(GoldenPath);
            EditorSceneManager.SaveScene(EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single), ScenePath);
        }

        private static BuildPlayerOptions Configure(string target, string output)
        {
            switch (target)
            {
                case "windows-mono":
                    return Windows(output, ScriptingImplementation.Mono2x, OSArchitecture.x64);
                case "windows-il2cpp":
                    return Windows(output, ScriptingImplementation.IL2CPP, OSArchitecture.x64);
                case "windows-arm64":
                    return Windows(output, ScriptingImplementation.Mono2x, OSArchitecture.ARM64);
                case "android-arm64":
                    return Android(output, AndroidArchitecture.ARM64);
                case "android-armv7":
                    return Android(output, AndroidArchitecture.ARMv7);
                case "webgl":
                    PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
                    return new BuildPlayerOptions { target = BuildTarget.WebGL, locationPathName = output };
                default:
                    throw new ArgumentException($"unknown check target {target}");
            }
        }

        private static BuildPlayerOptions Windows(string output, ScriptingImplementation backend, OSArchitecture architecture)
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, backend);
#if UNITY_STANDALONE_WIN
            UnityEditor.WindowsStandalone.UserBuildSettings.architecture = architecture;
#endif
            return new BuildPlayerOptions { target = BuildTarget.StandaloneWindows64, locationPathName = Path.Combine(output, Product + ".exe") };
        }

        private static BuildPlayerOptions Android(string output, AndroidArchitecture architecture)
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, AndroidIdentifier);
            PlayerSettings.Android.targetArchitectures = architecture;
            EditorUserBuildSettings.buildAppBundle = false;
            return new BuildPlayerOptions { target = BuildTarget.Android, locationPathName = Path.Combine(output, Product + ".apk") };
        }

        private static string Argument(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, name);
            if (index < 0 || index + 1 >= arguments.Length)
            {
                throw new ArgumentException($"the command line has no {name}");
            }

            return arguments[index + 1];
        }
    }
}
