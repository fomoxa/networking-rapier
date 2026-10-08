# 08.16 Two-sided check: a Rapier console server with a Rapier Unity client

> Sub-step 08.16; the plan is in the section "Kế hoạch đề xuất của 08.16: kiểm hai phía" (proposed plan of 08.16: two-sided check) of `implementation/08-prediction-physics.md` in the `unity` repository, and the results are also recorded in its 08.16 section. This is the first time a console server runs with a Unity client; 10.9 only checked a console server with a console client.

| | Work | Status |
|---|---|:---:|
| 1 | Shared scenario file: scene file, ball description, impulses, input and reconcile codecs, record | ✅ |
| 2 | Console server `checks/two-sided/` | ✅ |
| 3 | Unity client: the `TwoSidedClientCheck` test | ✅ |
| 4 | `Tools/two-sided-check.sh` (`linux`, `windows`), comparison of the two records | ✅ |
| 5 | Runs on two combinations: Linux and Windows servers, Windows Unity client | ✅ |

Rules: P27 (Q163 (1) A, (3) A, (5) A).

---

## 1. Overview

### Scenario

- The balls move in a closed box without gravity, made of a floor, a ceiling, four walls (boxes) and a triangle-mesh pyramid in the middle. The server builds the scene file in code (`TwoSidedScenario.Arena`) and writes it to disk; the client reads the same bytes through `RapierPhysics.SceneFileOf`. The server loads the geometry into the world of `sceneId` 0, and the client into the world of the open scene.
- When the client connects, the server spawns three balls owned by the client, in a fixed order. Each ball is a sphere with radius 0.5, mass 1, friction 0.5, restitution 0.5 and the `Average` combine rule. The Unity client builds the balls from a prefab with a matching `SphereCollider`, `PhysicsMaterial` and `Rigidbody`, and the 08.10 converter produces the same description the server uses.
- On every tick, `Apply` adds an impulse to the ball computed from the tick and the ball's index (its spawn order). The impulse comes from integer bit mixing divided by 1024, with no trigonometric functions, so that CoreCLR and Mono produce the same bits. The input content does not affect the impulse, so a late input, which makes the server repeat the previous one, causes no divergence.
- Reconcile runs every tick (`ReconcileInterval` 1) on 13 floats of the body: position, rotation, velocity and angular velocity. Both sides must match bit for bit, and every mismatch is counted.
- Each side records `tick → StateHash` of the world in `Capture`, that is, after the step of that tick; the client overwrites entries when it replays. The server stops when the client disconnects. The client runs for 25 seconds of wall-clock time, with `Thread.Sleep(1)` between frames.

### Criteria and results

The check passes when every tick present in both records has the same hash, there are at least 1000 common ticks, and no reconcile mismatches. The client runs in the Unity 6000.5.7f1 Editor (Mono, Windows, batch mode).

| Server | Common ticks | Hash mismatches | Reconcile mismatches | Result |
|---|---:|---:|---:|:---:|
| Linux, .NET 8.0.30 | 1484 | 0 | 0 | PASS |
| Windows, .NET 9.0.20 | 1483 | 0 | 0 | PASS |

The server counts contacts through each ball's `Touching`. In one run, balls touched a wall in 110 ticks and the mesh in 21 ticks, and touched each other 6 times (counted from both sides of each pair).

### Findings

- The client only starts predicting after the first reconcile state. Before that it sends inputs without applying them; when it reconciles, it restores the server state at tick t and replays from t + 1. The client's first applied tick is therefore always later than the server's. The first prototype started the impulses at "first applied tick + 60" and was off by exactly one tick at the start, which gave three reconcile mismatches. The current scenario does not depend on the first applied tick.
- `RapierPhysics` used to create bodies in the iteration order of a `HashSet`; it now uses `ObjectId` order (08.15).
- Unity also runs `[Explicit]` tests when filtering with `-assemblyNames`, so the client test skips itself when the environment variables set by the script are missing.

### Differences from the plan

- There is no `StartTick` synchronized through state. The impulses apply from the first tick, and the state before the client starts predicting comes from the server's reconcile (see Findings). Synchronized state would need a model on a reliable channel in the registry, which the check does not need.
- The script uses the WSL IP address when the server runs on Linux, and `127.0.0.1` when it runs on Windows. Parameters reach Unity through `WSLENV`.

---

## 2. Code

`com.fomoxa.networking.rapier/Tests/Unity/TwoSided/TwoSidedScenario.cs` (compiled into the Unity test assembly and into the console server):

```csharp
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Rapier.TwoSided
{
    public static class TwoSidedScenario
    {
        public const uint BallPrefabId = 0x2B00_0001;
        public const uint BallFingerprint = 0x2B00_F001;
        public const uint ClientSceneId = 0x2B00_0A0A;
        public const int TickRate = 60;
        public const string SceneFileName = "two-sided.fomoxascene";

        public static readonly ColliderMaterial BallMaterial = new ColliderMaterial(0.5f, 0.5f, CombineRule.Average, CombineRule.Average);

        public static readonly Vector3[] BallPositions =
        {
            new Vector3(-3f, 5f, 0f),
            new Vector3(0f, 5f, 1f),
            new Vector3(3f, 5f, -1f),
        };

        public static BodyDesc Ball(Vector3 position) =>
            new BodyDesc(BodyKind.Dynamic, new[] { new ColliderDesc(BodyShape.Sphere(0.5f), Vector3.Zero, Quaternion.Identity, BallMaterial, 0, false) }, position, Quaternion.Identity, 1f);

        public static SceneFile Arena(uint sceneId)
        {
            var file = new SceneFile { SceneId = sceneId };
            Add(file, Box(new Vector3(0f, -0.5f, 0f), new Vector3(6f, 0.5f, 6f)));
            Add(file, Box(new Vector3(0f, 10.5f, 0f), new Vector3(6f, 0.5f, 6f)));
            Add(file, Box(new Vector3(-6.5f, 5f, 0f), new Vector3(0.5f, 5f, 6f)));
            Add(file, Box(new Vector3(6.5f, 5f, 0f), new Vector3(0.5f, 5f, 6f)));
            Add(file, Box(new Vector3(0f, 5f, -6.5f), new Vector3(6f, 5f, 0.5f)));
            Add(file, Box(new Vector3(0f, 5f, 6.5f), new Vector3(6f, 5f, 0.5f)));
            Vector3[] vertices =
            {
                new Vector3(-2f, 0f, -2f),
                new Vector3(2f, 0f, -2f),
                new Vector3(2f, 0f, 2f),
                new Vector3(-2f, 0f, 2f),
                new Vector3(0f, 2.5f, 0f),
            };
            int[] triangles = { 0, 4, 1, 1, 4, 2, 2, 4, 3, 3, 4, 0 };
            Add(file, new ColliderDesc(BodyShape.TriangleMesh(vertices, triangles), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 0, false));
            for (int layer = 0; layer < 32; layer++)
            {
                file.LayerCollisions.Add(uint.MaxValue);
            }

            return file;
        }

        public static Vector3 Impulse(uint tick, int ball)
        {
            uint mixed = unchecked((tick * 2654435761u) ^ ((uint)(ball + 16) * 40503u));
            mixed ^= mixed >> 15;
            mixed = unchecked(mixed * 2246822519u);
            mixed ^= mixed >> 13;
            return new Vector3(Component(mixed), Component(mixed >> 8), Component(mixed >> 16));
        }

        private static float Component(uint bits) => ((int)(bits & 0xFF) - 127) / 1024f;

        private static ColliderDesc Box(Vector3 center, Vector3 halfExtents) =>
            new ColliderDesc(BodyShape.Box(halfExtents), center, Quaternion.Identity, ColliderMaterial.Default, 0, false);

        private static void Add(SceneFile file, ColliderDesc collider) => file.Colliders.Add(SceneFileGeometry.ToFile(collider));
    }

    public sealed class TwoSidedInput
    {
        public uint Sequence { get; set; }
    }

    public sealed class TwoSidedInputCodec : IMessageCodec<TwoSidedInput>
    {
        public static readonly TwoSidedInputCodec Instance = new TwoSidedInputCodec();

        private readonly byte[] buffer = new byte[4];

        public uint MessageId => 0x2B00_0011;

        public ReadOnlyMemory<byte> Encode(TwoSidedInput value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value.Sequence);
            return buffer;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref TwoSidedInput value)
        {
            if (payload.Length < 4)
            {
                throw new MessageDecodeException($"a two-sided input needs 4 bytes, got {payload.Length}", null);
            }

            value.Sequence = BinaryPrimitives.ReadUInt32LittleEndian(payload.Span);
        }
    }

    public sealed class TwoSidedState
    {
        public float[] Values { get; } = new float[13];
    }

    public sealed class TwoSidedStateCodec : IMessageCodec<TwoSidedState>
    {
        public static readonly TwoSidedStateCodec Instance = new TwoSidedStateCodec();

        private readonly byte[] buffer = new byte[52];

        public uint MessageId => 0x2B00_0012;

        public ReadOnlyMemory<byte> Encode(TwoSidedState value)
        {
            for (int index = 0; index < 13; index++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(index * 4), BitConverter.SingleToInt32Bits(value.Values[index]));
            }

            return buffer;
        }

        public void Decode(ReadOnlyMemory<byte> payload, ref TwoSidedState value)
        {
            if (payload.Length < 52)
            {
                throw new MessageDecodeException($"a two-sided state needs 52 bytes, got {payload.Length}", null);
            }

            for (int index = 0; index < 13; index++)
            {
                value.Values[index] = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(payload.Span.Slice(index * 4)));
            }
        }
    }

    public sealed class TwoSidedRecord
    {
        public SortedDictionary<uint, ulong> Hashes { get; } = new SortedDictionary<uint, ulong>();

        public SortedDictionary<int, uint> FirstTicks { get; } = new SortedDictionary<int, uint>();

        public int Mismatches { get; set; }

        public int Applies { get; set; }

        public SortedDictionary<uint, int> Unbodied { get; } = new SortedDictionary<uint, int>();

        public void Write(string path)
        {
            using (var writer = new StreamWriter(path))
            {
                foreach (KeyValuePair<int, uint> entry in FirstTicks)
                {
                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "first {0} {1}", entry.Key, entry.Value));
                }

                writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "mismatches {0}", Mismatches));
                writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "applies {0}", Applies));
                foreach (KeyValuePair<uint, int> entry in Unbodied)
                {
                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "unbodied {0} {1}", entry.Key, entry.Value));
                }
                foreach (KeyValuePair<uint, ulong> entry in Hashes)
                {
                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0} {1:X16}", entry.Key, entry.Value));
                }
            }
        }
    }

    public sealed class TwoSidedMotion
    {
        private readonly Func<PhysicsBody> body;
        private readonly int ball;
        private readonly Func<ulong> worldHash;
        private readonly TwoSidedRecord record;
        private uint appliedTick;

        public TwoSidedMotion(Func<PhysicsBody> body, int ball, Func<ulong> worldHash, TwoSidedRecord record)
        {
            this.body = body;
            this.ball = ball;
            this.worldHash = worldHash;
            this.record = record;
        }

        public void Register(NetworkInput input)
        {
            input.Use(TwoSidedInputCodec.Instance, Gather, Apply);
            input.Reconcile(TwoSidedStateCodec.Instance, Capture, Restore, Matches);
        }

        private static void Gather(TwoSidedInput input)
        {
            input.Sequence++;
        }

        private void Apply(TwoSidedInput input, InputContext context)
        {
            PhysicsBody current = body();
            if (!current.IsValid)
            {
                record.Unbodied.TryGetValue(context.Tick, out int count);
                record.Unbodied[context.Tick] = count + 1;
                return;
            }

            if (!record.FirstTicks.ContainsKey(ball))
            {
                record.FirstTicks[ball] = context.Tick;
            }

            appliedTick = context.Tick;
            record.Applies++;
            current.AddImpulse(TwoSidedScenario.Impulse(context.Tick, ball));
        }

        private void Capture(TwoSidedState state)
        {
            PhysicsBody current = body();
            if (!current.IsValid)
            {
                return;
            }

            BodyState values = current.State;
            float[] into = state.Values;
            into[0] = values.Position.X;
            into[1] = values.Position.Y;
            into[2] = values.Position.Z;
            into[3] = values.Rotation.X;
            into[4] = values.Rotation.Y;
            into[5] = values.Rotation.Z;
            into[6] = values.Rotation.W;
            into[7] = values.Velocity.X;
            into[8] = values.Velocity.Y;
            into[9] = values.Velocity.Z;
            into[10] = values.AngularVelocity.X;
            into[11] = values.AngularVelocity.Y;
            into[12] = values.AngularVelocity.Z;
            if (appliedTick != 0)
            {
                record.Hashes[appliedTick] = worldHash();
            }
        }

        private void Restore(TwoSidedState state)
        {
            PhysicsBody current = body();
            if (!current.IsValid)
            {
                return;
            }

            float[] from = state.Values;
            current.State = new BodyState
            {
                Position = new Vector3(from[0], from[1], from[2]),
                Rotation = new Quaternion(from[3], from[4], from[5], from[6]),
                Velocity = new Vector3(from[7], from[8], from[9]),
                AngularVelocity = new Vector3(from[10], from[11], from[12]),
            };
        }

        private bool Matches(TwoSidedState server, TwoSidedState predicted)
        {
            for (int index = 0; index < 13; index++)
            {
                if (BitConverter.SingleToInt32Bits(server.Values[index]) != BitConverter.SingleToInt32Bits(predicted.Values[index]))
                {
                    record.Mismatches++;
                    return false;
                }
            }

            return true;
        }
    }
}
```

`checks/two-sided/Fomoxa.Rapier.TwoSided.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>disable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <Unity>../../../unity/com.fomoxa.networking</Unity>
    <Package>../../com.fomoxa.networking.rapier</Package>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="$(Unity)/Runtime/Core/**/*.cs" />
    <Compile Include="$(Unity)/Tests/Fixtures/**/*.cs" />
    <Compile Include="$(Unity)/Standalone~/**/*.cs" />
    <Compile Include="$(Package)/Runtime/Rapier/**/*.cs" />
    <Compile Include="$(Package)/Tests/Unity/TwoSided/TwoSidedScenario.cs" />
    <Compile Include="*.cs" />
  </ItemGroup>

  <ItemGroup>
    <None Include="$(Package)/Runtime/Plugins/Linux/x86_64/libfomoxa_rapier.so" Link="libfomoxa_rapier.so" CopyToOutputDirectory="PreserveNewest" />
    <None Include="$(Package)/Runtime/Plugins/Windows/x86_64/fomoxa_rapier.dll" Link="fomoxa_rapier.dll" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

  <ItemGroup>
    <Reference Include="Fomoxa.Attributes">
      <HintPath>$(Unity)/Runtime/Plugins/Fomoxa.Attributes.dll</HintPath>
    </Reference>
    <Reference Include="Fomoxa.Net">
      <HintPath>$(Unity)/Runtime/Plugins/Fomoxa.Net.dll</HintPath>
    </Reference>
  </ItemGroup>

</Project>
```

`checks/two-sided/Program.cs`:

```csharp
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
```

`com.fomoxa.networking.rapier/Tests/Unity/TwoSided/TwoSidedClientCheck.cs`:

```csharp
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
```

`Tools/two-sided-check.sh`:

```bash
#!/bin/bash
set -e

MODE="${1:-linux}"
WIN_USER="admin"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$SCRIPT_DIR/.." && pwd)"
PORT=7790
CLIENT_SECONDS="${CLIENT_SECONDS:-25}"
WIN_DIR="C:\\Users\\$WIN_USER\\unity-check-rapier\\two-sided"
DIR="/mnt/c/Users/$WIN_USER/unity-check-rapier/two-sided"
SERVER_WIN_ROOT="C:\\Users\\$WIN_USER\\rapier-two-sided"
SERVER_DST="/mnt/c/Users/$WIN_USER/rapier-two-sided"
DOTNET_WIN="/mnt/c/Program Files/dotnet/dotnet.exe"
export LANG=C.UTF-8 LC_ALL=C.UTF-8 DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

rm -rf "$DIR"
mkdir -p "$DIR"

case "$MODE" in
  linux)
    HOST="$(hostname -I | awk '{print $1}')"
    dotnet build -c Release "$REPO/checks/two-sided/Fomoxa.Rapier.TwoSided.csproj" > "$DIR/server-build.txt" 2>&1 || { grep -E " error " "$DIR/server-build.txt" | sort -u; exit 1; }
    SERVER_OUT="$REPO/checks/two-sided/out"
    rm -rf "$SERVER_OUT"
    dotnet "$REPO/checks/two-sided/bin/Release/net8.0/Fomoxa.Rapier.TwoSided.dll" --port "$PORT" --out "$SERVER_OUT" > "$DIR/server-console.txt" 2>&1 &
    SERVER_PID=$!
    ;;
  windows)
    HOST="127.0.0.1"
    mkdir -p "$SERVER_DST/unity" "$SERVER_DST/networking-rapier"
    rsync -a --delete --exclude='test-project' --exclude='.git' "$REPO/../unity/com.fomoxa.networking/" "$SERVER_DST/unity/com.fomoxa.networking/"
    rsync -a --delete --exclude='.git' --exclude='test-project' --exclude='native/fomoxa-rapier/target' --exclude='tests' \
      --exclude='checks/two-sided/bin' --exclude='checks/two-sided/obj' --exclude='checks/two-sided/out' "$REPO/" "$SERVER_DST/networking-rapier/"
    sed -i 's|<TargetFramework>net8.0</TargetFramework>|<TargetFramework>net9.0</TargetFramework>|' "$SERVER_DST/networking-rapier/checks/two-sided/Fomoxa.Rapier.TwoSided.csproj"
    "$DOTNET_WIN" build -c Release "$SERVER_WIN_ROOT\\networking-rapier\\checks\\two-sided\\Fomoxa.Rapier.TwoSided.csproj" > "$DIR/server-build.txt" 2>&1 || { grep -E " error " "$DIR/server-build.txt" | sort -u; exit 1; }
    SERVER_OUT="$SERVER_DST/networking-rapier/checks/two-sided/out"
    rm -rf "$SERVER_OUT"
    "$DOTNET_WIN" "$SERVER_WIN_ROOT\\networking-rapier\\checks\\two-sided\\bin\\Release\\net9.0\\Fomoxa.Rapier.TwoSided.dll" --port "$PORT" --out "$SERVER_WIN_ROOT\\networking-rapier\\checks\\two-sided\\out" > "$DIR/server-console.txt" 2>&1 &
    SERVER_PID=$!
    ;;
  *)
    echo "usage: $0 [linux|windows]"
    exit 2
    ;;
esac

for attempt in $(seq 1 100); do
  [ -f "$SERVER_OUT/two-sided.fomoxascene" ] && break
  sleep 0.2
done
cp "$SERVER_OUT/two-sided.fomoxascene" "$DIR/"

export FOMOXA_TWO_SIDED_HOST="$HOST" FOMOXA_TWO_SIDED_PORT="$PORT" FOMOXA_TWO_SIDED_DIR="$WIN_DIR" FOMOXA_TWO_SIDED_SECONDS="$CLIENT_SECONDS"
export WSLENV="FOMOXA_TWO_SIDED_HOST:FOMOXA_TWO_SIDED_PORT:FOMOXA_TWO_SIDED_DIR:FOMOXA_TWO_SIDED_SECONDS${WSLENV:+:$WSLENV}"
TEST_FILTER="Fomoxa.Unity.Rapier.Tests.TwoSidedClientCheck.TheClientPredictsTheBallsOfATwoSidedServer" bash "$SCRIPT_DIR/unity-windows-check.sh" | tail -6

wait "$SERVER_PID" || true
cp "$SERVER_OUT/server.log" "$DIR/server.log"
cat "$DIR/server-console.txt"
grep "\[two-sided\]" "$REPO/test-project/Logs/unity-windows-check.log" || true

python3 - "$DIR/server.log" "$DIR/client.log" <<'PY'
import sys

def read(path):
    firsts, hashes, values = {}, {}, {}
    for line in open(path):
        parts = line.split()
        if parts[0] == "first":
            firsts[int(parts[1])] = int(parts[2])
        elif parts[0] == "unbodied":
            values.setdefault("unbodied", []).append((int(parts[1]), int(parts[2])))
        elif parts[0] in ("mismatches", "applies"):
            values[parts[0]] = int(parts[1])
        else:
            hashes[int(parts[0])] = parts[1]
    return firsts, hashes, values

server_firsts, server, server_values = read(sys.argv[1])
client_firsts, client, client_values = read(sys.argv[2])
print("first applied tick per ball: server", server_firsts, "client", client_firsts)
common = sorted(set(server) & set(client))
different = [tick for tick in common if server[tick] != client[tick]]
start = max(list(server_firsts.values()) + list(client_firsts.values()) or [0])
moving = [tick for tick in common if tick >= start]
print(f"ticks: server {len(server)}, client {len(client)}, common {len(common)}, common after the start {len(moving)}")
print(f"client reconcile mismatches: {client_values.get('mismatches')}")
print(f"applies without a body: server {server_values.get('unbodied', [])} client {client_values.get('unbodied', [])}")
if different:
    print(f"DIFFERENT hashes at {len(different)} ticks, first at {different[0]}: server {server[different[0]]} client {client[different[0]]}")
    sys.exit(1)
if len(moving) < 1000 or client_values.get("mismatches", 1) != 0:
    print("NOT ENOUGH: fewer than 1000 common moving ticks or mismatched reconciles")
    sys.exit(1)
print("PASS: every common tick has the same world hash on both sides")
PY
```

