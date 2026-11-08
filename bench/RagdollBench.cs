using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using BundleFixture;
using Fomoxa.Networking.Sessions;
using Fomoxa.Networking.Simulation;
using Fomoxa.Networking.Standalone;

namespace Fomoxa.Networking.Rapier.Bench
{
    public sealed class RagdollBench
    {
        private const ushort Port = 7777;
        private const int ConnectFrames = 600;
        private const float Spacing = 3f;
        private const float DropHeight = 2f;

        private readonly int ragdolls;
        private readonly int fallingTicks;
        private readonly int settleTicks;
        private readonly int restingTicks;
        private readonly FomoxaRegistry registry = TestObjects.Registry();
        private readonly NetworkSettings settings = new NetworkSettings { PhysicsBackend = PhysicsBackend.Rapier };
        private readonly List<Exception> logged = new List<Exception>();
        private readonly double frameSeconds;
        private NetworkRuntime server;
        private NetworkRuntime client;
        private TimeSpan now;

        public RagdollBench(int ragdolls, int fallingTicks, int settleTicks, int restingTicks)
        {
            this.ragdolls = ragdolls;
            this.fallingTicks = fallingTicks;
            this.settleTicks = settleTicks;
            this.restingTicks = restingTicks;
            frameSeconds = 1.0 / settings.TickRate;
        }

        public (PhaseResult Falling, PhaseResult Resting) Run()
        {
            using (var physics = new RapierScenes(new Vector3(0f, -9.81f, 0f)))
            {
                float half = (MathF.Ceiling(MathF.Sqrt(ragdolls)) * Spacing) + Spacing;
                physics.AddStatic(0, new[] { new ColliderDesc(BodyShape.Box(new Vector3(half, 0.5f, half)), new Vector3(0f, -0.5f, 0f), Quaternion.Identity, ColliderMaterial.Default, 0, false) });
                Connect(physics);
                Spawn();
                PhaseResult falling = Measure(fallingTicks);
                for (int tick = 0; tick < settleTicks; tick++)
                {
                    Frame();
                }

                PhaseResult resting = Measure(restingTicks);
                if (client.ClientManager.Spawned.Count != ragdolls)
                {
                    throw new InvalidOperationException($"the client sees {client.ClientManager.Spawned.Count} ragdolls instead of {ragdolls}");
                }

                if (logged.Count > 0)
                {
                    throw new InvalidOperationException("the runtime logged an exception", logged[0]);
                }

                server.ServerManager.StopConnection();
                client.ClientManager.StopConnection();
                return (falling, resting);
            }
        }

        private void Connect(IPhysicsScenes physics)
        {
            var factory = new LoopbackFactory();
            var log = new NetworkLog(logged.Add, message => { });
            var serverPrefabs = new StandalonePrefabs();
            serverPrefabs.Register(Ragdoll.RagdollPrefabId, Ragdoll.Fingerprint, () => new Ragdoll());
            var clientPrefabs = new StandalonePrefabs();
            clientPrefabs.Register(Ragdoll.RagdollPrefabId, Ragdoll.Fingerprint, () => new Ragdoll());
            var scenes = new EmptySceneFiles();
            server = StandaloneRuntime.Create(registry, settings, factory, serverPrefabs, new StandaloneBehaviours(), scenes, () => now, log, physics);
            client = StandaloneRuntime.Create(registry, settings, factory, clientPrefabs, new StandaloneBehaviours(), scenes, () => now, log);
            server.ServerManager.StartConnection(Port);
            client.ClientManager.StartConnection("127.0.0.1", Port);
            for (int frame = 0; frame < ConnectFrames && client.ClientManager.State != ConnectionState.Started; frame++)
            {
                Frame();
            }

            if (client.ClientManager.State != ConnectionState.Started)
            {
                throw new InvalidOperationException($"the client is {client.ClientManager.State} after {ConnectFrames} frames");
            }
        }

        private void Spawn()
        {
            int side = (int)MathF.Ceiling(MathF.Sqrt(ragdolls));
            for (int index = 0; index < ragdolls; index++)
            {
                var ragdoll = new Ragdoll { Position = new Vector3(((index % side) - (side / 2f)) * Spacing, DropHeight, ((index / side) - (side / 2f)) * Spacing) };
                server.ServerManager.Spawn(ragdoll);
            }
        }

        private PhaseResult Measure(int ticks)
        {
            var elapsed = new double[ticks];
            long readsBefore = Ragdoll.Reads;
            for (int tick = 0; tick < ticks; tick++)
            {
                elapsed[tick] = Frame();
            }

            double[] sorted = elapsed.OrderBy(value => value).ToArray();
            return new PhaseResult(sorted.Average(), Percentile(sorted, 0.5), Percentile(sorted, 0.95), sorted[sorted.Length - 1], (double)(Ragdoll.Reads - readsBefore) / ticks);
        }

        private double Frame()
        {
            now += TimeSpan.FromSeconds(frameSeconds);
            long started = Stopwatch.GetTimestamp();
            server.BeginFrame(now, frameSeconds);
            server.EndFrame();
            long elapsed = Stopwatch.GetTimestamp() - started;
            client.BeginFrame(now, frameSeconds);
            client.EndFrame();
            return elapsed * 1000.0 / Stopwatch.Frequency;
        }

        private static double Percentile(double[] sorted, double share) =>
            sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(share * sorted.Length) - 1)];
    }

    public readonly struct PhaseResult
    {
        public PhaseResult(double meanMs, double medianMs, double p95Ms, double maxMs, double slotsPerTick)
        {
            MeanMs = meanMs;
            MedianMs = medianMs;
            P95Ms = p95Ms;
            MaxMs = maxMs;
            SlotsPerTick = slotsPerTick;
        }

        public double MeanMs { get; }

        public double MedianMs { get; }

        public double P95Ms { get; }

        public double MaxMs { get; }

        public double SlotsPerTick { get; }
    }
}
