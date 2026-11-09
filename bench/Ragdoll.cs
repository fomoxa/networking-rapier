using System;
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

        private static readonly (int Parent, JointKind Kind)[] Joints =
        {
            (-1, JointKind.Spherical),
            (-1, JointKind.Spherical),
            (-1, JointKind.Spherical),
            (1, JointKind.Revolute),
            (2, JointKind.Revolute),
            (-1, JointKind.Spherical),
            (5, JointKind.Spherical),
            (5, JointKind.Spherical),
            (6, JointKind.Revolute),
            (7, JointKind.Revolute),
        };

        private static readonly Quaternion ElbowAxis = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);

        public Ragdoll(RapierScenes joints)
            : this(joints, new Part(null))
        {
        }

        private Ragdoll(RapierScenes joints, Part torso)
            : base(RagdollPrefabId, Behaviours(torso))
        {
            if (joints != null)
            {
                torso.Started = () => CreateJoints(joints);
            }
        }

        public static int JointCount => Joints.Length;

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

        private static EntityBehaviour[] Behaviours(Part torso)
        {
            var behaviours = new EntityBehaviour[Limbs.Length + 1];
            behaviours[0] = torso;
            for (int index = 0; index < Limbs.Length; index++)
            {
                behaviours[index + 1] = new Part(ChildBody.Of(index));
            }

            return behaviours;
        }

        private static Vector3 PositionOf(int limb) => limb < 0 ? Vector3.Zero : Limbs[limb].Position;

        private void CreateJoints(RapierScenes joints)
        {
            for (int limb = 0; limb < Joints.Length; limb++)
            {
                (int parent, JointKind kind) = Joints[limb];
                Vector3 pivot = (PositionOf(parent) + PositionOf(limb)) / 2f;
                Quaternion frame = kind == JointKind.Revolute && Limbs[limb].Position.Z > 0f ? ElbowAxis : Quaternion.Identity;
                var desc = new JointDesc(kind, pivot - PositionOf(parent), frame, pivot - PositionOf(limb), frame);
                desc = kind == JointKind.Revolute
                    ? desc.WithLimit(new JointLimit(-1.2f, 1.2f))
                    : desc.WithLimit(new JointLimit(-0.3f, 0.3f)).WithSwingLimits(new JointLimit(-0.6f, 0.6f), new JointLimit(-0.6f, 0.6f));
                joints.CreateJoint(this, Bodies[parent + 1], Bodies[limb + 1], desc);
            }
        }

        private sealed class Part : EntityBehaviour, ITransformReceiver
        {
            public Part(ChildBody? body)
            {
                ITransformSource source = body.HasValue ? new ChildBodySource(this, body.Value) : (ITransformSource)new EntityPoseSource(this);
                SetTransform(new TransformSync(Settings), new CountedSource(source), this, body.HasValue ? TransformTarget.BodyRoot : TransformTarget.EntityPose);
            }

            public Action Started { get; set; }

            public override void OnStartServer() => Started?.Invoke();

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
