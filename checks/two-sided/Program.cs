using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Threading;
using BundleFixture;
using Fomoxa.Net;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Rapier.TwoSided;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Simulation;
using Fomoxa.Networking.Standalone;
using Fomoxa.Networking.Transports;

namespace Fomoxa.Networking.Rapier.TwoSidedServer
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            int port = int.Parse(Argument(args, "--port", "7790"), CultureInfo.InvariantCulture);
            string output = Argument(args, "--out", ".");
            double seconds = double.Parse(Argument(args, "--seconds", "300"), CultureInfo.InvariantCulture);
            Directory.CreateDirectory(output);

            FomoxaRegistry registry = TestObjects.Registry();
            File.WriteAllBytes(Path.Combine(output, TwoSidedScenario.SceneFileName), SceneFileFormat.Write(registry, TwoSidedScenario.Arena(TwoSidedScenario.ClientSceneId)));

            var record = new TwoSidedRecord();
            int wallTicks = 0;
            int meshTicks = 0;
            int ballContacts = 0;
            using (var physics = new RapierScenes(Vector3.Zero))
            {
                physics.LoadScene(TwoSidedScenario.Arena(0));
                RapierWorld world = physics.WorldOf(0);
                var prefabs = new StandalonePrefabs();
                prefabs.Register(TwoSidedScenario.BallPrefabId, TwoSidedScenario.BallFingerprint, () => BallEntity.Create(-1, () => world.StateHash, record));
                var settings = new NetworkSettings { TickRate = TwoSidedScenario.TickRate, PhysicsBackend = PhysicsBackend.Rapier, ReconcileInterval = 1 };
                var clock = Stopwatch.StartNew();
                var log = new NetworkLog(exception => Console.Error.WriteLine(exception), message => Console.WriteLine(message));
                NetworkRuntime server = StandaloneRuntime.Create(registry, settings, new UdpTransportFactory(), prefabs, new StandaloneBehaviours(), new SceneFileDirectory(output), () => clock.Elapsed, log, physics);
                ulong joined = 0;
                bool left = false;
                server.ServerManager.OnRemoteConnectionState += change =>
                {
                    if (change.State == ConnectionState.Started && joined == 0)
                    {
                        joined = change.PeerId;
                    }
                    else if (change.State == ConnectionState.Stopped && change.PeerId == joined)
                    {
                        left = true;
                    }
                };
                server.ServerManager.StartConnection((ushort)port);
                Console.WriteLine($"two-sided server listening on {port}, {Environment.OSVersion}, {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
                bool spawned = false;
                uint lastTick = 0;
                var touching = new System.Collections.Generic.List<RapierCollider>();
                int meshIndex = TwoSidedScenario.Arena(0).Colliders.Count - 1;
                var balls = new System.Collections.Generic.List<BallEntity>();
                TimeSpan last = clock.Elapsed;
                while (clock.Elapsed.TotalSeconds < seconds && !left)
                {
                    TimeSpan now = clock.Elapsed;
                    server.BeginFrame(now, (now - last).TotalSeconds);
                    server.EndFrame();
                    last = now;
                    if (spawned && server.TimeManager.Tick != lastTick)
                    {
                        lastTick = server.TimeManager.Tick;
                        bool walls = false;
                        bool mesh = false;
                        foreach (BallEntity ball in balls)
                        {
                            if (!physics.TryGetBody(ball, world, out BodyHandle handle))
                            {
                                continue;
                            }

                            touching.Clear();
                            world.Touching(RapierCollider.OfBody(handle, 0), touching);
                            foreach (RapierCollider other in touching)
                            {
                                if (!other.IsStatic)
                                {
                                    ballContacts++;
                                }
                                else if (other.Index == meshIndex)
                                {
                                    mesh = true;
                                }
                                else
                                {
                                    walls = true;
                                }
                            }
                        }

                        wallTicks += walls ? 1 : 0;
                        meshTicks += mesh ? 1 : 0;
                    }

                    if (!spawned && joined != 0)
                    {
                        for (int index = 0; index < TwoSidedScenario.BallPositions.Length; index++)
                        {
                            BallEntity ball = BallEntity.Create(index, () => world.StateHash, record);
                            ball.Position = TwoSidedScenario.BallPositions[index];
                            server.ServerManager.Spawn(ball, joined);
                            balls.Add(ball);
                        }

                        spawned = true;
                        Console.WriteLine($"spawned three balls for peer {joined} at tick {server.TimeManager.Tick}");
                    }

                    Thread.Sleep(1);
                }

                server.ServerManager.StopConnection();
            }

            string path = Path.Combine(output, "server.log");
            record.Write(path);
            Console.WriteLine($"server record: {record.Hashes.Count} ticks, {record.Applies} applies, written to {path}");
            Console.WriteLine($"ticks with a ball on a wall: {wallTicks}, on the triangle mesh: {meshTicks}; ball-to-ball contacts (counted from both balls): {ballContacts}");
            return 0;
        }

        private static string Argument(string[] args, string name, string fallback)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }
    }

    public sealed class BallEntity : StandaloneEntity
    {
        private BallEntity(BallBehaviour behaviour)
            : base(TwoSidedScenario.BallPrefabId, new EntityBehaviour[] { behaviour })
        {
        }

        public static BallEntity Create(int index, Func<ulong> worldHash, TwoSidedRecord record)
        {
            var behaviour = new BallBehaviour();
            var entity = new BallEntity(behaviour);
            behaviour.Motion = new TwoSidedMotion(() => entity.Body, index, worldHash, record);
            return entity;
        }

        public override bool TryGetBody(out BodyDesc body)
        {
            body = TwoSidedScenario.Ball(Position);
            return true;
        }
    }

    public sealed class BallBehaviour : EntityBehaviour
    {
        public TwoSidedMotion Motion { get; set; }

        protected override void OnRegisterInput(NetworkInput input) => Motion.Register(input);
    }
}
