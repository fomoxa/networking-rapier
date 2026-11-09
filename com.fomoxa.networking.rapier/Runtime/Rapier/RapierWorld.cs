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
        private uint[] moved = new uint[64];
        private RapierNative.ColliderRef[] touched = new RapierNative.ColliderRef[16];
        private RapierNative.ContactPoint[] contactPoints = new RapierNative.ContactPoint[16];

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
                id = RapierNative.fr_body_create(world, (uint)desc.Kind, position, rotation, desc.Mass, (uint)motion.Locks, motion.UseGravity ? 1f : 0f, motion.LinearDamping, motion.AngularDamping, motion.ContinuousCollision, native.Native, (uint)native.Native.Length);
            }

            if (id == 0)
            {
                throw new ArgumentException("Rapier refused the body (no collider, degenerate hull or mesh, or a disposed world)", nameof(desc));
            }

            return new BodyHandle((int)id);
        }

        public bool RemoveBody(BodyHandle body) => body.IsValid && RapierNative.fr_body_remove(world, (uint)body.Value);

        public bool Contains(BodyHandle body) => body.IsValid && RapierNative.fr_body_contains(world, (uint)body.Value);

        public JointHandle CreateJoint(BodyHandle a, BodyHandle b, in JointDesc desc)
        {
            uint id = RapierNative.fr_joint_create(world, Id(a), Id(b), RapierNative.Joint.Of(desc));
            if (id == 0)
            {
                throw new ArgumentException("Rapier refused the joint (body a missing, b missing or equal to a, an unknown kind, a rope without a positive length, or a disposed world)", nameof(desc));
            }

            return new JointHandle((int)id);
        }

        public bool RemoveJoint(JointHandle joint) => joint.IsValid && RapierNative.fr_joint_remove(world, (uint)joint.Value);

        public bool Contains(JointHandle joint) => joint.IsValid && RapierNative.fr_joint_contains(world, (uint)joint.Value);

        public void SetJoint(JointHandle joint, in JointDesc desc)
        {
            if (!joint.IsValid || !RapierNative.fr_joint_set(world, (uint)joint.Value, RapierNative.Joint.Of(desc)))
            {
                throw new ArgumentException($"joint {joint.Value} is not in this world, or Rapier refused the description", nameof(joint));
            }
        }

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

        public void AddTorque(BodyHandle body, Vector3 torque)
        {
            if (!RapierNative.fr_body_add_torque(world, Id(body), torque.X, torque.Y, torque.Z))
            {
                throw Missing(body);
            }
        }

        public void AddTorqueImpulse(BodyHandle body, Vector3 torqueImpulse)
        {
            if (!RapierNative.fr_body_add_torque_impulse(world, Id(body), torqueImpulse.X, torqueImpulse.Y, torqueImpulse.Z))
            {
                throw Missing(body);
            }
        }

        public void AddForceAtPoint(BodyHandle body, Vector3 force, Vector3 point)
        {
            if (!RapierNative.fr_body_add_force_at_point(world, Id(body), force.X, force.Y, force.Z, point.X, point.Y, point.Z))
            {
                throw Missing(body);
            }
        }

        public void AddImpulseAtPoint(BodyHandle body, Vector3 impulse, Vector3 point)
        {
            if (!RapierNative.fr_body_add_impulse_at_point(world, Id(body), impulse.X, impulse.Y, impulse.Z, point.X, point.Y, point.Z))
            {
                throw Missing(body);
            }
        }

        public CharacterMove MoveCharacter(BodyHandle body, Vector3 desired, float seconds, in CharacterSettings settings) =>
            MoveCharacter(body, desired, seconds, settings, QueryFilter.All);

        public CharacterMove MoveCharacter(BodyHandle body, Vector3 desired, float seconds, in CharacterSettings settings, in QueryFilter filter)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f)
            {
                throw new ArgumentException("seconds must be finite and not negative", nameof(seconds));
            }

            RapierNative.CharacterSettings native = RapierNative.CharacterSettings.Of(settings);
            RapierNative.QueryFilter nativeFilter = RapierNative.QueryFilter.Of(filter);
            if (!RapierNative.fr_character_move(world, Id(body), desired, seconds, native, nativeFilter, out RapierNative.CharacterMove moved))
            {
                throw new ArgumentException($"body {body.Value} is not a kinematic body of this world with exactly one non-trigger box, sphere or capsule collider, or the move is not finite", nameof(body));
            }

            return new CharacterMove(moved.Moved, moved.Grounded != 0, moved.Sliding != 0);
        }

        public bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, in QueryFilter filter, out RayHit hit)
        {
            RapierNative.QueryFilter native = RapierNative.QueryFilter.Of(filter);
            bool found = RapierNative.fr_world_raycast(world, origin, direction, maxDistance, native, out RapierNative.RayHit nativeHit);
            hit = found ? HitOf(nativeHit) : default;
            return found;
        }

        public bool ShapeCast(in BodyShape shape, Vector3 position, Quaternion rotation, Vector3 direction, float maxDistance, in QueryFilter filter, out RayHit hit)
        {
            RapierNative.QueryShape nativeShape = RapierNative.QueryShape.Of(shape);
            RapierNative.QueryFilter native = RapierNative.QueryFilter.Of(filter);
            bool found = RapierNative.fr_world_shape_cast(world, nativeShape, position, rotation, direction, maxDistance, native, out RapierNative.RayHit nativeHit);
            hit = found ? HitOf(nativeHit) : default;
            return found;
        }

        public int Overlap(in BodyShape shape, Vector3 position, Quaternion rotation, in QueryFilter filter, BodyHandle[] results)
        {
            RapierNative.QueryShape nativeShape = RapierNative.QueryShape.Of(shape);
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            if (overlapped.Length < results.Length)
            {
                overlapped = new uint[results.Length];
            }

            RapierNative.QueryFilter native = RapierNative.QueryFilter.Of(filter);
            int count = (int)RapierNative.fr_world_overlap(world, nativeShape, position, rotation, native, overlapped, (uint)results.Length);
            for (int index = 0; index < count; index++)
            {
                results[index] = new BodyHandle((int)overlapped[index]);
            }

            return count;
        }

        public void CollectMoved(List<BodyHandle> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            uint count = RapierNative.fr_world_moved(world);
            if (count == 0)
            {
                return;
            }

            if (moved.Length < count)
            {
                moved = new uint[Math.Max(count, (uint)moved.Length * 2)];
            }

            count = RapierNative.fr_world_moved_copy(world, moved, count);
            for (int index = 0; index < (int)count; index++)
            {
                into.Add(new BodyHandle((int)moved[index]));
            }
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

        public int GetContactPoints(BodyHandle body, BodyHandle other, in QueryFilter filter, List<ContactPointInfo> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();
            RapierNative.QueryFilter native = RapierNative.QueryFilter.Of(filter);
            uint count = RapierNative.fr_body_contact_points(world, Id(body), Id(other), native, contactPoints, (uint)contactPoints.Length);
            if (count > contactPoints.Length)
            {
                contactPoints = new RapierNative.ContactPoint[count];
                count = RapierNative.fr_body_contact_points(world, Id(body), Id(other), native, contactPoints, (uint)contactPoints.Length);
            }

            AddContactPoints(count, into);
            return into.Count;
        }

        public void ContactPoints(RapierCollider collider, RapierCollider other, List<ContactPointInfo> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            uint count = RapierNative.fr_collider_contact_points(world, Id(collider.Body), (uint)collider.Index, Id(other.Body), (uint)other.Index, contactPoints, (uint)contactPoints.Length);
            if (count > contactPoints.Length)
            {
                contactPoints = new RapierNative.ContactPoint[count];
                count = RapierNative.fr_collider_contact_points(world, Id(collider.Body), (uint)collider.Index, Id(other.Body), (uint)other.Index, contactPoints, (uint)contactPoints.Length);
            }

            AddContactPoints(count, into);
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

        private static RayHit HitOf(in RapierNative.RayHit hit) =>
            new RayHit(new BodyHandle((int)hit.Body), new Vector3(hit.PointX, hit.PointY, hit.PointZ), new Vector3(hit.NormalX, hit.NormalY, hit.NormalZ), hit.Distance);

        private static ArgumentException Missing(BodyHandle body) => new ArgumentException($"body {body.Value} is not in this world", nameof(body));

        private static uint Id(BodyHandle body) => body.IsValid ? (uint)body.Value : 0u;

        private void AddContactPoints(uint count, List<ContactPointInfo> into)
        {
            for (int index = 0; index < count; index++)
            {
                RapierNative.ContactPoint point = contactPoints[index];
                into.Add(new ContactPointInfo(new Vector3(point.PointX, point.PointY, point.PointZ), new Vector3(point.NormalX, point.NormalY, point.NormalZ), point.Depth));
            }
        }

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
