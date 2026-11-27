using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using BundleFixture;
using Fomoxa.Networking;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Rapier;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Simulation;
using Fomoxa.Unity.Tests.Support;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Fomoxa.Unity.Rapier.Tests
{
    public sealed class RapierPhysicsTest
    {
        private const double FrameSeconds = 1.0 / 60;
        private const uint BallPrefabId = 0xBA11;
        private const uint GroundPrefabId = 0x6400;
        private const uint ScaledBallPrefabId = 0xBA12;
        private const uint RagdollPrefabId = 0xBA13;
        private const uint ChainPrefabId = 0xBA14;
        private const uint ArenaSceneId = 0x77;
        private const uint RoomSceneId = 0x78;
        private const uint SideSceneId = 0x79;
        private const string RoomPath = "Assets/FomoxaRapierRoom{0}.unity";
        private const string SidePath = "Assets/FomoxaRapierSide.unity";

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
            foreach (string path in new[] { string.Format(RoomPath, 0), string.Format(RoomPath, 1), SidePath })
            {
                Scene scene = SceneManager.GetSceneByPath(path);
                if (scene.IsValid())
                {
                    EditorSceneManager.CloseScene(scene, true);
                }

                if (System.IO.File.Exists(path))
                {
                    AssetDatabase.DeleteAsset(path);
                }
            }
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
            Assert.AreEqual(ball.transform.position.y, ball.Body.Position.y, 1e-5f);
            Assert.IsTrue(ground.Body.IsValid);

            server.ReleasePhysicsSimulation();

            Assert.IsFalse(ball.GetComponent<Rigidbody>().isKinematic);
        }

        [Test]
        public void ARestingBallIsReadOnlyWhileRapierMovesItUnlessItSyncsScale()
        {
            NetworkObject ballPrefab = Prefab("Ball", BallPrefabId, ball => { ball.AddComponent<SphereCollider>().radius = 0.5f; ball.AddComponent<Rigidbody>(); ball.AddComponent<NetworkTransform>(); });
            NetworkObject scaledPrefab = Prefab("ScaledBall", ScaledBallPrefabId, ball =>
            {
                ball.AddComponent<SphereCollider>().radius = 0.5f;
                ball.AddComponent<Rigidbody>();
                var scaled = new SerializedObject(ball.AddComponent<NetworkTransform>());
                scaled.FindProperty("syncScale").boolValue = true;
                scaled.ApplyModifiedPropertiesWithoutUndo();
            });
            NetworkObject groundPrefab = Prefab("Ground", GroundPrefabId, ground => ground.AddComponent<BoxCollider>().size = new Vector3(20f, 1f, 20f));
            NetworkManager server = Manager(ballPrefab, scaledPrefab, groundPrefab);
            NetworkManager client = Manager(ballPrefab, scaledPrefab, groundPrefab);
            server.ServerManager.StartConnection(1);
            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(30);
            Spawn(server, groundPrefab, new Vector3(0f, -0.5f, 0f));
            NetworkObject ball = Spawn(server, ballPrefab, new Vector3(0f, 3f, 0f));
            NetworkObject scaledBall = Spawn(server, scaledPrefab, new Vector3(4f, 3f, 0f));
            RunFrames(5);
            NetworkTransform onClient = ((NetworkObject)client.ClientManager.Spawned[ball.ObjectId]).GetComponent<NetworkTransform>();
            uint lastTickAfterSpawn = onClient.LastTick;
            RunFrames(15);
            bool fallingReadEveryTick = ReadEveryTick(ball);
            uint lastTickWhileFalling = onClient.LastTick;

            RunFrames(600);
            bool restingReadEveryTick = ReadEveryTick(ball);
            float restingGap = Vector3.Distance(ball.transform.position, onClient.transform.position);
            uint lastTickAtRest = onClient.LastTick;
            RunFrames(30);
            uint lastTickStillAtRest = onClient.LastTick;
            NetworkBody body = ball.Body;
            body.Velocity = new Vector3(3f, 0f, 0f);
            RunFrames(5);

            Assert.IsFalse(fallingReadEveryTick);
            Assert.AreEqual(lastTickAfterSpawn + 15, lastTickWhileFalling);
            Assert.IsFalse(restingReadEveryTick);
            Assert.IsTrue(ReadEveryTick(scaledBall));
            Assert.Less(restingGap, 1e-4f);
            Assert.AreEqual(lastTickAtRest, lastTickStillAtRest);
            Assert.Greater(ball.transform.position.x, 0.1f);
            Assert.Greater(onClient.LastTick, lastTickStillAtRest + 2);
        }

        [Test]
        public void ARestingBallIsSentWhenItsParentMoves()
        {
            NetworkObject ballPrefab = Prefab("Ball", BallPrefabId, ball => { ball.AddComponent<SphereCollider>().radius = 0.5f; ball.AddComponent<Rigidbody>(); ball.AddComponent<NetworkTransform>(); });
            NetworkObject groundPrefab = Prefab("Ground", GroundPrefabId, ground => ground.AddComponent<BoxCollider>().size = new Vector3(20f, 1f, 20f));
            NetworkManager server = Manager(ballPrefab, groundPrefab);
            NetworkManager client = Manager(ballPrefab, groundPrefab);
            server.ServerManager.StartConnection(1);
            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(30);
            Spawn(server, groundPrefab, new Vector3(0f, -0.5f, 0f));
            var holder = new GameObject("Holder");
            created.Add(holder);
            NetworkObject ball = UnityEngine.Object.Instantiate(ballPrefab, new Vector3(0f, 3f, 0f), Quaternion.identity, holder.transform);
            created.Add(ball.gameObject);
            server.ServerManager.Spawn(ball);
            RunFrames(600);
            NetworkTransform onClient = ((NetworkObject)client.ClientManager.Spawned[ball.ObjectId]).GetComponent<NetworkTransform>();
            bool restingReadEveryTick = ReadEveryTick(ball);
            uint lastTickAtRest = onClient.LastTick;
            RunFrames(30);
            uint lastTickStillAtRest = onClient.LastTick;

            holder.transform.position += new Vector3(2f, 0f, 0f);
            RunFrames(5);
            float localUnderMovedParent = ball.transform.localPosition.x;
            float clientUnderMovedParent = onClient.transform.localPosition.x;
            uint lastTickUnderMovedParent = onClient.LastTick;
            ball.transform.SetParent(null, true);
            RunFrames(5);

            Assert.IsFalse(restingReadEveryTick);
            Assert.AreEqual(lastTickAtRest, lastTickStillAtRest);
            Assert.Greater(lastTickUnderMovedParent, lastTickStillAtRest);
            Assert.AreEqual(-2f, localUnderMovedParent, 0.05f);
            Assert.AreEqual(localUnderMovedParent, clientUnderMovedParent, 1e-4f);
            Assert.Greater(onClient.LastTick, lastTickUnderMovedParent);
            Assert.AreEqual(0f, ball.transform.localPosition.x, 0.05f);
            Assert.AreEqual(ball.transform.localPosition.x, onClient.transform.localPosition.x, 1e-4f);
        }

        [Test]
        public void ARestingTwoDimensionalBallIsSentWhenItsZChanges()
        {
            NetworkObject ballPrefab = Prefab("Ball2D", BallPrefabId, ball => { ball.AddComponent<CircleCollider2D>().radius = 0.5f; ball.AddComponent<Rigidbody2D>(); ball.AddComponent<NetworkTransform>(); });
            NetworkObject groundPrefab = Prefab("Ground2D", GroundPrefabId, ground => ground.AddComponent<BoxCollider2D>().size = new Vector2(20f, 1f));
            NetworkManager server = Manager(ballPrefab, groundPrefab);
            NetworkManager client = Manager(ballPrefab, groundPrefab);
            server.ServerManager.StartConnection(1);
            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(30);
            Spawn(server, groundPrefab, new Vector3(0f, -0.5f, 0f));
            NetworkObject ball = Spawn(server, ballPrefab, new Vector3(0f, 3f, 0f));
            RunFrames(600);
            NetworkTransform onClient = ((NetworkObject)client.ClientManager.Spawned[ball.ObjectId]).GetComponent<NetworkTransform>();
            bool restingReadEveryTick = ReadEveryTick(ball);
            uint lastTickAtRest = onClient.LastTick;
            RunFrames(30);
            uint lastTickStillAtRest = onClient.LastTick;

            Vector3 resting = ball.transform.position;
            ball.transform.position = new Vector3(resting.x, resting.y, 4f);
            RunFrames(5);

            Assert.IsFalse(restingReadEveryTick);
            Assert.AreEqual(lastTickAtRest, lastTickStillAtRest);
            Assert.AreEqual(4f, ball.transform.position.z);
            Assert.AreEqual(4f, onClient.transform.position.z, 1e-5f);
        }

        [Test]
        public void AZChangedInATickHandlerIsSentInThatTick()
        {
            NetworkObject ballPrefab = Prefab("Ball2D", BallPrefabId, ball => { ball.AddComponent<CircleCollider2D>().radius = 0.5f; ball.AddComponent<Rigidbody2D>(); ball.AddComponent<NetworkTransform>(); });
            NetworkObject groundPrefab = Prefab("Ground2D", GroundPrefabId, ground => ground.AddComponent<BoxCollider2D>().size = new Vector2(20f, 1f));
            NetworkManager server = Manager(ballPrefab, groundPrefab);
            NetworkManager client = Manager(ballPrefab, groundPrefab);
            server.ServerManager.StartConnection(1);
            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(30);
            Spawn(server, groundPrefab, new Vector3(0f, -0.5f, 0f));
            NetworkObject ball = Spawn(server, ballPrefab, new Vector3(0f, 3f, 0f));
            RunFrames(600);
            NetworkTransform onClient = ((NetworkObject)client.ClientManager.Spawned[ball.ObjectId]).GetComponent<NetworkTransform>();
            uint lastTickAtRest = onClient.LastTick;
            uint changedAt = 0;
            Action change = () =>
            {
                if (changedAt == 0)
                {
                    changedAt = server.TimeManager.Tick;
                    Vector3 resting = ball.transform.position;
                    ball.transform.position = new Vector3(resting.x, resting.y, 4f);
                }
            };
            server.TimeManager.OnTick += change;
            uint firstReceived = lastTickAtRest;
            for (int frame = 0; frame < 5 && firstReceived == lastTickAtRest; frame++)
            {
                RunFrames(1);
                firstReceived = onClient.LastTick;
            }

            server.TimeManager.OnTick -= change;
            RunFrames(5);

            Assert.AreNotEqual(0u, changedAt);
            Assert.AreEqual(changedAt, firstReceived);
            Assert.AreEqual(4f, onClient.transform.position.z, 1e-5f);
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
        public void EachInstanceOfOneSceneGetsItsOwnRapierWorldAndLeavesItAlone()
        {
            NetworkObject ballPrefab = Prefab("Ball", BallPrefabId, ball => ball.AddComponent<SphereCollider>().radius = 0.5f);
            ballPrefab.gameObject.AddComponent<Rigidbody>();
            NetworkManager server = Manager(ballPrefab);
            var physics = server.GetComponent<RapierPhysics>();
            var loader = new RoomLoader();
            server.Scenes.Register(RoomSceneId, loader);
            physics.SceneIdOf = scene => loader.Rooms.Contains(scene) ? RoomSceneId : 0;
            physics.SceneFileOf = sceneId => sceneId == RoomSceneId ? loader.File : null;
            server.ServerManager.StartConnection(1);

            NetworkScene red = server.ServerManager.Scenes.Load(RoomSceneId, Array.Empty<ulong>(), ScenePhysics.Own);
            NetworkScene blue = server.ServerManager.Scenes.Load(RoomSceneId, Array.Empty<ulong>(), ScenePhysics.Own);
            RunFrames(1);
            NetworkObject redBall = SpawnIn(server, ballPrefab, loader.Rooms[0], new Vector3(0f, 4f, 0f));
            NetworkObject blueBall = SpawnIn(server, ballPrefab, loader.Rooms[1], new Vector3(0f, 4f, 0f));
            RunFrames(180);

            CollectionAssert.AreEquivalent(new[] { red, blue }, physics.Scenes.Scenes);
            Assert.AreNotSame(physics.Scenes.WorldOf(red), physics.Scenes.WorldOf(blue));
            Assert.AreEqual(0f, redBall.transform.position.x, 1e-3f);
            Assert.AreEqual(0.5f, redBall.transform.position.y, 0.05f);
            Assert.AreEqual(0f, blueBall.transform.position.x, 1e-3f);
            Assert.AreEqual(0.5f, blueBall.transform.position.y, 0.05f);

            server.ServerManager.Scenes.Unload(red);
            RunFrames(60);

            CollectionAssert.AreEqual(new[] { blue }, physics.Scenes.Scenes);
            Assert.AreEqual(0.5f, blueBall.transform.position.y, 0.05f);
        }

        [Test]
        public void AnObjectMovedToAnotherInstanceIsRebuiltInItsWorldWithItsMotionAndItsPrefabJointsButNotItsCodeJoints()
        {
            NetworkObject chainPrefab = Prefab("Chain", ChainPrefabId, chain =>
            {
                chain.AddComponent<SphereCollider>().radius = 0.5f;
                Rigidbody root = chain.AddComponent<Rigidbody>();
                var link = Limb(chain.transform, "Link", new Vector3(1f, 0f, 0f)).AddComponent<HingeJoint>();
                link.connectedBody = root;
                link.anchor = new Vector3(-0.5f, 0f, 0f);
                link.axis = Vector3.forward;
            });
            NetworkManager server = Manager(chainPrefab);
            var physics = server.GetComponent<RapierPhysics>();
            var loader = new RoomLoader();
            server.Scenes.Register(RoomSceneId, loader);
            physics.SceneIdOf = scene => loader.Rooms.Contains(scene) ? RoomSceneId : 0;
            physics.SceneFileOf = sceneId => sceneId == RoomSceneId ? loader.File : null;
            server.ServerManager.StartConnection(1);
            NetworkScene red = server.ServerManager.Scenes.Load(RoomSceneId, Array.Empty<ulong>(), ScenePhysics.Own);
            NetworkScene blue = server.ServerManager.Scenes.Load(RoomSceneId, Array.Empty<ulong>(), ScenePhysics.Own);
            RunFrames(1);
            NetworkObject chained = SpawnIn(server, chainPrefab, loader.Rooms[0], new Vector3(0f, 4f, 0f));
            RunFrames(1);
            JointHandle rope = physics.Scenes.CreateJoint(chained, chained.Body, default, JointDesc.Rope(System.Numerics.Vector3.Zero, new System.Numerics.Vector3(0f, 5f, 0f), 1f));
            NetworkBody body = chained.Body;
            body.Velocity = new Vector3(1f, 0f, 0f);
            RunFrames(10);
            BodyHandle before = chained.Body.Handle;
            Vector3 position = chained.Body.Position;
            Vector3 velocity = chained.Body.Velocity;
            Assert.Greater(position.y, 3.9f);

            Assert.IsTrue(server.ServerManager.MoveToScene(chained, blue));

            Assert.AreEqual(loader.Rooms[1], chained.gameObject.scene);
            Assert.IsFalse(physics.Scenes.WorldOf(red).Contains(before));
            Assert.IsFalse(physics.Scenes.WorldOf(red).Contains(rope));
            Assert.IsTrue(physics.Scenes.WorldOf(blue).Contains(chained.Body.Handle));
            Assert.IsTrue(physics.Scenes.WorldOf(blue).Contains(chained.Bodies[1].Handle));
            Assert.Less(Vector3.Distance(position, chained.Body.Position), 1e-5f);
            Assert.Less(Vector3.Distance(velocity, chained.Body.Velocity), 1e-5f);

            RunFrames(180);

            Transform link = chained.transform.Find("Link");
            Assert.Less(chained.transform.position.y, 3f);
            Assert.Less(Vector3.Distance(link.TransformPoint(new Vector3(-0.5f, 0f, 0f)), chained.transform.TransformPoint(new Vector3(0.5f, 0f, 0f))), 0.02f);
        }

        [Test]
        public void AnOpenSceneStaysInTheDefaultWorldWhileAnInstanceOfItsSceneLoads()
        {
            NetworkObject ballPrefab = Prefab("Ball", BallPrefabId, ball => ball.AddComponent<SphereCollider>().radius = 0.5f);
            ballPrefab.gameObject.AddComponent<Rigidbody>();
            NetworkManager server = Manager(ballPrefab);
            var physics = server.GetComponent<RapierPhysics>();
            Scene active = SceneManager.GetActiveScene();
            Static("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f), false);
            byte[] file = ExportedFile(active, RoomSceneId);
            var loader = new RoomLoader { Deferred = true };
            server.Scenes.Register(RoomSceneId, loader);
            physics.SceneIdOf = scene => scene == active || loader.Rooms.Contains(scene) ? RoomSceneId : 0;
            physics.SceneFileOf = sceneId => sceneId == RoomSceneId ? file : null;
            var box = new[] { new ColliderDesc(NetworkShapes.Box(Vector3.one), System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, ColliderMaterial.Default, 0, false) };
            server.ServerManager.StartConnection(1);
            NetworkObject ball = Spawn(server, ballPrefab, new Vector3(0f, 2f, 0f));
            RunFrames(60);

            NetworkScene room = server.ServerManager.Scenes.Load(RoomSceneId, Array.Empty<ulong>(), ScenePhysics.Own);
            RunFrames(60);

            Assert.AreEqual(0.5f, ball.transform.position.y, 0.05f);
            CollectionAssert.AreEqual(new[] { default(NetworkScene) }, physics.Scenes.Scenes);
            Assert.Throws<InvalidOperationException>(() => physics.AddStatic(loader.Rooms[0], box));
            Assert.AreEqual(default(NetworkScene), physics.AddStatic(active, box).Scene);

            loader.Finish();
            RunFrames(1);

            CollectionAssert.AreEquivalent(new[] { default(NetworkScene), room }, physics.Scenes.Scenes);
            Assert.AreEqual(0.5f, ball.transform.position.y, 0.05f);
        }

        [Test]
        public void ScenesOutsideNetworkScenesShareTheDefaultWorldAndTakeOnlyTheirOwnGeometryAway()
        {
            NetworkObject ballPrefab = Prefab("Ball", BallPrefabId, ball => ball.AddComponent<SphereCollider>().radius = 0.5f);
            ballPrefab.gameObject.AddComponent<Rigidbody>();
            NetworkManager server = Manager(ballPrefab);
            var physics = server.GetComponent<RapierPhysics>();
            Scene active = SceneManager.GetActiveScene();
            Static("Left", new Vector3(-5f, -0.5f, 0f), new Vector3(4f, 1f, 4f), false);
            Scene side = OpenScratchScene(SidePath);
            SceneManager.MoveGameObjectToScene(Static("Right", new Vector3(5f, -0.5f, 0f), new Vector3(4f, 1f, 4f), false), side);
            byte[] activeFile = ExportedFile(active, ArenaSceneId);
            byte[] sideFile = ExportedFile(side, SideSceneId);
            physics.SceneIdOf = scene => scene == active ? ArenaSceneId : scene == side ? SideSceneId : 0;
            physics.SceneFileOf = sceneId => sceneId == ArenaSceneId ? activeFile : sceneId == SideSceneId ? sideFile : null;
            server.ServerManager.StartConnection(1);
            NetworkObject leftBall = Spawn(server, ballPrefab, new Vector3(-5f, 2f, 0f));
            NetworkObject rightBall = Spawn(server, ballPrefab, new Vector3(5f, 2f, 0f));

            RunFrames(120);

            CollectionAssert.AreEqual(new[] { default(NetworkScene) }, physics.Scenes.Scenes);
            Assert.AreEqual(0.5f, leftBall.transform.position.y, 0.05f);
            Assert.AreEqual(0.5f, rightBall.transform.position.y, 0.05f);

            EditorSceneManager.CloseScene(side, true);
            RunFrames(120);

            Assert.AreEqual(0.5f, leftBall.transform.position.y, 0.05f);
            Assert.Less(rightBall.transform.position.y, -1f);
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

            CollectionAssert.AreEqual(new[] { default(NetworkScene) }, physics.Scenes.Scenes);
            Assert.AreEqual(0.5f, ball.transform.position.y, 0.05f);
        }

        [Test]
        public void AChildRigidbodyIsABodyOfItsOwnThatRapierMovesWhileItsNetworkTransformIsFollowed()
        {
            NetworkObject ragdollPrefab = Prefab("Ragdoll", RagdollPrefabId, ragdoll =>
            {
                ragdoll.AddComponent<SphereCollider>().radius = 0.5f;
                ragdoll.AddComponent<Rigidbody>();
                ragdoll.AddComponent<NetworkTransform>();
                Limb(ragdoll.transform, "Limb", new Vector3(1.5f, 2f, 0f)).AddComponent<NetworkTransform>();
            });
            NetworkObject groundPrefab = Prefab("Ground", GroundPrefabId, ground => ground.AddComponent<BoxCollider>().size = new Vector3(20f, 1f, 20f));
            NetworkManager server = Manager(ragdollPrefab, groundPrefab);
            NetworkManager client = Manager(ragdollPrefab, groundPrefab);
            server.ServerManager.StartConnection(1);
            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(30);
            Spawn(server, groundPrefab, new Vector3(0f, -0.5f, 0f));
            NetworkObject ragdoll = Spawn(server, ragdollPrefab, new Vector3(0f, 3f, 0f));
            Transform limb = ragdoll.transform.Find("Limb");
            RunFrames(20);
            bool fallingReadEveryTick = ReadEveryTick(ragdoll);

            RunFrames(600);

            var onClient = (NetworkObject)client.ClientManager.Spawned[ragdoll.ObjectId];
            Assert.IsFalse(fallingReadEveryTick);
            Assert.IsFalse(ReadEveryTick(ragdoll));
            Assert.AreEqual(2, ragdoll.Bodies.Count);
            Assert.AreEqual(ragdoll.Body.Handle, ragdoll.Bodies[0].Handle);
            Assert.IsTrue(limb.GetComponent<Rigidbody>().isKinematic);
            Assert.AreEqual(0.5f, ragdoll.transform.position.y, 0.05f);
            Assert.AreEqual(0.25f, limb.position.y, 0.05f);
            Assert.AreEqual(limb.position.y, ragdoll.Bodies[1].Position.y, 1e-5f);
            Assert.Less(Vector3.Distance(limb.localPosition, onClient.transform.Find("Limb").localPosition), 1e-3f);
        }

        [Test]
        public void TheServerWritesAnOwnerPoseIntoTheRapierBodiesOfTheRootAndAChildAndHoldsThemKinematic()
        {
            NetworkObject ragdollPrefab = Prefab("Ragdoll", RagdollPrefabId, ragdoll =>
            {
                ragdoll.AddComponent<SphereCollider>().radius = 0.5f;
                ragdoll.AddComponent<Rigidbody>();
                LetTheOwnerDecide(ragdoll.AddComponent<NetworkTransform>());
                LetTheOwnerDecide(Limb(ragdoll.transform, "Limb", new Vector3(1.5f, 0f, 0f)).AddComponent<NetworkTransform>());
            });
            NetworkManager server = Manager(ragdollPrefab);
            NetworkManager client = Manager(ragdollPrefab);
            ulong ownerId = 0;
            server.ServerManager.OnRemoteConnectionState += args => ownerId = args.PeerId;
            server.ServerManager.StartConnection(1);
            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(30);
            NetworkObject ragdoll = UnityEngine.Object.Instantiate(ragdollPrefab, new Vector3(0f, 3f, 0f), Quaternion.identity);
            created.Add(ragdoll.gameObject);
            server.ServerManager.Spawn(ragdoll, ownerId);
            RunFrames(10);
            var onOwner = (NetworkObject)client.ClientManager.Spawned[ragdoll.ObjectId];
            Transform limb = ragdoll.transform.Find("Limb");
            onOwner.transform.position = new Vector3(5f, 3f, 0f);
            onOwner.transform.Find("Limb").localPosition = new Vector3(1.5f, 1f, 0f);
            RunFrames(30);

            Assert.IsTrue(ragdoll.Body.IsKinematic);
            Assert.IsTrue(ragdoll.Bodies[1].IsKinematic);
            Assert.Less(Vector3.Distance(new Vector3(5f, 3f, 0f), ragdoll.transform.position), 1e-4f);
            Assert.Less(Vector3.Distance(new Vector3(5f, 3f, 0f), ragdoll.Body.Position), 1e-4f);
            Assert.Less(Vector3.Distance(new Vector3(6.5f, 4f, 0f), limb.position), 1e-4f);
            Assert.Less(Vector3.Distance(new Vector3(6.5f, 4f, 0f), ragdoll.Bodies[1].Position), 1e-4f);

            ragdoll.GetComponent<NetworkTransform>().OwnerAuthority = false;
            limb.GetComponent<NetworkTransform>().OwnerAuthority = false;
            RunFrames(2);

            Assert.IsFalse(ragdoll.Body.IsKinematic);
            Assert.IsFalse(ragdoll.Bodies[1].IsKinematic);
        }

        [Test]
        public void BodyOfAPartIsTheRapierBodyOfThatGameObjectOnly()
        {
            NetworkObject ragdollPrefab = Prefab("Ragdoll", RagdollPrefabId, ragdoll =>
            {
                ragdoll.AddComponent<SphereCollider>().radius = 0.5f;
                ragdoll.AddComponent<Rigidbody>();
                Limb(ragdoll.transform, "Limb", new Vector3(1.5f, 0f, 0f));
                new GameObject("Bare").transform.SetParent(ragdoll.transform, false);
            });
            NetworkManager server = Manager(ragdollPrefab);
            server.ServerManager.StartConnection(1);
            NetworkObject ragdoll = Spawn(server, ragdollPrefab, new Vector3(0f, 3f, 0f));
            RunFrames(1);
            var physics = server.GetComponent<RapierPhysics>();

            PhysicsBody limb = physics.BodyOf(ragdoll.transform.Find("Limb"));

            Assert.AreEqual(ragdoll.Body.Handle, physics.BodyOf(ragdoll.transform).Handle);
            Assert.AreEqual(ragdoll.Bodies[1].Handle, limb.Handle);
            Assert.IsFalse(physics.BodyOf(ragdoll.transform.Find("Bare")).IsValid);
            Assert.IsFalse(physics.Body2DOf(ragdoll.transform).IsValid);
            Assert.IsFalse(physics.BodyOf((Transform)null).IsValid);
        }

        [Test]
        public void AClientProxiesTheRootAndTheChildBodiesWithANetworkTransformOnly()
        {
            NetworkObject ragdollPrefab = Prefab("Ragdoll", RagdollPrefabId, ragdoll =>
            {
                ragdoll.AddComponent<SphereCollider>().radius = 0.5f;
                ragdoll.AddComponent<Rigidbody>();
                Limb(ragdoll.transform, "Synced", new Vector3(2f, 0f, 0f)).AddComponent<NetworkTransform>();
                Limb(ragdoll.transform, "Free", new Vector3(-2f, 0f, 0f));
            });
            NetworkManager server = Manager(ragdollPrefab);
            NetworkManager client = Manager(ragdollPrefab);
            server.ServerManager.StartConnection(1);
            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(30);
            NetworkObject ragdoll = Spawn(server, ragdollPrefab, new Vector3(0f, 4f, 0f));
            RunFrames(30);

            var onClient = (NetworkObject)client.ClientManager.Spawned[ragdoll.ObjectId];
            IReadOnlyList<NetworkBody> bodies = onClient.Bodies;
            Assert.AreEqual(3, bodies.Count);
            Assert.IsTrue(bodies[0].IsKinematic);
            Assert.IsTrue(bodies[1].IsKinematic);
            Assert.IsFalse(bodies[2].IsKinematic);
            Assert.Less(onClient.transform.Find("Free").position.y, 3.5f);
        }

        [Test]
        public void JointsOfAPrefabAreBuiltAtSpawnAndJointsRapierCannotBuildAreReported()
        {
            var outside = new GameObject("Outside body");
            created.Add(outside);
            Rigidbody stranger = outside.AddComponent<Rigidbody>();
            NetworkObject chainPrefab = Prefab("Chain", ChainPrefabId, chain =>
            {
                chain.AddComponent<SphereCollider>().radius = 0.25f;
                Rigidbody root = chain.AddComponent<Rigidbody>();
                chain.AddComponent<HingeJoint>().axis = Vector3.forward;
                var link = Limb(chain.transform, "Link", new Vector3(1f, 0f, 0f)).AddComponent<HingeJoint>();
                link.connectedBody = root;
                link.anchor = new Vector3(-0.5f, 0f, 0f);
                link.axis = Vector3.forward;
                Limb(chain.transform, "Loose", new Vector3(-1f, 0f, 0f)).AddComponent<ConfigurableJoint>().connectedBody = root;
                Limb(chain.transform, "Outside", new Vector3(0f, 0f, 1f)).AddComponent<FixedJoint>().connectedBody = stranger;
                var breakable = Limb(chain.transform, "Breakable", new Vector3(0f, 0f, -1f)).AddComponent<FixedJoint>();
                breakable.connectedBody = root;
                breakable.breakForce = 50f;
            });
            NetworkManager server = Manager(chainPrefab);
            server.ServerManager.StartConnection(1);
            LogAssert.Expect(LogType.Error, new Regex("^ConfigurableJoint on Loose is not supported under Rapier"));
            LogAssert.Expect(LogType.Error, new Regex("^FixedJoint on Outside: the connected body is not a body of the same NetworkObject"));
            LogAssert.Expect(LogType.Error, new Regex("^FixedJoint on Breakable: Rapier joints do not break"));
            NetworkObject chained = Spawn(server, chainPrefab, new Vector3(0f, 5f, 0f));

            RunFrames(120);

            Transform link = chained.transform.Find("Link");
            Transform breakableLimb = chained.transform.Find("Breakable");
            Assert.Less(Vector3.Distance(new Vector3(0f, 5f, 0f), chained.transform.position), 0.01f);
            Assert.Less(Vector3.Distance(link.TransformPoint(new Vector3(-0.5f, 0f, 0f)), chained.transform.TransformPoint(new Vector3(0.5f, 0f, 0f))), 0.02f);
            Assert.Less(link.position.y, 4.9f);
            Assert.Less(Vector3.Distance(chained.transform.TransformPoint(new Vector3(0f, 0f, -1f)), breakableLimb.position), 0.02f);
            Assert.Less(chained.transform.Find("Loose").position.y, 0f);
        }

        [Test]
        public void AClientSimulatesAJointedLimbWithoutANetworkTransformAgainstItsProxyRoot()
        {
            NetworkObject chainPrefab = Prefab("Chain", ChainPrefabId, chain =>
            {
                chain.AddComponent<SphereCollider>().radius = 0.25f;
                Rigidbody root = chain.AddComponent<Rigidbody>();
                chain.AddComponent<NetworkTransform>();
                chain.AddComponent<HingeJoint>().axis = Vector3.forward;
                var link = Limb(chain.transform, "Link", new Vector3(1f, 0f, 0f)).AddComponent<HingeJoint>();
                link.connectedBody = root;
                link.anchor = new Vector3(-0.5f, 0f, 0f);
                link.axis = Vector3.forward;
            });
            NetworkManager server = Manager(chainPrefab);
            NetworkManager client = Manager(chainPrefab);
            server.ServerManager.StartConnection(1);
            client.ClientManager.StartConnection("unused.invalid", 1);
            RunFrames(30);
            NetworkObject chained = Spawn(server, chainPrefab, new Vector3(0f, 5f, 0f));

            RunFrames(120);

            var onClient = (NetworkObject)client.ClientManager.Spawned[chained.ObjectId];
            Transform link = onClient.transform.Find("Link");
            Assert.IsTrue(onClient.Body.IsKinematic);
            Assert.IsFalse(onClient.Bodies[1].IsKinematic);
            Assert.Less(Vector3.Distance(link.TransformPoint(new Vector3(-0.5f, 0f, 0f)), onClient.transform.TransformPoint(new Vector3(0.5f, 0f, 0f))), 0.02f);
            Assert.Less(link.position.y, 4.9f);
        }

        [Test]
        public void Rigidbody2DsThatAreNotSimulatedAtSpawnAreNotBodiesAndTheirJointsAreNotBuilt()
        {
            NetworkObject runnerPrefab = Prefab("Runner2D", RagdollPrefabId, runner =>
            {
                runner.AddComponent<CircleCollider2D>().radius = 0.5f;
                runner.AddComponent<Rigidbody2D>();
                Rigidbody2D body = Bone2D(runner.transform, "Body", new Vector2(0f, 0.5f));
                Rigidbody2D arm = Bone2D(runner.transform, "Arm", new Vector2(0.5f, 0.5f));
                arm.gameObject.AddComponent<HingeJoint2D>().connectedBody = body;
            });
            NetworkObject ghostPrefab = Prefab("Ghost2D", ChainPrefabId, ghost =>
            {
                ghost.AddComponent<CircleCollider2D>().radius = 0.5f;
                ghost.AddComponent<Rigidbody2D>().simulated = false;
            });
            NetworkObject groundPrefab = Prefab("Ground2D", GroundPrefabId, ground => ground.AddComponent<BoxCollider2D>().size = new Vector2(20f, 1f));
            NetworkManager server = Manager(runnerPrefab, ghostPrefab, groundPrefab);
            server.ServerManager.StartConnection(1);
            Spawn(server, groundPrefab, new Vector3(0f, -0.5f, 0f));
            NetworkObject runner = Spawn(server, runnerPrefab, new Vector3(0f, 3f, 0f));
            NetworkObject ghost = Spawn(server, ghostPrefab, new Vector3(4f, 3f, 0f));
            Transform arm = runner.transform.Find("Arm");

            RunFrames(180);

            Assert.AreEqual(1, runner.Bodies2D.Count);
            Assert.AreEqual(runner.Body2D.Handle, runner.Bodies2D[0].Handle);
            Assert.AreEqual(0.5f, runner.transform.position.y, 0.05f);
            Assert.AreEqual(new Vector3(0.5f, 0.5f, 0f), arm.localPosition);
            Assert.AreEqual(0, ghost.Bodies2D.Count);
            Assert.IsFalse(ghost.Body2D.IsValid);
            Assert.AreEqual(3f, ghost.transform.position.y, 1e-5f);
            LogAssert.NoUnexpectedReceived();
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
            Assert.AreEqual(onClient.transform.position.y, onClient.Body.Position.y, 1e-5f);
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
        public void ACollisionOnABallGivesTheContactPointsOfTheGround()
        {
            GameObject ground = Static("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(20f, 1f, 20f), false);
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

            RunFrames(120);

            var points = new List<NetworkContactPoint> { default, default };
            Assert.Greater(collision.GetContactPoints(ground.GetComponent<Collider>(), points), 0);
            foreach (NetworkContactPoint point in points)
            {
                Assert.AreEqual(0f, Vector3.Distance(Vector3.up, point.Normal), 1e-3f);
                Assert.AreEqual(0f, point.Point.y, 0.02f);
                Assert.AreEqual(0f, point.Depth, 0.02f);
            }

            Assert.AreEqual(0, collision.GetContactPoints(null, points));
            Assert.AreEqual(0, points.Count);
        }

        [Test]
        public void ACollision2DOnABallGivesTheContactPointsOfTheGround()
        {
            NetworkObject ballPrefab = Prefab("Ball2D", BallPrefabId, ball =>
            {
                ball.AddComponent<CircleCollider2D>().radius = 0.5f;
                ball.AddComponent<Rigidbody2D>();
                ball.AddComponent<NetworkCollision2D>();
            });
            NetworkObject groundPrefab = Prefab("Ground2D", GroundPrefabId, ground => ground.AddComponent<BoxCollider2D>().size = new Vector2(20f, 1f));
            NetworkManager server = Manager(ballPrefab, groundPrefab);
            server.ServerManager.StartConnection(1);
            NetworkObject ground = Spawn(server, groundPrefab, new Vector3(0f, -0.5f, 0f));
            NetworkObject ball = Spawn(server, ballPrefab, new Vector3(0f, 4f, 0f));
            var collision = Enable(ball.GetComponent<NetworkCollision2D>());

            RunFrames(180);

            var points = new List<NetworkContactPoint2D> { default, default };
            Assert.Greater(collision.GetContactPoints(ground.GetComponent<Collider2D>(), points), 0);
            foreach (NetworkContactPoint2D point in points)
            {
                Assert.AreEqual(0f, Vector2.Distance(Vector2.up, point.Normal), 1e-3f);
                Assert.AreEqual(0f, point.Point.y, 0.02f);
                Assert.AreEqual(0f, point.Depth, 0.02f);
            }
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

            Assert.AreEqual((default(NetworkScene), 0, 2), (group.Scene, group.FirstIndex, group.Count));
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

        [Test]
        public void TheServerMovesAKinematicCharacterThroughRapierScenesUntilAWallStopsIt()
        {
            Static("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f), false);
            Static("Wall", new Vector3(2f, 1f, 0f), new Vector3(0.2f, 2f, 2f), false);
            Static("Zone", new Vector3(1f, 1f, 0f), new Vector3(0.4f, 2f, 2f), true);
            NetworkObject characterPrefab = Prefab("Character", BallPrefabId, character =>
            {
                var capsule = character.AddComponent<CapsuleCollider>();
                capsule.radius = 0.3f;
                capsule.height = 1.6f;
                character.AddComponent<Rigidbody>().isKinematic = true;
            });
            NetworkManager server = Manager(characterPrefab);
            UseActiveScene(server);
            server.ServerManager.StartConnection(1);
            NetworkObject character = Spawn(server, characterPrefab, new Vector3(0f, 0.81f, 0f));
            RunFrames(2);
            RapierScenes scenes = server.GetComponent<RapierPhysics>().Scenes;

            NetworkCharacterMove last = default;
            for (int frame = 0; frame < 40; frame++)
            {
                last = scenes.MoveCharacter(character, character.Body, new Vector3(0.1f, -0.02f, 0f), (float)FrameSeconds, CharacterSettings.Default);
                RunFrames(1);
            }

            Assert.AreEqual(1.58f, character.transform.position.x, 0.05f);
            Assert.AreEqual(0.81f, character.transform.position.y, 0.02f);
            Assert.AreEqual(Quaternion.identity, character.transform.rotation);
            Assert.IsTrue(last.Grounded);
            Assert.AreEqual(0f, last.Moved.x, 1e-3f);
            Assert.IsTrue(character.GetComponent<Rigidbody>().isKinematic);
            CharacterMove core = scenes.MoveCharacter(character, character.Body.Core, new System.Numerics.Vector3(0.1f, -0.02f, 0f), (float)FrameSeconds, CharacterSettings.Default);
            NetworkCharacterMove filtered = scenes.MoveCharacter(character, character.Body, new Vector3(0.1f, -0.02f, 0f), (float)FrameSeconds, CharacterSettings.Default, QueryFilter.All);
            Assert.AreEqual(core.Moved.X, filtered.Moved.x, 1e-6f);
            Assert.AreEqual(core.Grounded, filtered.Grounded);
            Assert.AreEqual(core.Sliding, filtered.Sliding);
        }

        [Test]
        public void AJointBetweenAnObjectAndTheWorldIsCreatedThroughTheScenesWithItsNetworkBody()
        {
            NetworkObject doorPrefab = Prefab("Door", BallPrefabId, door => { door.AddComponent<BoxCollider>().size = new Vector3(1f, 2f, 0.1f); door.AddComponent<Rigidbody>().useGravity = false; });
            NetworkManager server = Manager(doorPrefab);
            UseActiveScene(server);
            server.ServerManager.StartConnection(1);
            NetworkObject door = Spawn(server, doorPrefab, new Vector3(0f, 1f, 0f));
            RunFrames(2);
            RapierScenes scenes = server.GetComponent<RapierPhysics>().Scenes;
            Quaternion vertical = Quaternion.AngleAxis(90f, Vector3.forward);
            JointDesc hinge = NetworkJoints.Create(JointKind.Revolute, new Vector3(-0.5f, 0f, 0f), vertical, new Vector3(-0.5f, 1f, 0f), vertical);

            JointHandle joint = scenes.CreateJoint(door, door.Body, default, hinge);

            Assert.IsTrue(joint.IsValid);
            Assert.Throws<ArgumentException>(() => scenes.CreateJoint(door, default(NetworkBody), default, hinge));
        }

        [Test]
        public void TheScenesMoveJoinAndDriveA2DObjectThroughItsNetworkBody2DLikeTheCoreCalls()
        {
            NetworkObject moverPrefab = Prefab("Mover2D", BallPrefabId, mover =>
            {
                var capsule = mover.AddComponent<CapsuleCollider2D>();
                capsule.size = new Vector2(0.6f, 1.6f);
                mover.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            });
            NetworkManager server = Manager(moverPrefab);
            UseActiveScene2D(server);
            server.ServerManager.StartConnection(1);
            NetworkObject mover = Spawn(server, moverPrefab, new Vector3(0f, 2f, 0f));
            RunFrames(2);
            RapierScenes scenes = server.GetComponent<RapierPhysics>().Scenes;

            CharacterMove2D core = scenes.MoveCharacter(mover, mover.Body2D.Core, new System.Numerics.Vector2(0.1f, 0f), (float)FrameSeconds, CharacterSettings2D.Default);
            NetworkCharacterMove2D move = scenes.MoveCharacter(mover, mover.Body2D, new Vector2(0.1f, 0f), (float)FrameSeconds, CharacterSettings2D.Default);
            NetworkCharacterMove2D filtered = scenes.MoveCharacter(mover, mover.Body2D, new Vector2(0.1f, 0f), (float)FrameSeconds, CharacterSettings2D.Default, QueryFilter.All);
            JointHandle joint = scenes.CreateJoint(mover, mover.Body2D, default, NetworkJoints.Create2D(JointKind.Revolute, Vector2.zero, 0f, new Vector2(0f, 2f), 0f));
            var firstCollider = new[] { 0 };
            EffectorDesc2D belt = EffectorDesc2D.Surface(2f, 0.5f, false, true, true).WithFrame(Vector2.zero, 0f);
            EffectorHandle effector = scenes.AddEffector(mover, mover.Body2D, firstCollider, belt);

            Assert.AreEqual(0.1f, move.Moved.x, 1e-3f);
            Assert.AreEqual(core.Moved.X, move.Moved.x, 1e-6f);
            Assert.AreEqual(core.Moved.Y, move.Moved.y, 1e-6f);
            Assert.AreEqual(core.Grounded, move.Grounded);
            Assert.AreEqual(core.Moved.X, filtered.Moved.x, 1e-6f);
            Assert.IsTrue(joint.IsValid);
            Assert.IsTrue(effector.IsValid);
            Assert.Throws<ArgumentException>(() => scenes.AddEffector(mover, mover.Body2D, firstCollider, belt));
            var unowned = Assert.Throws<ArgumentException>(() => scenes.AddEffector(mover.Body2D, firstCollider, belt));
            StringAssert.Contains("not an unowned 2D body", unowned.Message);
        }

        [Test]
        public void ACharacterControllerIsStillNotSupportedUnderRapier()
        {
            NetworkObject characterPrefab = Prefab("Controlled", BallPrefabId, character => character.AddComponent<CharacterController>());
            NetworkManager server = Manager(characterPrefab);
            server.ServerManager.StartConnection(1);
            LogAssert.Expect(LogType.Exception, new Regex("CharacterController on Controlled\\(Clone\\): CharacterController is not supported"));
            NetworkObject character = Spawn(server, characterPrefab, new Vector3(0f, 1f, 0f));

            RunFrames(2);

            Assert.IsFalse(character.Body.IsValid);
        }

        [Test]
        public void TheEffectorsOfTheSceneFileAndOfAPrefabPushWhatTheyTouch()
        {
            GameObject wind = Static2D("Wind", new Vector2(-5f, 2f), new Vector2(4f, 6f), true);
            var area = wind.AddComponent<AreaEffector2D>();
            area.forceMagnitude = 10f;
            area.forceAngle = 0f;
            area.useGlobalAngle = true;
            NetworkObject floatingPrefab = Prefab("Floating", BallPrefabId, ball =>
            {
                ball.AddComponent<CircleCollider2D>().radius = 0.5f;
                ball.AddComponent<Rigidbody2D>().gravityScale = 0f;
            });
            NetworkObject crate = Prefab("Crate", ScaledBallPrefabId, ball =>
            {
                ball.AddComponent<BoxCollider2D>().size = Vector2.one;
                ball.AddComponent<Rigidbody2D>();
            });
            NetworkObject beltPrefab = Prefab("Belt", GroundPrefabId, belt =>
            {
                var box = belt.AddComponent<BoxCollider2D>();
                box.size = new Vector2(20f, 1f);
                box.usedByEffector = true;
                var surface = belt.AddComponent<SurfaceEffector2D>();
                surface.speed = 3f;
                surface.speedVariation = 1f;
            });
            NetworkManager server = Manager(floatingPrefab, crate, beltPrefab);
            UseActiveScene2D(server);
            server.ServerManager.StartConnection(1);
            LogAssert.Expect(LogType.Warning, new Regex("SurfaceEffector2D on Belt\\(Clone\\): Rapier does not simulate speedVariation, so they count as 0"));
            Spawn(server, beltPrefab, new Vector3(5f, -0.5f, 0f));
            NetworkObject floating = Spawn(server, floatingPrefab, new Vector3(-5f, 2f, 0f));
            NetworkObject carried = Spawn(server, crate, new Vector3(5f, 0.5f, 0f));

            RunFrames(60);

            Assert.Greater(floating.transform.position.x, -3.5f);
            Assert.AreEqual(2f, floating.transform.position.y, 1e-3f);
            Assert.Greater(carried.transform.position.x, 6f);
            Assert.AreEqual(0.5f, carried.transform.position.y, 0.05f);
        }

        [Test]
        public void AnEffectorOnABlockAddedAtRuntimeDrivesWhatRestsOnItUntilTheBlockIsRemoved()
        {
            NetworkObject cratePrefab = Prefab("Crate", BallPrefabId, crate =>
            {
                crate.AddComponent<BoxCollider2D>().size = Vector2.one;
                crate.AddComponent<Rigidbody2D>();
            });
            NetworkManager server = Manager(cratePrefab);
            UseActiveScene(server);
            server.ServerManager.StartConnection(1);
            var physics = server.GetComponent<RapierPhysics>();
            var block = new GameObject("Block");
            created.Add(block);
            var belt = new GameObject("Belt");
            belt.transform.SetParent(block.transform, false);
            belt.transform.localPosition = new Vector3(0f, -0.5f, 0f);
            var box = belt.AddComponent<BoxCollider2D>();
            box.size = new Vector2(20f, 1f);
            box.usedByEffector = true;
            belt.AddComponent<SurfaceEffector2D>().speed = 3f;

            StaticGroup group = physics.AddStatic2D(block);
            NetworkObject crate = Spawn(server, cratePrefab, new Vector3(0f, 0.5f, 0f));
            RunFrames(60);

            Assert.Greater(crate.transform.position.x, 1f);
            Assert.AreEqual(0.5f, crate.transform.position.y, 0.05f);

            physics.RemoveStatic(group);
            RunFrames(60);

            Assert.Less(crate.transform.position.y, -1f);
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
            byte[] bytes = ExportedFile(active, ArenaSceneId);
            var physics = manager.GetComponent<RapierPhysics>();
            physics.SceneIdOf = scene => scene == active ? ArenaSceneId : 0;
            physics.SceneFileOf = sceneId => sceneId == ArenaSceneId ? bytes : null;
        }

        private GameObject Static2D(string name, Vector2 position, Vector2 size, bool isTrigger)
        {
            var gameObject = new GameObject(name);
            created.Add(gameObject);
            gameObject.transform.position = position;
            var box = gameObject.AddComponent<BoxCollider2D>();
            box.size = size;
            box.isTrigger = isTrigger;
            box.usedByEffector = true;
            return gameObject;
        }

        private static void UseActiveScene2D(NetworkManager manager)
        {
            Scene active = SceneManager.GetActiveScene();
            var sources = new List<Collider2D>();
            var colliders = new List<ColliderDesc2D>();
            var pieces = new List<Collider2D>();
            BodyDescriptions.StaticColliders2D(active, sources);
            for (int index = 0; index < sources.Count; index++)
            {
                if (index == 0 || sources[index] != sources[index - 1])
                {
                    BodyDescriptions.DescribeStatic2D(sources[index], null, colliders);
                    while (pieces.Count < colliders.Count)
                    {
                        pieces.Add(sources[index]);
                    }
                }
            }

            var file = new SceneFile { SceneId = ArenaSceneId };
            var effectors = new List<Effector2D>();
            var ignored = new List<string>();
            for (int index = 0; index < colliders.Count; index++)
            {
                SceneFileCollider2D collider = SceneFileGeometry.ToFile(colliders[index]);
                if (EffectorDescriptions.TryFind(pieces[index], out Effector2D effector))
                {
                    if (!effectors.Contains(effector))
                    {
                        effectors.Add(effector);
                        file.Effectors2D.Add(EffectorDescriptions.Describe(effector, null, ignored));
                    }

                    collider.Effector = (ushort)(effectors.IndexOf(effector) + 1);
                }

                file.Colliders2D.Add(collider);
            }

            for (int layer = 0; layer < 32; layer++)
            {
                file.LayerCollisions2D.Add(uint.MaxValue);
            }

            byte[] bytes = SceneFileFormat.Write(TestObjects.Registry(), file);
            var physics = manager.GetComponent<RapierPhysics>();
            physics.SceneIdOf = scene => scene == active ? ArenaSceneId : 0;
            physics.SceneFileOf = sceneId => sceneId == ArenaSceneId ? bytes : null;
        }

        private static void LetTheOwnerDecide(NetworkTransform networkTransform)
        {
            var serialized = new SerializedObject(networkTransform);
            serialized.FindProperty("ownerAuthority").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject Limb(Transform parent, string name, Vector3 localPosition)
        {
            var limb = new GameObject(name);
            limb.transform.SetParent(parent, false);
            limb.transform.localPosition = localPosition;
            limb.AddComponent<SphereCollider>().radius = 0.25f;
            limb.AddComponent<Rigidbody>();
            return limb;
        }

        private static Rigidbody2D Bone2D(Transform parent, string name, Vector2 localPosition)
        {
            var bone = new GameObject(name);
            bone.transform.SetParent(parent, false);
            bone.transform.localPosition = localPosition;
            bone.AddComponent<CircleCollider2D>().radius = 0.25f;
            var rigidbody = bone.AddComponent<Rigidbody2D>();
            rigidbody.simulated = false;
            return rigidbody;
        }

        private static bool ReadEveryTick(NetworkObject networkObject) =>
            typeof(EntityRecord).GetProperty("TransformOrderNode", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(((INetworkEntity)networkObject).Record) != null;

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

        private static byte[] ExportedFile(Scene scene, uint sceneId)
        {
            var sources = new List<Collider>();
            var colliders = new List<ColliderDesc>();
            BodyDescriptions.StaticColliders(scene, sources);
            foreach (Collider source in sources)
            {
                BodyDescriptions.DescribeStatic(source, null, colliders);
            }

            var file = new SceneFile { SceneId = sceneId };
            foreach (ColliderDesc collider in colliders)
            {
                file.Colliders.Add(SceneFileGeometry.ToFile(collider));
            }

            for (int layer = 0; layer < 32; layer++)
            {
                file.LayerCollisions.Add(uint.MaxValue);
            }

            return SceneFileFormat.Write(TestObjects.Registry(), file);
        }

        private static Scene OpenScratchScene(string path)
        {
            System.IO.File.WriteAllText(path, "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            AssetDatabase.ImportAsset(path);
            return EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        }

        private NetworkObject SpawnIn(NetworkManager server, NetworkObject prefab, Scene scene, Vector3 position)
        {
            NetworkObject instance = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
            SceneManager.MoveGameObjectToScene(instance.gameObject, scene);
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

        private sealed class RoomLoader : ISceneLoader
        {
            public readonly List<Scene> Rooms = new List<Scene>();
            public byte[] File;
            public bool Deferred;
            private Action finish;

            public void Finish() => finish();

            public void Load(SceneLoadRequest request, Action<float> progress, Action<Scene> loaded, Action<Exception> failed)
            {
                Scene room = OpenScratchScene(string.Format(RoomPath, Rooms.Count));
                var ground = new GameObject("Ground");
                ground.transform.position = new Vector3(0f, -0.5f, 0f);
                ground.AddComponent<BoxCollider>().size = new Vector3(20f, 1f, 20f);
                SceneManager.MoveGameObjectToScene(ground, room);
                File ??= ExportedFile(room, RoomSceneId);
                Rooms.Add(room);
                if (Deferred)
                {
                    finish = () => loaded(room);
                    return;
                }

                loaded(room);
            }

            public void Unload(Scene scene, Action unloaded)
            {
                EditorSceneManager.CloseScene(scene, true);
                unloaded();
            }
        }
    }
}
