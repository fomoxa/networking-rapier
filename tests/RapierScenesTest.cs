using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Net.Transports;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Simulation;
using Fomoxa.Networking.Standalone;
using Fomoxa.Networking.Transports;
using NUnit.Framework;

namespace Fomoxa.Networking.Rapier.Tests
{
    public sealed class RapierScenesTest
    {
        private const uint ArenaSceneId = 0x00A1_E7A0;
        private const uint LobbySceneId = 0x00B0_B000;
        private const uint BallPrefabId = 0x0000_0B01;
        private const float Tick = 1f / 60f;

        [Test]
        public void ASceneFileBuildsTheStaticGeometryOfTheWorldOfItsScene()
        {
            using (var scenes = new RapierScenes())
            {
                scenes.LoadScene(Arena(ArenaSceneId));
                PhysicsBody ball = scenes.AddBody(Entity(), ArenaSceneId, Ball(new Vector3(0f, 5f, 0f)));

                Run(scenes, 300);

                CollectionAssert.AreEqual(new[] { ArenaSceneId }, scenes.SceneIds);
                Assert.AreEqual(0.5f, ball.Position.Y, 0.05f);
                Assert.AreEqual(PhysicsBackend.Rapier, scenes.Backend);
            }
        }

        [Test]
        public void BodiesOfDifferentScenesLiveInDifferentWorlds()
        {
            using (var scenes = new RapierScenes())
            {
                scenes.LoadScene(Arena(ArenaSceneId));
                StandaloneEntity inArena = Entity();
                StandaloneEntity inLobby = Entity();
                PhysicsBody resting = scenes.AddBody(inArena, ArenaSceneId, Ball(new Vector3(0f, 2f, 0f)));
                PhysicsBody falling = scenes.AddBody(inLobby, LobbySceneId, Ball(new Vector3(0f, 2f, 0f)));
                var worlds = new List<IPhysicsSimulation>();

                Run(scenes, 120);
                scenes.WorldsOf(inLobby, worlds);

                Assert.AreEqual(0.5f, resting.Position.Y, 0.05f);
                Assert.Less(falling.Position.Y, -1f);
                CollectionAssert.AreEqual(new IPhysicsSimulation[] { scenes.WorldOf(LobbySceneId) }, worlds);
                CollectionAssert.AreEqual(new[] { ArenaSceneId, LobbySceneId }, scenes.SceneIds);
            }
        }

        [Test]
        public void UnloadingASceneDisposesItsWorldAndRemovingAnEntityRemovesItsBodies()
        {
            using (var scenes = new RapierScenes())
            {
                scenes.LoadScene(Arena(ArenaSceneId));
                StandaloneEntity left = Entity();
                PhysicsBody first = scenes.AddBody(left, ArenaSceneId, Ball(Vector3.Zero));
                PhysicsBody second = scenes.AddBody(left, ArenaSceneId, Ball(Vector3.UnitX * 3f));
                PhysicsBody other = scenes.AddBody(Entity(), ArenaSceneId, Ball(Vector3.UnitX * 6f));

                scenes.RemoveBodies(left);
                bool otherBeforeUnload = other.IsValid;
                scenes.UnloadScene(ArenaSceneId);
                var stepping = new List<IPhysicsSimulation>();
                scenes.WorldsToStep(stepping);

                Assert.IsFalse(first.IsValid);
                Assert.IsFalse(second.IsValid);
                Assert.IsTrue(otherBeforeUnload);
                Assert.IsFalse(other.IsValid);
                Assert.IsEmpty(stepping);
                CollectionAssert.IsEmpty(scenes.SceneIds);
            }
        }

        [Test]
        public void AColliderTouchingTheGroundLeadsBackToItsEntityUntilItsBodiesAreRemoved()
        {
            using (var scenes = new RapierScenes())
            {
                scenes.LoadScene(Arena(ArenaSceneId));
                StandaloneEntity entity = Entity();
                scenes.AddBody(entity, ArenaSceneId, Ball(new Vector3(0f, 1f, 0f)));
                RapierWorld world = scenes.WorldOf(ArenaSceneId);
                Run(scenes, 60);
                var touching = new List<RapierCollider>();

                world.Touching(RapierCollider.Static(0), touching);

                Assert.AreEqual(1, touching.Count);
                Assert.AreSame(entity, scenes.EntityOf(world, touching[0].Body));
                Assert.IsNull(scenes.EntityOf(null, touching[0].Body));

                scenes.RemoveBodies(entity);

                Assert.IsNull(scenes.EntityOf(world, touching[0].Body));
            }
        }

        [Test]
        public void AProxyIsKinematicAndKeepsItsStateWhenTheWorldIsLoaded()
        {
            using (var scenes = new RapierScenes())
            {
                StandaloneEntity entity = Entity();
                PhysicsBody body = scenes.AddBody(entity, ArenaSceneId, Ball(Vector3.Zero));
                RapierWorld world = scenes.WorldOf(ArenaSceneId);
                PhysicsHistory history = scenes.HistoryOf(world, 4);

                scenes.PlaceProxy(entity);
                PhysicsSnapshot snapshot = world.CreateSnapshot();
                world.Save(snapshot);
                body.Position = new Vector3(4f, 0f, 0f);
                world.Load(snapshot);
                bool proxyKinematic = body.IsKinematic;
                scenes.EndProxy(entity);

                Assert.IsTrue(proxyKinematic);
                Assert.AreEqual(4f, body.Position.X, 1e-5f);
                Assert.IsFalse(body.IsKinematic);
                Assert.AreSame(history, scenes.HistoryOf(world, 4));
            }
        }

        [Test]
        public void BrokenGeometryAsksForANewExport()
        {
            using (var scenes = new RapierScenes())
            {
                var broken = new SceneFile { SceneId = LobbySceneId };
                Vector3[] point = { Vector3.One, Vector3.One, Vector3.One, Vector3.One };
                broken.Colliders.Add(SceneFileGeometry.ToFile(new ColliderDesc(BodyShape.ConvexHull(point), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 0, false)));

                InvalidDataException exception = Assert.Throws<InvalidDataException>(() => scenes.LoadScene(broken));
                StringAssert.Contains("export the scene again", exception.Message);
            }
        }

        [Test]
        public void AConsoleServerSimulatesTheSceneAndItsClientFollowsWithAProxy()
        {
            var files = new SceneFiles();
            files.Add(Arena(ArenaSceneId));
            var prefabs = new StandalonePrefabs();
            prefabs.Register(BallPrefabId, 0xBA11, () => new BallEntity());
            var logged = new List<Exception>();
            var log = new NetworkLog(logged.Add, message => { });
            var settings = new NetworkSettings { PhysicsBackend = PhysicsBackend.Rapier };
            var factory = new LoopbackFactory();
            TimeSpan now = TimeSpan.Zero;
            using (var serverPhysics = new RapierScenes())
            using (var clientPhysics = new RapierScenes())
            {
                NetworkRuntime server = StandaloneRuntime.Create(TestObjects.Registry(), settings, factory, prefabs, new StandaloneBehaviours(), files, () => now, log, serverPhysics);
                NetworkRuntime client = StandaloneRuntime.Create(TestObjects.Registry(), settings, factory, prefabs, new StandaloneBehaviours(), files, () => now, log, clientPhysics);
                void Frames(int count)
                {
                    for (int frame = 0; frame < count; frame++)
                    {
                        now += TimeSpan.FromSeconds(Tick);
                        server.BeginFrame(now, Tick);
                        client.BeginFrame(now, Tick);
                        server.EndFrame();
                        client.EndFrame();
                    }
                }

                server.ServerManager.StartConnection(7777);
                client.ClientManager.StartConnection("127.0.0.1", 7777);
                Frames(10);
                server.ServerManager.Scenes.LoadGlobal(new[] { ArenaSceneId });
                Frames(10);
                var ball = new BallEntity { SceneId = ArenaSceneId, Position = new Vector3(0f, 5f, 0f) };
                server.ServerManager.Spawn(ball);
                Frames(180);

                var onClient = (StandaloneEntity)client.ClientManager.Spawned[ball.Record.ObjectId];
                Assert.AreEqual(ConnectionState.Started, client.ClientManager.State);
                Assert.IsEmpty(logged);
                Assert.AreEqual(0.5f, ball.Position.Y, 0.05f);
                Assert.IsTrue(onClient.Body.IsValid);
                Assert.IsTrue(onClient.Body.IsKinematic);
                CollectionAssert.AreEqual(new[] { ArenaSceneId }, clientPhysics.SceneIds);
            }
        }

        [Test]
        public void ATwoDimensionalSceneHasItsOwnWorldSteppedAfterTheThreeDimensionalOne()
        {
            using (var scenes = new RapierScenes())
            {
                scenes.LoadScene(Flatland(ArenaSceneId));
                StandaloneEntity entity = Entity();
                PhysicsBody2D crate = scenes.AddBody2D(entity, ArenaSceneId, new BodyDesc2D(BodyKind.Dynamic, BodyShape2D.Box(new Vector2(0.5f)), new Vector2(0f, 4f), 0f, 1f));
                var worlds = new List<IPhysicsSimulation>();
                var stepping = new List<IPhysicsSimulation>();

                Run(scenes, 300);
                scenes.WorldsOf(entity, worlds);
                scenes.WorldsToStep(stepping);

                Assert.AreEqual(0.5f, crate.Position.Y, 0.05f);
                CollectionAssert.AreEqual(new IPhysicsSimulation[] { scenes.WorldOf2D(ArenaSceneId) }, worlds);
                CollectionAssert.AreEqual(new IPhysicsSimulation[] { scenes.WorldOf2D(ArenaSceneId) }, stepping);
                CollectionAssert.IsEmpty(scenes.SceneIds);
                CollectionAssert.AreEqual(new[] { ArenaSceneId }, scenes.SceneIds2D);
            }
        }

        [Test]
        public void AWorldCreatedAfterTheSceneLoadedGetsTheLayersOfItsSceneFile()
        {
            using (var scenes = new RapierScenes())
            {
                SceneFile file = Arena(ArenaSceneId);
                for (int layer = 0; layer < 32; layer++)
                {
                    file.LayerCollisions2D.Add(layer == 2 ? ~(1u << 3) : layer == 3 ? ~(1u << 2) : uint.MaxValue);
                }

                scenes.LoadScene(file);
                bool twoDimensionalBeforeBodies = scenes.SceneIds2D.GetEnumerator().MoveNext();
                PhysicsBody2D floor = scenes.AddBody2D(Entity(), ArenaSceneId, new BodyDesc2D(BodyKind.Static, new[] { new ColliderDesc2D(BodyShape2D.Box(new Vector2(10f, 0.5f)), Vector2.Zero, 0f, ColliderMaterial.Default, 2, false) }, new Vector2(0f, -0.5f), 0f, 0f));
                PhysicsBody2D ghost = scenes.AddBody2D(Entity(), ArenaSceneId, new BodyDesc2D(BodyKind.Dynamic, new[] { new ColliderDesc2D(BodyShape2D.Circle(0.5f), Vector2.Zero, 0f, ColliderMaterial.Default, 3, false) }, new Vector2(0f, 2f), 0f, 1f));

                Run(scenes, 120);

                Assert.IsFalse(twoDimensionalBeforeBodies);
                Assert.IsTrue(floor.IsValid);
                Assert.Less(ghost.Position.Y, -1f);
            }
        }

        [Test]
        public void AConsoleServerSimulatesATwoDimensionalSceneObjectFromTheSceneFile()
        {
            SceneFile flatland = Flatland(ArenaSceneId);
            flatland.Objects.Add(new SceneFileObject
            {
                SceneObjectId = 21,
                Fingerprint = 0xC0,
                Pose = new SceneFilePose { PositionY = 4f, RotationW = 1f, ScaleX = 1f, ScaleY = 1f, ScaleZ = 1f },
                Body2D = SceneFileGeometry.ToFile(new BodyDesc2D(BodyKind.Dynamic, BodyShape2D.Circle(0.5f), new Vector2(0f, 4f), 0f, 1f)),
            });
            var files = new SceneFiles();
            files.Add(flatland);
            var logged = new List<Exception>();
            var log = new NetworkLog(logged.Add, message => { });
            var settings = new NetworkSettings { PhysicsBackend = PhysicsBackend.Rapier };
            var factory = new LoopbackFactory();
            TimeSpan now = TimeSpan.Zero;
            using (var serverPhysics = new RapierScenes())
            using (var clientPhysics = new RapierScenes())
            {
                NetworkRuntime server = StandaloneRuntime.Create(TestObjects.Registry(), settings, factory, new StandalonePrefabs(), new StandaloneBehaviours(), files, () => now, log, serverPhysics);
                NetworkRuntime client = StandaloneRuntime.Create(TestObjects.Registry(), settings, factory, new StandalonePrefabs(), new StandaloneBehaviours(), files, () => now, log, clientPhysics);
                void Frames(int count)
                {
                    for (int frame = 0; frame < count; frame++)
                    {
                        now += TimeSpan.FromSeconds(Tick);
                        server.BeginFrame(now, Tick);
                        client.BeginFrame(now, Tick);
                        server.EndFrame();
                        client.EndFrame();
                    }
                }

                server.ServerManager.StartConnection(7777);
                client.ClientManager.StartConnection("127.0.0.1", 7777);
                Frames(10);
                server.ServerManager.Scenes.LoadGlobal(new[] { ArenaSceneId });
                Frames(240);

                StandaloneEntity crate = null;
                foreach (INetworkEntity entity in server.ServerManager.Spawned.Values)
                {
                    crate = (StandaloneEntity)entity;
                }

                Assert.IsEmpty(logged);
                Assert.IsNotNull(crate);
                Assert.IsTrue(crate.Body2D.IsValid);
                Assert.AreEqual(0.5f, crate.Position.Y, 0.05f);
                CollectionAssert.AreEqual(new[] { ArenaSceneId }, clientPhysics.SceneIds2D);
            }
        }

        [Test]
        public void StaticGroupsContinueTheStaticIndicesOfTheSceneFileAndCanBeRemoved()
        {
            using (var scenes = new RapierScenes())
            {
                scenes.LoadScene(Flatland(ArenaSceneId));
                StaticGroup ledge = scenes.AddStatic2D(ArenaSceneId, new[] { new ColliderDesc2D(BodyShape2D.Box(new Vector2(1f, 0.5f)), new Vector2(20f, 2f), 0f, ColliderMaterial.Default, 0, false) });
                StaticGroup floor = scenes.AddStatic(LobbySceneId, new[] { new ColliderDesc(BodyShape.Box(new Vector3(10f, 0.5f, 10f)), new Vector3(0f, -0.5f, 0f), Quaternion.Identity, ColliderMaterial.Default, 0, false) });
                PhysicsBody2D crate = scenes.AddBody2D(ArenaSceneId, new BodyDesc2D(BodyKind.Dynamic, BodyShape2D.Box(new Vector2(0.5f, 0.5f)), new Vector2(20f, 5f), 0f, 1f));

                Run(scenes, 120);

                Assert.AreEqual((ArenaSceneId, 1, 1), (ledge.SceneId, ledge.FirstIndex, ledge.Count));
                Assert.AreEqual((LobbySceneId, 0, 1), (floor.SceneId, floor.FirstIndex, floor.Count));
                Assert.AreNotEqual(ledge.Id, floor.Id);
                Assert.AreEqual(3f, crate.Position.Y, 0.05f);
                var touching = new List<RapierCollider>();
                scenes.WorldOf2D(ArenaSceneId).Touching(RapierCollider.Static(ledge.FirstIndex), touching);
                Assert.AreEqual(1, touching.Count);

                scenes.RemoveStatic(ledge);
                Run(scenes, 120);

                Assert.Less(crate.Position.Y, 1f);
            }
        }

        [Test]
        public void UnownedBodiesBelongToNoEntityAndLeaveWithTheirScene()
        {
            using (var scenes = new RapierScenes())
            {
                scenes.LoadScene(Flatland(ArenaSceneId));
                StaticGroup ledge = scenes.AddStatic2D(ArenaSceneId, new[] { new ColliderDesc2D(BodyShape2D.Box(Vector2.One), new Vector2(5f, 5f), 0f, ColliderMaterial.Default, 0, false) });
                PhysicsBody2D lift = scenes.AddBody2D(ArenaSceneId, new BodyDesc2D(BodyKind.Kinematic, BodyShape2D.Box(Vector2.One), new Vector2(-5f, 1f), 0f, 0f));
                PhysicsBody2D crate = scenes.AddBody2D(ArenaSceneId, new BodyDesc2D(BodyKind.Dynamic, BodyShape2D.Box(new Vector2(0.5f, 0.5f)), new Vector2(-8f, 1f), 0f, 1f));
                PhysicsBody ball = scenes.AddBody(LobbySceneId, Ball(Vector3.Zero));
                StandaloneEntity entity = Entity();
                scenes.AddBody(entity, ArenaSceneId, Ball(Vector3.Zero));
                var worlds = new List<IPhysicsSimulation>();
                var stepping = new List<IPhysicsSimulation>();

                scenes.WorldsOf(entity, worlds);
                scenes.WorldsToStep(stepping);
                lift.Position = new Vector2(-6f, 1f);
                scenes.RemoveBody2D(lift);
                scenes.RemoveBody2D(lift);

                CollectionAssert.AreEqual(new IPhysicsSimulation[] { scenes.WorldOf(ArenaSceneId) }, worlds);
                CollectionAssert.Contains(stepping, scenes.WorldOf(LobbySceneId));
                Assert.IsFalse(lift.IsValid);
                Assert.IsTrue(crate.IsValid);
                Assert.IsTrue(ball.IsValid);

                scenes.UnloadScene(ArenaSceneId);

                Assert.IsFalse(crate.IsValid);
                Assert.DoesNotThrow(() => scenes.RemoveStatic(ledge));
                Assert.DoesNotThrow(() => scenes.RemoveBody2D(crate));
                Assert.IsTrue(ball.IsValid);
            }
        }

        private static SceneFile Flatland(uint sceneId)
        {
            var file = new SceneFile { SceneId = sceneId };
            file.Colliders2D.Add(SceneFileGeometry.ToFile(new ColliderDesc2D(BodyShape2D.Polyline(new[] { new Vector2(-10f, 0f), new Vector2(10f, 0f) }), Vector2.Zero, 0f, ColliderMaterial.Default, 0, false)));
            for (int layer = 0; layer < 32; layer++)
            {
                file.LayerCollisions.Add(uint.MaxValue);
                file.LayerCollisions2D.Add(uint.MaxValue);
            }

            return file;
        }

        private static SceneFile Arena(uint sceneId)
        {
            var file = new SceneFile { SceneId = sceneId };
            file.Colliders.Add(SceneFileGeometry.ToFile(new ColliderDesc(BodyShape.Box(new Vector3(10f, 0.5f, 10f)), new Vector3(0f, -0.5f, 0f), Quaternion.Identity, ColliderMaterial.Default, 0, false)));
            for (int layer = 0; layer < 32; layer++)
            {
                file.LayerCollisions.Add(uint.MaxValue);
            }

            return file;
        }

        private static BodyDesc Ball(Vector3 position) =>
            new BodyDesc(BodyKind.Dynamic, BodyShape.Sphere(0.5f), position, Quaternion.Identity, 1f);

        private static StandaloneEntity Entity() => new StandaloneEntity(1, Array.Empty<EntityBehaviour>());

        private static void Run(RapierScenes scenes, int ticks)
        {
            var worlds = new List<IPhysicsSimulation>();
            for (int tick = 0; tick < ticks; tick++)
            {
                worlds.Clear();
                scenes.WorldsToStep(worlds);
                foreach (IPhysicsSimulation world in worlds)
                {
                    world.Step(Tick);
                }
            }
        }

        private sealed class BallEntity : StandaloneEntity
        {
            public BallEntity()
                : base(BallPrefabId, Array.Empty<EntityBehaviour>())
            {
            }

            public override bool TryGetBody(out BodyDesc body)
            {
                body = Ball(Vector3.Zero);
                return true;
            }
        }

        private sealed class SceneFiles : ISceneFiles
        {
            private readonly Dictionary<uint, byte[]> scenes = new Dictionary<uint, byte[]>();

            public void Add(SceneFile file) => scenes[file.SceneId] = SceneFileFormat.Write(TestObjects.Registry(), file);

            public bool Knows(uint sceneId) => scenes.ContainsKey(sceneId);

            public byte[] Read(uint sceneId) => scenes[sceneId];
        }

        private sealed class LoopbackFactory : ITransportFactory
        {
            private readonly LoopbackListener listener = new LoopbackListener(256);

            public int FrameBudget => FomoxaWire.MaxDataFrameSize;

            public IListenerTransport CreateListener(ushort port, out ushort boundPort)
            {
                boundPort = port;
                return listener;
            }

            public ITransportConnector CreateConnector(string address, ushort port) => new Connector(listener);
        }

        private sealed class Connector : ITransportConnector
        {
            private readonly LoopbackListener listener;

            public Connector(LoopbackListener listener)
            {
                this.listener = listener;
            }

            public ConnectStatus Poll(out ITransport transport)
            {
                transport = listener.Connect();
                return ConnectStatus.Connected;
            }

            public void Dispose()
            {
            }
        }
    }
}
