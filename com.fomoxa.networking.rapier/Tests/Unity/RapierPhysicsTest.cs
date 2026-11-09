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
            PhysicsBody body = ball.Body;
            body.Velocity = new System.Numerics.Vector3(3f, 0f, 0f);
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
            Assert.AreEqual(limb.position.y, ragdoll.Bodies[1].Position.Y, 1e-5f);
            Assert.Less(Vector3.Distance(limb.localPosition, onClient.transform.Find("Limb").localPosition), 1e-3f);
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
            IReadOnlyList<PhysicsBody> bodies = onClient.Bodies;
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

            CharacterMove last = default;
            for (int frame = 0; frame < 40; frame++)
            {
                last = scenes.MoveCharacter(character, character.Body, new System.Numerics.Vector3(0.1f, -0.02f, 0f), (float)FrameSeconds, CharacterSettings.Default);
                RunFrames(1);
            }

            Assert.AreEqual(1.58f, character.transform.position.x, 0.05f);
            Assert.AreEqual(0.81f, character.transform.position.y, 0.02f);
            Assert.AreEqual(Quaternion.identity, character.transform.rotation);
            Assert.IsTrue(last.Grounded);
            Assert.AreEqual(0f, last.Moved.X, 1e-3f);
            Assert.IsTrue(character.GetComponent<Rigidbody>().isKinematic);
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

        private static GameObject Limb(Transform parent, string name, Vector3 localPosition)
        {
            var limb = new GameObject(name);
            limb.transform.SetParent(parent, false);
            limb.transform.localPosition = localPosition;
            limb.AddComponent<SphereCollider>().radius = 0.25f;
            limb.AddComponent<Rigidbody>();
            return limb;
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
