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
