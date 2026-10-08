using System;
using System.Collections.Generic;
using System.Reflection;
using BundleFixture;
using Fomoxa.Networking;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Simulation;
using Fomoxa.Unity.Tests.Support;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity.Rapier.Tests
{
    public sealed class RapierPhysicsTest
    {
        private const double FrameSeconds = 1.0 / 60;
        private const uint BallPrefabId = 0xBA11;
        private const uint GroundPrefabId = 0x6400;
        private const uint ArenaSceneId = 0x77;

        private readonly List<GameObject> created = new List<GameObject>();
        private readonly List<NetworkManager> managers = new List<NetworkManager>();
        private readonly List<MonoBehaviour> enabled = new List<MonoBehaviour>();
        private InMemoryNetworkTransport network;
        private TimeSpan now;

        [SetUp]
        public void CreateNetwork()
        {
            now = TimeSpan.FromSeconds(1);
            network = new GameObject("InMemoryNetwork").AddComponent<InMemoryNetworkTransport>();
            created.Add(network.gameObject);
        }

        [TearDown]
        public void DestroyManagers()
        {
            foreach (MonoBehaviour behaviour in enabled)
            {
                if (behaviour != null)
                {
                    Disable(behaviour);
                }
            }

            enabled.Clear();
            foreach (NetworkManager manager in managers)
            {
                manager.ClientManager.StopConnection();
                manager.ServerManager.StopConnection();
                manager.ReleasePhysicsSimulation();
            }

            foreach (GameObject gameObject in created)
            {
                if (gameObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            }

            managers.Clear();
            created.Clear();
        }

        [Test]
        public void TheServerSimulatesSpawnedObjectsWithRapierAndMovesTheirTransforms()
        {
            NetworkObject ballPrefab = Prefab("Ball", BallPrefabId, ball => { ball.AddComponent<SphereCollider>().radius = 0.5f; ball.AddComponent<Rigidbody>().mass = 2f; });
            NetworkObject groundPrefab = Prefab("Ground", GroundPrefabId, ground => ground.AddComponent<BoxCollider>().size = new Vector3(20f, 1f, 20f));
            NetworkManager server = Manager(ballPrefab, groundPrefab);
            server.ServerManager.StartConnection(1);
            NetworkObject ground = Spawn(server, groundPrefab, new Vector3(0f, -0.5f, 0f));
            NetworkObject ball = Spawn(server, ballPrefab, new Vector3(0f, 4f, 0f));

            RunFrames(180);

            Assert.AreEqual(0.5f, ball.transform.position.y, 0.05f);
            Assert.IsTrue(ball.GetComponent<Rigidbody>().isKinematic);
            Assert.IsTrue(ball.Body.IsValid);
            Assert.AreEqual(2f, ball.Body.Mass, 1e-4f);
            Assert.AreEqual(ball.transform.position.y, ball.Body.Position.Y, 1e-5f);
            Assert.IsTrue(ground.Body.IsValid);

            server.ReleasePhysicsSimulation();

            Assert.IsFalse(ball.GetComponent<Rigidbody>().isKinematic);
        }

        [Test]
        public void TwoDimensionalObjectsFallOntoTwoDimensionalGround()
        {
            NetworkObject ballPrefab = Prefab("Ball2D", BallPrefabId, ball => { ball.AddComponent<CircleCollider2D>().radius = 0.5f; ball.AddComponent<Rigidbody2D>(); });
            NetworkObject groundPrefab = Prefab("Ground2D", GroundPrefabId, ground => ground.AddComponent<BoxCollider2D>().size = new Vector2(20f, 1f));
            NetworkManager server = Manager(ballPrefab, groundPrefab);
            server.ServerManager.StartConnection(1);
            Spawn(server, groundPrefab, new Vector3(0f, -0.5f, 0f));
            NetworkObject ball = Spawn(server, ballPrefab, new Vector3(0f, 4f, 3f));

            RunFrames(180);

            Assert.AreEqual(0.5f, ball.transform.position.y, 0.05f);
            Assert.AreEqual(3f, ball.transform.position.z, 1e-5f);
            Assert.AreEqual(RigidbodyType2D.Kinematic, ball.GetComponent<Rigidbody2D>().bodyType);
            Assert.IsTrue(ball.Body2D.IsValid);
            Assert.IsFalse(ball.Body.IsValid);
        }

        [Test]
        public void TheStaticGeometryOfANetworkSceneComesFromItsSceneFile()
        {
            Static("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f), false);
            NetworkObject ballPrefab = Prefab("Ball", BallPrefabId, ball => ball.AddComponent<SphereCollider>().radius = 0.5f);
            ballPrefab.gameObject.AddComponent<Rigidbody>();
            NetworkManager server = Manager(ballPrefab);
            var physics = server.GetComponent<RapierPhysics>();
            UseActiveScene(server);
            server.ServerManager.StartConnection(1);
            NetworkObject ball = Spawn(server, ballPrefab, new Vector3(0f, 4f, 0f));

            RunFrames(180);

            CollectionAssert.AreEqual(new[] { ArenaSceneId }, physics.Scenes.SceneIds);
            Assert.AreEqual(0.5f, ball.transform.position.y, 0.05f);
        }

        [Test]
        public void AClientKeepsTheObjectsItDoesNotPredictAsKinematicProxies()
        {
            NetworkObject ballPrefab = Prefab("Ball", BallPrefabId, ball => { ball.AddComponent<SphereCollider>().radius = 0.5f; ball.AddComponent<Rigidbody>(); });
            NetworkManager server = Manager(ballPrefab);
            NetworkManager client = Manager(ballPrefab);
            server.ServerManager.StartConnection(1);
            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(30);
            NetworkObject ball = Spawn(server, ballPrefab, new Vector3(0f, 4f, 0f));
            RunFrames(30);

            var onClient = (NetworkObject)client.ClientManager.Spawned[ball.ObjectId];
            Vector3 placed = onClient.transform.position;
            RunFrames(30);

            Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
            Assert.IsTrue(onClient.Body.IsValid);
            Assert.IsTrue(onClient.Body.IsKinematic);
            Assert.AreEqual(placed, onClient.transform.position);
            Assert.AreEqual(onClient.transform.position.y, onClient.Body.Position.Y, 1e-5f);
        }

        [Test]
        public void ACollisionOnABallReportsTheGroundOfTheSceneFile()
        {
            Static("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f), false);
            NetworkObject ballPrefab = Prefab("Ball", BallPrefabId, ball =>
            {
                ball.AddComponent<SphereCollider>().radius = 0.5f;
                ball.AddComponent<Rigidbody>();
                ball.AddComponent<NetworkCollision>();
            });
            NetworkManager server = Manager(ballPrefab);
            UseActiveScene(server);
            server.ServerManager.StartConnection(1);
            NetworkObject ball = Spawn(server, ballPrefab, new Vector3(0f, 3f, 0f));
            var collision = Enable(ball.GetComponent<NetworkCollision>());
            var events = new List<string>();
            collision.OnEnter += other => events.Add("enter " + other.name);
            collision.OnExit += other => events.Add("exit " + other.name);

            RunFrames(120);

            Assert.IsTrue(collision.IsAttached);
            CollectionAssert.AreEqual(new[] { "enter Ground" }, events);
            CollectionAssert.AreEqual(new[] { "Ground" }, Names(collision.Touching));
        }

        [Test]
        public void AStaticTriggerSeesABallPassThroughUntilThePhysicsIsReleased()
        {
            GameObject zone = Static("Zone", Vector3.zero, new Vector3(4f, 1f, 4f), true);
            var trigger = Enable(zone.AddComponent<NetworkTrigger>());
            var events = new List<string>();
            trigger.OnEnter += other => events.Add("enter " + other.name);
            trigger.OnExit += other => events.Add("exit " + other.name);
            NetworkObject ballPrefab = Prefab("Ball", BallPrefabId, ball =>
            {
                ball.AddComponent<SphereCollider>().radius = 0.25f;
                ball.AddComponent<Rigidbody>();
            });
            NetworkManager server = Manager(ballPrefab);
            UseActiveScene(server);
            server.ServerManager.StartConnection(1);
            Spawn(server, ballPrefab, new Vector3(0f, 2f, 0f));

            RunFrames(90);

            Assert.IsTrue(trigger.IsAttached);
            CollectionAssert.AreEqual(new[] { "enter Ball(Clone)", "exit Ball(Clone)" }, events);

            server.ReleasePhysicsSimulation();

            Assert.IsFalse(trigger.IsAttached);
        }

        [Test]
        public void ABlockAddedAtRuntimeHoldsABallAndItsTriggerSeesTheBallUntilTheBlockIsRemoved()
        {
            NetworkObject ballPrefab = Prefab("Ball2D", BallPrefabId, ball =>
            {
                ball.AddComponent<CircleCollider2D>().radius = 0.25f;
                ball.AddComponent<Rigidbody2D>();
            });
            NetworkManager server = Manager(ballPrefab);
            UseActiveScene(server);
            server.ServerManager.StartConnection(1);
            var physics = server.GetComponent<RapierPhysics>();
            var block = new GameObject("Block");
            created.Add(block);
            var floor = new GameObject("Floor");
            floor.transform.SetParent(block.transform, false);
            floor.transform.localPosition = new Vector3(0f, -0.5f, 0f);
            floor.AddComponent<BoxCollider2D>().size = new Vector2(10f, 1f);
            var zone = new GameObject("Zone");
            zone.transform.SetParent(block.transform, false);
            zone.transform.localPosition = new Vector3(0f, 2f, 0f);
            var zoneBox = zone.AddComponent<BoxCollider2D>();
            zoneBox.size = Vector2.one;
            zoneBox.isTrigger = true;
            var trigger = Enable(zone.AddComponent<NetworkTrigger2D>());
            var events = new List<string>();
            trigger.OnEnter += other => events.Add("enter " + other.name);
            trigger.OnExit += other => events.Add("exit " + other.name);

            StaticGroup group = physics.AddStatic2D(block);
            NetworkObject ball = Spawn(server, ballPrefab, new Vector3(0f, 4f, 0f));
            RunFrames(180);

            Assert.AreEqual((ArenaSceneId, 0, 2), (group.SceneId, group.FirstIndex, group.Count));
            Assert.AreEqual(0.25f, ball.transform.position.y, 0.05f);
            Assert.IsTrue(trigger.IsAttached);
            CollectionAssert.AreEqual(new[] { "enter Ball2D(Clone)", "exit Ball2D(Clone)" }, events);

            physics.RemoveStatic(group);
            RunFrames(60);

            Assert.IsFalse(trigger.IsAttached);
            Assert.Less(ball.transform.position.y, -1f);
        }

        [Test]
        public void UnownedBodiesAndGroupsNeedAPhysicsThatHasBegun()
        {
            var holder = new GameObject("Unbegun");
            created.Add(holder);
            var physics = holder.AddComponent<RapierPhysics>();

            Assert.Throws<InvalidOperationException>(() => physics.AddStatic2D(SceneManager.GetActiveScene(), Array.Empty<ColliderDesc2D>()));
            Assert.Throws<InvalidOperationException>(() => physics.AddBody2D(SceneManager.GetActiveScene(), new BodyDesc2D(BodyKind.Kinematic, BodyShape2D.Circle(1f), System.Numerics.Vector2.Zero, 0f, 0f)));
            Assert.DoesNotThrow(() => physics.RemoveStatic(default));
        }

        [Test]
        public void AClientProxyWithATriggerTouchesTheStaticGeometryOfItsOwnWorld()
        {
            Static("Wall", new Vector3(0f, 4f, 0f), new Vector3(2f, 2f, 2f), false);
            NetworkObject probePrefab = Prefab("Probe", BallPrefabId, probe =>
            {
                var sphere = probe.AddComponent<SphereCollider>();
                sphere.radius = 0.5f;
                sphere.isTrigger = true;
                probe.AddComponent<Rigidbody>().isKinematic = true;
                probe.AddComponent<NetworkTrigger>();
            });
            NetworkManager server = Manager(probePrefab);
            NetworkManager client = Manager(probePrefab);
            UseActiveScene(server);
            UseActiveScene(client);
            server.ServerManager.StartConnection(1);
            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(30);
            NetworkObject probe = Spawn(server, probePrefab, new Vector3(0f, 4f, 0f));
            RunFrames(30);
            var onClient = (NetworkObject)client.ClientManager.Spawned[probe.ObjectId];
            var trigger = Enable(onClient.GetComponent<NetworkTrigger>());

            RunFrames(10);

            Assert.IsTrue(onClient.Body.IsKinematic);
            Assert.IsTrue(trigger.IsAttached);
            CollectionAssert.AreEqual(new[] { "Wall" }, Names(trigger.Touching));
        }

        private GameObject Static(string name, Vector3 position, Vector3 size, bool isTrigger)
        {
            var gameObject = new GameObject(name);
            created.Add(gameObject);
            gameObject.transform.position = position;
            var box = gameObject.AddComponent<BoxCollider>();
            box.size = size;
            box.isTrigger = isTrigger;
            return gameObject;
        }

        private static void UseActiveScene(NetworkManager manager)
        {
            Scene active = SceneManager.GetActiveScene();
            var sources = new List<Collider>();
            var colliders = new List<ColliderDesc>();
            BodyDescriptions.StaticColliders(active, sources);
            foreach (Collider source in sources)
            {
                BodyDescriptions.DescribeStatic(source, null, colliders);
            }

            var file = new SceneFile { SceneId = ArenaSceneId };
            foreach (ColliderDesc collider in colliders)
            {
                file.Colliders.Add(SceneFileGeometry.ToFile(collider));
            }

            for (int layer = 0; layer < 32; layer++)
            {
                file.LayerCollisions.Add(uint.MaxValue);
            }

            byte[] bytes = SceneFileFormat.Write(TestObjects.Registry(), file);
            var physics = manager.GetComponent<RapierPhysics>();
            physics.SceneIdOf = scene => scene == active ? ArenaSceneId : 0;
            physics.SceneFileOf = sceneId => sceneId == ArenaSceneId ? bytes : null;
        }

        private static List<string> Names(IEnumerable<Collider> colliders)
        {
            var names = new List<string>();
            foreach (Collider collider in colliders)
            {
                names.Add(collider.name);
            }

            return names;
        }

        private T Enable<T>(T behaviour)
            where T : MonoBehaviour
        {
            enabled.Add(behaviour);
            behaviour.GetType().GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(behaviour, null);
            return behaviour;
        }

        private static void Disable(MonoBehaviour behaviour) =>
            behaviour.GetType().GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(behaviour, null);

        private NetworkObject Prefab(string name, uint prefabId, Action<GameObject> build)
        {
            var gameObject = new GameObject(name);
            created.Add(gameObject);
            var networkObject = gameObject.AddComponent<NetworkObject>();
            var serialized = new SerializedObject(networkObject);
            serialized.FindProperty("prefabId").uintValue = prefabId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            build(gameObject);
            return networkObject;
        }

        private NetworkManager Manager(params NetworkObject[] prefabs)
        {
            var gameObject = new GameObject("NetworkManager");
            created.Add(gameObject);
            var manager = gameObject.AddComponent<NetworkManager>();
            var physics = gameObject.AddComponent<RapierPhysics>();
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("transport").objectReferenceValue = network;
            serialized.FindProperty("physics").objectReferenceValue = physics;
            serialized.FindProperty("tickRate").intValue = 60;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            manager.Registry = TestObjects.Registry();
            manager.Initialize();
            manager.FindServerSceneObjects = () => new List<NetworkObject>();
            manager.FindClientSceneObjects = () => new List<NetworkObject>();
            foreach (NetworkObject prefab in prefabs)
            {
                manager.Prefabs.Register(prefab);
            }

            managers.Add(manager);
            return manager;
        }

        private NetworkObject Spawn(NetworkManager server, NetworkObject prefab, Vector3 position)
        {
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
            created.Add(instance.gameObject);
            server.ServerManager.Spawn(instance);
            return instance;
        }

        private void RunFrames(int count)
        {
            for (int frame = 0; frame < count; frame++)
            {
                now += TimeSpan.FromSeconds(FrameSeconds);
                foreach (NetworkManager manager in managers)
                {
                    manager.RunFrameStart(FrameSeconds, now);
                }

                foreach (NetworkManager manager in managers)
                {
                    manager.RunFrameEnd();
                }
            }
        }
    }
}
