using System.Collections.Generic;
using System.Numerics;
using Fomoxa.Networking.Simulation;
using Fomoxa.Networking.Standalone;

namespace Fomoxa.Networking.Rapier.Bench
{
    public sealed class Ragdoll : StandaloneEntity
    {
        public const uint RagdollPrefabId = 0x0000_0D01;
        public const uint Fingerprint = 0x0000_D0D1;

        private static readonly TransformSyncSettings Settings = new TransformSyncSettings(true, true, false, 0.01f, 0.1f, 0.01f);

        private static readonly (Vector3 Position, float HalfSize)[] Limbs =
        {
            (new Vector3(0f, 0f, 0.5f), 0.14f),
            (new Vector3(-0.45f, 0f, 0.2f), 0.1f),
            (new Vector3(0.45f, 0f, 0.2f), 0.1f),
            (new Vector3(-0.75f, 0f, 0.2f), 0.1f),
            (new Vector3(0.75f, 0f, 0.2f), 0.1f),
            (new Vector3(0f, 0f, -0.45f), 0.1f),
            (new Vector3(-0.15f, 0f, -0.75f), 0.1f),
            (new Vector3(0.15f, 0f, -0.75f), 0.1f),
            (new Vector3(-0.15f, 0f, -1.05f), 0.1f),
            (new Vector3(0.15f, 0f, -1.05f), 0.1f),
        };

        public Ragdoll()
            : base(RagdollPrefabId, Behaviours())
        {
        }

        public static int BodyCount => Limbs.Length + 1;

        public static long Reads { get; set; }

        public override bool TryGetBody(out BodyDesc body)
        {
            body = new BodyDesc(BodyKind.Dynamic, BodyShape.Box(new Vector3(0.25f, 0.15f, 0.3f)), Vector3.Zero, Quaternion.Identity, 10f);
            return true;
        }

        public override void GetChildBodies(List<ChildBodyDesc> into)
        {
            foreach ((Vector3 position, float halfSize) in Limbs)
            {
                into.Add(new ChildBodyDesc(new BodyDesc(BodyKind.Dynamic, BodyShape.Box(new Vector3(halfSize, halfSize, halfSize)), Vector3.Zero, Quaternion.Identity, 2f), position, Quaternion.Identity));
            }
        }

        private static EntityBehaviour[] Behaviours()
        {
            var behaviours = new EntityBehaviour[Limbs.Length + 1];
            behaviours[0] = new Part(null);
            for (int index = 0; index < Limbs.Length; index++)
            {
                behaviours[index + 1] = new Part(ChildBody.Of(index));
            }

            return behaviours;
        }

        private sealed class Part : EntityBehaviour, ITransformReceiver
        {
            public Part(ChildBody? body)
            {
                ITransformSource source = body.HasValue ? new ChildBodySource(this, body.Value) : (ITransformSource)new EntityPoseSource(this);
                SetTransform(new TransformSync(Settings), new CountedSource(source), this, body.HasValue ? TransformTarget.BodyRoot : TransformTarget.EntityPose);
            }

            public void ResetReceive()
            {
            }

            public void Receive(in TransformSample sample)
            {
            }
        }

        private sealed class CountedSource : ITransformSource
        {
            private readonly ITransformSource inner;

            public CountedSource(ITransformSource inner)
            {
                this.inner = inner;
            }

            public void ReadLocal(out Vector3 localPosition, out Quaternion localRotation, out Vector3 localScale)
            {
                Reads++;
                inner.ReadLocal(out localPosition, out localRotation, out localScale);
            }
        }
    }
}
