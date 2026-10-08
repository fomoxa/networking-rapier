using System;
using System.Collections.Generic;
using System.Numerics;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Rapier
{
    public sealed class RapierWorld : IPhysicsWorld, IRapierBodies, IDisposable
    {
        private const int LayerCount = 32;

        private IntPtr world;
        private uint[] overlapped = new uint[64];
        private RapierNative.ColliderRef[] touched = new RapierNative.ColliderRef[16];

        public RapierWorld(Vector3 gravity)
        {
            world = RapierNative.fr_world_create(gravity.X, gravity.Y, gravity.Z);
        }

        public PhysicsBackend Backend => PhysicsBackend.Rapier;

        public bool IsDisposed => world == IntPtr.Zero;

        public ulong StateHash => RapierNative.fr_world_hash(world);

        public void SetLayerCollisions(IReadOnlyList<uint> masks)
        {
            if (masks == null)
            {
                throw new ArgumentNullException(nameof(masks));
            }

            if (masks.Count != LayerCount)
            {
                throw new ArgumentException($"a layer collision matrix has {LayerCount} masks, not {masks.Count}", nameof(masks));
            }

            var copy = new uint[LayerCount];
            for (int layer = 0; layer < LayerCount; layer++)
            {
                copy[layer] = masks[layer];
            }

            RapierNative.fr_world_set_layers(world, copy);
        }

        public StaticGroup AddStatic(IReadOnlyList<ColliderDesc> colliders)
        {
            if (colliders == null)
            {
                throw new ArgumentNullException(nameof(colliders));
            }

            uint group;
            uint first;
            using (var native = new RapierColliders(colliders))
            {
                group = RapierNative.fr_world_add_static(world, native.Native, (uint)native.Native.Length, out first);
            }

            if (group == 0)
            {
                throw new ArgumentException("Rapier refused a static collider (degenerate hull or mesh)", nameof(colliders));
            }

            return new StaticGroup(0, (int)group, (int)first, colliders.Count);
        }

        public void RemoveStatic(StaticGroup group) => RapierNative.fr_world_remove_static(world, (uint)group.Id);

        public void Step(float seconds) => RapierNative.fr_world_step(world, seconds);

        public PhysicsSnapshot CreateSnapshot() => new RapierSnapshot();

        public void Save(PhysicsSnapshot into)
        {
            RapierSnapshot snapshot = Expect(into);
            uint length = RapierNative.fr_world_snapshot(world);
            snapshot.Resize((int)length);
            if (!RapierNative.fr_world_snapshot_copy(world, snapshot.Bytes, length))
            {
                throw new InvalidOperationException("the Rapier world could not copy its snapshot");
            }
        }

        public void Load(PhysicsSnapshot from)
        {
            RapierSnapshot snapshot = Expect(from);
            if (snapshot.Bytes.Length == 0 || !RapierNative.fr_world_load(world, snapshot.Bytes, (uint)snapshot.Bytes.Length))
            {
                throw new ArgumentException("the snapshot does not hold a Rapier world of this build", nameof(from));
            }
        }

        public BodyHandle CreateBody(in BodyDesc desc)
        {
            float[] position = { desc.Position.X, desc.Position.Y, desc.Position.Z };
            float[] rotation = { desc.Rotation.X, desc.Rotation.Y, desc.Rotation.Z, desc.Rotation.W };
            uint id;
            using (var native = new RapierColliders(desc.Colliders))
            {
                BodyMotion motion = desc.Motion;
                id = RapierNative.fr_body_create(world, (uint)desc.Kind, position, rotation, desc.Mass, (uint)motion.Locks, motion.UseGravity ? 1f : 0f, motion.LinearDamping, motion.AngularDamping, native.Native, (uint)native.Native.Length);
            }

            if (id == 0)
            {
                throw new ArgumentException("Rapier refused the body (no collider, degenerate hull or mesh, or a disposed world)", nameof(desc));
            }

            return new BodyHandle((int)id);
        }

        public bool RemoveBody(BodyHandle body) => body.IsValid && RapierNative.fr_body_remove(world, (uint)body.Value);

        public bool Contains(BodyHandle body) => body.IsValid && RapierNative.fr_body_contains(world, (uint)body.Value);

        public BodyState GetBody(BodyHandle body)
        {
            if (!RapierNative.fr_body_get(world, Id(body), out RapierNative.BodyState state))
            {
                throw Missing(body);
            }

            return new BodyState
            {
                Position = new Vector3(state.PositionX, state.PositionY, state.PositionZ),
                Rotation = new Quaternion(state.RotationX, state.RotationY, state.RotationZ, state.RotationW),
                Velocity = new Vector3(state.VelocityX, state.VelocityY, state.VelocityZ),
                AngularVelocity = new Vector3(state.AngularVelocityX, state.AngularVelocityY, state.AngularVelocityZ),
            };
        }

        public BodyKind GetKind(BodyHandle body)
        {
            int kind = RapierNative.fr_body_kind(world, Id(body));
            if (kind < 0)
            {
                throw Missing(body);
            }

            return (BodyKind)kind;
        }

        public float GetMass(BodyHandle body)
        {
            Require(body);
            return RapierNative.fr_body_mass(world, (uint)body.Value);
        }

        public void SetBody(BodyHandle body, in BodyState state)
        {
            var native = new RapierNative.BodyState
            {
                PositionX = state.Position.X,
                PositionY = state.Position.Y,
                PositionZ = state.Position.Z,
                RotationX = state.Rotation.X,
                RotationY = state.Rotation.Y,
                RotationZ = state.Rotation.Z,
                RotationW = state.Rotation.W,
                VelocityX = state.Velocity.X,
                VelocityY = state.Velocity.Y,
                VelocityZ = state.Velocity.Z,
                AngularVelocityX = state.AngularVelocity.X,
                AngularVelocityY = state.AngularVelocity.Y,
                AngularVelocityZ = state.AngularVelocity.Z,
            };
            if (!RapierNative.fr_body_set(world, Id(body), native))
            {
                throw Missing(body);
            }
        }

        public void SetKind(BodyHandle body, BodyKind kind)
        {
            if (!RapierNative.fr_body_set_kind(world, Id(body), (uint)kind))
            {
                throw Missing(body);
            }
        }

        public void SetRewindable(BodyHandle body, bool rewindable)
        {
            if (!RapierNative.fr_body_set_rewindable(world, Id(body), rewindable))
            {
                throw Missing(body);
            }
        }

        public void AddForce(BodyHandle body, Vector3 force)
        {
            if (!RapierNative.fr_body_add_force(world, Id(body), force.X, force.Y, force.Z))
            {
                throw Missing(body);
            }
        }

        public void AddImpulse(BodyHandle body, Vector3 impulse)
        {
            if (!RapierNative.fr_body_add_impulse(world, Id(body), impulse.X, impulse.Y, impulse.Z))
            {
                throw Missing(body);
            }
        }

        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, out RayHit hit)
        {
            if (!RapierNative.fr_world_raycast(world, new[] { origin.X, origin.Y, origin.Z }, new[] { direction.X, direction.Y, direction.Z }, maxDistance, out RapierNative.RayHit found))
            {
                hit = default;
                return false;
            }

            hit = new RayHit(new BodyHandle((int)found.Body), new Vector3(found.PointX, found.PointY, found.PointZ), new Vector3(found.NormalX, found.NormalY, found.NormalZ), found.Distance);
            return true;
        }

        public int Overlap(Vector3 center, float radius, BodyHandle[] results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            if (overlapped.Length < results.Length)
            {
                overlapped = new uint[results.Length];
            }

            int count = (int)RapierNative.fr_world_overlap(world, center.X, center.Y, center.Z, radius, overlapped, (uint)results.Length);
            for (int index = 0; index < count; index++)
            {
                results[index] = new BodyHandle((int)overlapped[index]);
            }

            return count;
        }

        public void Touching(RapierCollider collider, List<RapierCollider> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            uint count = RapierNative.fr_collider_touching(world, Id(collider.Body), (uint)collider.Index, touched, (uint)touched.Length);
            if (count > touched.Length)
            {
                touched = new RapierNative.ColliderRef[count];
                count = RapierNative.fr_collider_touching(world, Id(collider.Body), (uint)collider.Index, touched, (uint)touched.Length);
            }

            for (int index = 0; index < count; index++)
            {
                into.Add(RapierCollider.FromNative(touched[index]));
            }
        }

        public void Dispose()
        {
            if (world != IntPtr.Zero)
            {
                RapierNative.fr_world_destroy(world);
                world = IntPtr.Zero;
            }
        }

        private static RapierSnapshot Expect(PhysicsSnapshot snapshot) =>
            snapshot as RapierSnapshot ?? throw new ArgumentException("the snapshot was not created by a Rapier world", nameof(snapshot));

        private static ArgumentException Missing(BodyHandle body) => new ArgumentException($"body {body.Value} is not in this world", nameof(body));

        private static uint Id(BodyHandle body) => body.IsValid ? (uint)body.Value : 0u;

        private void Require(BodyHandle body)
        {
            if (!Contains(body))
            {
                throw Missing(body);
            }
        }

        private sealed class RapierSnapshot : PhysicsSnapshot
        {
            public byte[] Bytes { get; private set; } = Array.Empty<byte>();

            public void Resize(int length)
            {
                if (Bytes.Length != length)
                {
                    Bytes = new byte[length];
                }
            }
        }
    }
}
