using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using Fomoxa.Networking;
using Fomoxa.Networking.Rapier.TwoSided;
using Fomoxa.Networking.Sessions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace Fomoxa.Unity.Rapier.Tests
{
    public sealed class TwoSidedClientCheck
    {
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
        private NetworkManager manager;

        [TearDown]
        public void Destroy()
        {
            if (manager != null)
            {
                manager.ClientManager.StopConnection();
                manager.ReleasePhysicsSimulation();
            }

            foreach (UnityEngine.Object item in created)
            {
                if (item != null)
                {
                    UnityEngine.Object.DestroyImmediate(item);
                }
            }

            foreach (NetworkObject spawned in UnityEngine.Object.FindObjectsByType<NetworkObject>(FindObjectsSortMode.None))
            {
                UnityEngine.Object.DestroyImmediate(spawned.gameObject);
            }

            created.Clear();
            TwoSidedBall.Record = null;
            TwoSidedBall.WorldHash = null;
        }

        [Test]
        [Explicit("runs against a two-sided server started by Tools/two-sided-check.sh")]
        public void TheClientPredictsTheBallsOfATwoSidedServer()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FOMOXA_TWO_SIDED_DIR")))
            {
                Assert.Ignore("Tools/two-sided-check.sh runs this check against a two-sided server");
            }

            string host = Setting("FOMOXA_TWO_SIDED_HOST", "127.0.0.1");
            ushort port = ushort.Parse(Setting("FOMOXA_TWO_SIDED_PORT", "7790"), CultureInfo.InvariantCulture);
            string directory = Setting("FOMOXA_TWO_SIDED_DIR", ".");
            double seconds = double.Parse(Setting("FOMOXA_TWO_SIDED_SECONDS", "25"), CultureInfo.InvariantCulture);
            byte[] sceneFile = File.ReadAllBytes(Path.Combine(directory, TwoSidedScenario.SceneFileName));

            var record = new TwoSidedRecord();
            GameObject template = BallTemplate();
            manager = Manager(sceneFile);
            var physics = manager.GetComponent<RapierPhysics>();
            TwoSidedBall.Record = record;
            TwoSidedBall.WorldHash = () => physics.Scenes.WorldOf(TwoSidedScenario.ClientSceneId).StateHash;
            int instances = 0;
            manager.Prefabs.Register(TwoSidedScenario.BallPrefabId, TwoSidedScenario.BallFingerprint, (position, rotation) =>
            {
                GameObject instance = UnityEngine.Object.Instantiate(template, position, rotation);
                instance.GetComponent<TwoSidedBall>().Index = instances++;
                instance.SetActive(true);
                return instance.GetComponent<NetworkObject>();
            });

            manager.ClientManager.StartConnection(host, port);
            var clock = Stopwatch.StartNew();
            TimeSpan last = clock.Elapsed;
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                TimeSpan now = clock.Elapsed;
                manager.RunFrameStart((now - last).TotalSeconds, now);
                manager.RunFrameEnd();
                last = now;
                Thread.Sleep(1);
            }

            ConnectionState state = manager.ClientManager.State;
            int spawned = manager.ClientManager.Spawned.Count;
            string path = Path.Combine(directory, "client.log");
            record.Write(path);
            Debug.Log($"[two-sided] client state {state}, {spawned} spawned, {record.Hashes.Count} ticks, {record.Applies} applies, {record.Mismatches} mismatches, written to {path}");
            Assert.AreEqual(ConnectionState.Started, state);
            Assert.AreEqual(TwoSidedScenario.BallPositions.Length, spawned);
            Assert.Greater(record.Hashes.Count, 0);
        }

        private GameObject BallTemplate()
        {
            var template = new GameObject("TwoSidedBall");
            created.Add(template);
            var networkObject = template.AddComponent<NetworkObject>();
            var serialized = new SerializedObject(networkObject);
            serialized.FindProperty("prefabId").uintValue = TwoSidedScenario.BallPrefabId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var material = new PhysicsMaterial("TwoSidedBall")
            {
                dynamicFriction = TwoSidedScenario.BallMaterial.Friction,
                staticFriction = TwoSidedScenario.BallMaterial.Friction,
                bounciness = TwoSidedScenario.BallMaterial.Restitution,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine = PhysicsMaterialCombine.Average,
            };
            created.Add(material);
            var sphere = template.AddComponent<SphereCollider>();
            sphere.radius = 0.5f;
            sphere.sharedMaterial = material;
            var rigidbody = template.AddComponent<Rigidbody>();
            rigidbody.mass = 1f;
            rigidbody.useGravity = false;
            template.AddComponent<TwoSidedBall>();
            template.SetActive(false);
            return template;
        }

        private NetworkManager Manager(byte[] sceneFile)
        {
            var gameObject = new GameObject("TwoSidedClient");
            created.Add(gameObject);
            var transport = gameObject.AddComponent<UdpNetworkTransport>();
            var client = gameObject.AddComponent<NetworkManager>();
            var physics = gameObject.AddComponent<RapierPhysics>();
            var physicsSettings = new SerializedObject(physics);
            physicsSettings.FindProperty("gravity").vector3Value = Vector3.zero;
            physicsSettings.ApplyModifiedPropertiesWithoutUndo();
            var serialized = new SerializedObject(client);
            serialized.FindProperty("transport").objectReferenceValue = transport;
            serialized.FindProperty("physics").objectReferenceValue = physics;
            serialized.FindProperty("tickRate").intValue = TwoSidedScenario.TickRate;
            serialized.FindProperty("reconcileInterval").intValue = 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            client.Registry = BundleFixture.TestObjects.Registry();
            client.Initialize();
            client.FindServerSceneObjects = () => new List<NetworkObject>();
            client.FindClientSceneObjects = () => new List<NetworkObject>();
            Scene active = SceneManager.GetActiveScene();
            physics.SceneIdOf = scene => scene == active ? TwoSidedScenario.ClientSceneId : 0;
            physics.SceneFileOf = sceneId => sceneId == TwoSidedScenario.ClientSceneId ? sceneFile : null;
            return client;
        }

        private static string Setting(string name, string fallback)
        {
            string value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
    }

    public sealed class TwoSidedBall : NetworkBehaviour
    {
        public static TwoSidedRecord Record;
        public static Func<ulong> WorldHash;

        public int Index { get; set; }

        protected override void OnRegisterInput(NetworkInput input)
        {
            var motion = new TwoSidedMotion(() => NetworkObject.Body, Index, () => WorldHash(), Record);
            motion.Register(input);
        }
    }
}
