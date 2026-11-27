using System.IO;
using Fomoxa.Unity;
using Fomoxa.Unity.Rapier;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FomoxaChecks.Editor
{
    public static class RoomsCheckBuild
    {
        private const string BootPath = RoomsCheck.BuiltFolder + "/RoomsBoot.unity";
        private const string PlayerPath = RoomsCheck.BuiltFolder + "/RoomsPlayer.prefab";
        private const string CratePath = RoomsCheck.BuiltFolder + "/RoomsCrate.prefab";
        private const string Location = "Builds/Rooms/Rooms.exe";

        public static void BuildAll()
        {
            Directory.CreateDirectory(RoomsCheck.BuiltFolder);
            CreateLobby();
            CreateRoom();
            CreatePlayer();
            CreateCrate();
            CreateBoot();
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BootPath, true),
                new EditorBuildSettingsScene(RoomsCheck.LobbyPath, true),
                new EditorBuildSettingsScene(RoomsCheck.RoomPath, true),
            };
            PlayerSettings.runInBackground = true;
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { BootPath, RoomsCheck.LobbyPath, RoomsCheck.RoomPath },
                locationPathName = Location,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"RoomsCheck build failed: {report.summary.result}");
                EditorApplication.Exit(1);
            }
        }

        private static void AddPhysics(GameObject network, NetworkManager manager)
        {
            var physics = network.AddComponent<RapierPhysics>();
            var managerSettings = new SerializedObject(manager);
            managerSettings.FindProperty("physics").objectReferenceValue = physics;
            managerSettings.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CreateLobby()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Block("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(30f, 1f, 30f));
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), RoomsCheck.LobbyPath);
        }

        private static void CreateRoom()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            float size = RoomsLayout.HalfSize * 2f;
            Block("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(size, 1f, size));
            Block("North", new Vector3(0f, 1f, RoomsLayout.HalfSize + 0.5f), new Vector3(size + 2f, 2f, 1f));
            Block("South", new Vector3(0f, 1f, -RoomsLayout.HalfSize - 0.5f), new Vector3(size + 2f, 2f, 1f));
            Block("East", new Vector3(RoomsLayout.HalfSize + 0.5f, 1f, 0f), new Vector3(1f, 2f, size));
            Block("West", new Vector3(-RoomsLayout.HalfSize - 0.5f, 1f, 0f), new Vector3(1f, 2f, size));
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), RoomsCheck.RoomPath);
        }

        private static void Block(string name, Vector3 position, Vector3 size)
        {
            var block = new GameObject(name);
            block.transform.position = position;
            block.AddComponent<BoxCollider>().size = size;
        }

        private static void CreatePlayer()
        {
            GameObject player = Body("RoomsPlayer");
            player.GetComponent<Rigidbody>().constraints = RigidbodyConstraints.FreezeRotation;
            player.AddComponent<RoomsPlayer>();
            Save(player, PlayerPath);
        }

        private static void CreateCrate()
        {
            GameObject crate = Body("RoomsCrate");
            crate.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
            Save(crate, CratePath);
        }

        private static GameObject Body(string name)
        {
            var body = new GameObject(name);
            body.AddComponent<BoxCollider>();
            body.AddComponent<Rigidbody>();
            body.AddComponent<NetworkObject>();
            body.AddComponent<NetworkTransform>();
            body.AddComponent<RoomsContacts>();
            return body;
        }

        private static void Save(GameObject source, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(source, path);
            Object.DestroyImmediate(source);
        }

        private static void CreateBoot()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var network = new GameObject("Network");
            var transport = network.AddComponent<UdpNetworkTransport>();
            var manager = network.AddComponent<NetworkManager>();
            var managerSettings = new SerializedObject(manager);
            managerSettings.FindProperty("transport").objectReferenceValue = transport;
            managerSettings.FindProperty("tickRate").intValue = 60;
            managerSettings.ApplyModifiedPropertiesWithoutUndo();
            AddPhysics(network, manager);
            var check = network.AddComponent<RoomsCheck>();
            var checkSettings = new SerializedObject(check);
            checkSettings.FindProperty("manager").objectReferenceValue = manager;
            checkSettings.FindProperty("playerPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<NetworkObject>(PlayerPath);
            checkSettings.FindProperty("cratePrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<NetworkObject>(CratePath);
            checkSettings.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(network.scene, BootPath);
        }
    }
}
