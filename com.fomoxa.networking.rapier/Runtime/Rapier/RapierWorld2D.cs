using System;
using System.Collections.Generic;
using System.Numerics;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Rapier
{
    public sealed class RapierWorld2D : IPhysicsWorld2D, IRapierBodies, IDisposable
    {
        private const int LayerCount = 32;

        private IntPtr world;
        private uint[] overlapped = new uint[64];
        private uint[] moved = new uint[64];
        private RapierNative.ColliderRef[] touched = new RapierNative.ColliderRef[16];
        private RapierNative.ContactPoint2D[] contactPoints = new RapierNative.ContactPoint2D[16];

        public RapierWorld2D(Vector2 gravity)
        {
            world = RapierNative.fr2_world_create(gravity.X, gravity.Y);
        }

        public PhysicsBackend Backend => PhysicsBackend.Rapier;

        public bool IsDisposed => world == IntPtr.Zero;

        public ulong StateHash => RapierNative.fr2_world_hash(world);

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

            RapierNative.fr2_world_set_layers(world, copy);
        }

        public StaticGroup AddStatic(IReadOnlyList<ColliderDesc2D> colliders)
        {
            if (colliders == null)
            {
                throw new ArgumentNullException(nameof(colliders));
            }

            uint group;
            uint first;
            using (var native = new RapierColliders2D(colliders))
            {
                group = RapierNative.fr2_world_add_static(world, native.Native, (uint)native.Native.Length, out first);
            }

            if (group == 0)
            {
                throw new ArgumentException("Rapier refused a static 2D collider (degenerate polygon or polyline)", nameof(colliders));
            }

            return new StaticGroup(0, (int)group, (int)first, colliders.Count);
        }

        public void RemoveStatic(StaticGroup group) => RapierNative.fr2_world_remove_static(world, (uint)group.Id);

        public void Step(float seconds) => RapierNative.fr2_world_step(world, seconds);

        public PhysicsSnapshot CreateSnapshot() => new RapierSnapshot();

        public void Save(PhysicsSnapshot into)
        {
            RapierSnapshot snapshot = Expect(into);
            uint length = RapierNative.fr2_world_snapshot(world);
            snapshot.Resize((int)length);
            if (!RapierNative.fr2_world_snapshot_copy(world, snapshot.Bytes, length))
            {
                throw new InvalidOperationException("the 2D Rapier world could not copy its snapshot");
            }
        }

        public void Load(PhysicsSnapshot from)
        {
            RapierSnapshot snapshot = Expect(from);
            if (snapshot.Bytes.Length == 0 || !RapierNative.fr2_world_load(world, snapshot.Bytes, (uint)snapshot.Bytes.Length))
            {
                throw new ArgumentException("the snapshot does not hold a 2D Rapier world of this build", nameof(from));
            }
        }

        public BodyHandle CreateBody(in BodyDesc2D desc)
        {
            uint id;
            using (var native = new RapierColliders2D(desc.Colliders))
            {
                BodyMotion2D motion = desc.Motion;
                id = RapierNative.fr2_body_create(world, (uint)desc.Kind, desc.Position.X, desc.Position.Y, desc.Rotation, desc.Mass, (uint)motion.Locks, motion.GravityScale, motion.LinearDamping, motion.AngularDamping, motion.ContinuousCollision, native.Native, (uint)native.Native.Length);
            }

            if (id == 0)
            {
                throw new ArgumentException("Rapier refused the 2D body (no collider, degenerate polygon, or a disposed world)", nameof(desc));
            }

            return new BodyHandle((int)id);
        }

        public bool RemoveBody(BodyHandle body) => body.IsValid && RapierNative.fr2_body_remove(world, (uint)body.Value);

        public bool Contains(BodyHandle body) => body.IsValid && RapierNative.fr2_body_contains(world, (uint)body.Value);

        public JointHandle CreateJoint(BodyHandle a, BodyHandle b, in JointDesc2D desc)
        {
            uint id = RapierNative.fr2_joint_create(world, Id(a), Id(b), RapierNative.Joint2D.Of(desc));
            if (id == 0)
            {
                throw new ArgumentException("Rapier refused the joint (body a missing, b missing or equal to a, an unknown kind, a rope without a positive length, or a disposed world)", nameof(desc));
            }

            return new JointHandle((int)id);
        }

        public bool RemoveJoint(JointHandle joint) => joint.IsValid && RapierNative.fr2_joint_remove(world, (uint)joint.Value);

        public bool Contains(JointHandle joint) => joint.IsValid && RapierNative.fr2_joint_contains(world, (uint)joint.Value);

        public void SetJoint(JointHandle joint, in JointDesc2D desc)
        {
            if (!joint.IsValid || !RapierNative.fr2_joint_set(world, (uint)joint.Value, RapierNative.Joint2D.Of(desc)))
            {
                throw new ArgumentException($"joint {joint.Value} is not in this world, or Rapier refused the description", nameof(joint));
            }
        }

        public BodyState2D GetBody(BodyHandle body)
        {
            if (!RapierNative.fr2_body_get(world, Id(body), out RapierNative.BodyState2D state))
            {
                throw Missing(body);
            }

            return new BodyState2D
            {
                Position = new Vector2(state.PositionX, state.PositionY),
                Rotation = state.Rotation,
                Velocity = new Vector2(state.VelocityX, state.VelocityY),
                AngularVelocity = state.AngularVelocity,
            };
        }

        public BodyKind GetKind(BodyHandle body)
        {
            int kind = RapierNative.fr2_body_kind(world, Id(body));
            if (kind < 0)
            {
                throw Missing(body);
            }

            return (BodyKind)kind;
        }

        public float GetMass(BodyHandle body)
        {
            Require(body);
            return RapierNative.fr2_body_mass(world, (uint)body.Value);
        }

        public void SetBody(BodyHandle body, in BodyState2D state)
        {
            var native = new RapierNative.BodyState2D
            {
                PositionX = state.Position.X,
                PositionY = state.Position.Y,
                Rotation = state.Rotation,
                VelocityX = state.Velocity.X,
                VelocityY = state.Velocity.Y,
                AngularVelocity = state.AngularVelocity,
            };
            if (!RapierNative.fr2_body_set(world, Id(body), native))
            {
                throw Missing(body);
            }
        }

        public void SetKind(BodyHandle body, BodyKind kind)
        {
            if (!RapierNative.fr2_body_set_kind(world, Id(body), (uint)kind))
            {
                throw Missing(body);
            }
        }

        public void SetRewindable(BodyHandle body, bool rewindable)
        {
            if (!RapierNative.fr2_body_set_rewindable(world, Id(body), rewindable))
            {
                throw Missing(body);
            }
        }

        public void AddForce(BodyHandle body, Vector2 force)
        {
            if (!RapierNative.fr2_body_add_force(world, Id(body), force.X, force.Y))
            {
                throw Missing(body);
            }
        }

        public void AddImpulse(BodyHandle body, Vector2 impulse)
        {
            if (!RapierNative.fr2_body_add_impulse(world, Id(body), impulse.X, impulse.Y))
            {
                throw Missing(body);
            }
        }

        public void AddTorque(BodyHandle body, float torque)
        {
            if (!RapierNative.fr2_body_add_torque(world, Id(body), torque))
            {
                throw Missing(body);
            }
        }

        public void AddTorqueImpulse(BodyHandle body, float torqueImpulse)
        {
            if (!RapierNative.fr2_body_add_torque_impulse(world, Id(body), torqueImpulse))
            {
                throw Missing(body);
            }
        }

        public void AddForceAtPoint(BodyHandle body, Vector2 force, Vector2 point)
        {
            if (!RapierNative.fr2_body_add_force_at_point(world, Id(body), force.X, force.Y, point.X, point.Y))
            {
                throw Missing(body);
            }
        }

        public void AddImpulseAtPoint(BodyHandle body, Vector2 impulse, Vector2 point)
        {
            if (!RapierNative.fr2_body_add_impulse_at_point(world, Id(body), impulse.X, impulse.Y, point.X, point.Y))
            {
                throw Missing(body);
            }
        }

        public CharacterMove2D MoveCharacter(BodyHandle body, Vector2 desired, float seconds, in CharacterSettings2D settings) =>
            MoveCharacter(body, desired, seconds, settings, QueryFilter.All);

        public CharacterMove2D MoveCharacter(BodyHandle body, Vector2 desired, float seconds, in CharacterSettings2D settings, in QueryFilter filter)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f)
            {
                throw new ArgumentException("seconds must be finite and not negative", nameof(seconds));
            }

            RapierNative.CharacterSettings2D native = RapierNative.CharacterSettings2D.Of(settings);
            RapierNative.QueryFilter nativeFilter = RapierNative.QueryFilter.Of(filter);
            if (!RapierNative.fr2_character_move(world, Id(body), desired.X, desired.Y, seconds, native, nativeFilter, out RapierNative.CharacterMove2D moved))
            {
                throw new ArgumentException($"body {body.Value} is not a kinematic body of this world with exactly one non-trigger box, circle or capsule collider, or the move is not finite", nameof(body));
            }

            return new CharacterMove2D(moved.Moved, moved.Grounded != 0, moved.Sliding != 0);
        }

        public bool Raycast(Vector2 origin, Vector2 direction, float maxDistance, in QueryFilter filter, out RayHit2D hit)
        {
            RapierNative.QueryFilter native = RapierNative.QueryFilter.Of(filter);
            bool found = RapierNative.fr2_world_raycast(world, origin.X, origin.Y, direction.X, direction.Y, maxDistance, native, out RapierNative.RayHit2D nativeHit);
            hit = found ? HitOf(nativeHit) : default;
            return found;
        }

        public bool ShapeCast(in BodyShape2D shape, Vector2 position, float rotation, Vector2 direction, float maxDistance, in QueryFilter filter, out RayHit2D hit)
        {
            RapierNative.QueryShape2D nativeShape = RapierNative.QueryShape2D.Of(shape);
            RapierNative.QueryFilter native = RapierNative.QueryFilter.Of(filter);
            bool found = RapierNative.fr2_world_shape_cast(world, nativeShape, position.X, position.Y, rotation, direction.X, direction.Y, maxDistance, native, out RapierNative.RayHit2D nativeHit);
            hit = found ? HitOf(nativeHit) : default;
            return found;
        }

        public int Overlap(in BodyShape2D shape, Vector2 position, float rotation, in QueryFilter filter, BodyHandle[] results)
        {
            RapierNative.QueryShape2D nativeShape = RapierNative.QueryShape2D.Of(shape);
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            if (overlapped.Length < results.Length)
            {
                overlapped = new uint[results.Length];
            }

            RapierNative.QueryFilter native = RapierNative.QueryFilter.Of(filter);
            int count = (int)RapierNative.fr2_world_overlap(world, nativeShape, position.X, position.Y, rotation, native, overlapped, (uint)results.Length);
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

            uint count = RapierNative.fr2_world_moved(world);
            if (count == 0)
            {
                return;
            }

            if (moved.Length < count)
            {
                moved = new uint[Math.Max(count, (uint)moved.Length * 2)];
            }

            count = RapierNative.fr2_world_moved_copy(world, moved, count);
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

            uint count = RapierNative.fr2_collider_touching(world, Id(collider.Body), (uint)collider.Index, touched, (uint)touched.Length);
            if (count > touched.Length)
            {
                touched = new RapierNative.ColliderRef[count];
                count = RapierNative.fr2_collider_touching(world, Id(collider.Body), (uint)collider.Index, touched, (uint)touched.Length);
            }

            for (int index = 0; index < count; index++)
            {
                into.Add(RapierCollider.FromNative(touched[index]));
            }
        }

        public int GetContactPoints(BodyHandle body, BodyHandle other, in QueryFilter filter, List<ContactPointInfo2D> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Clear();
            RapierNative.QueryFilter native = RapierNative.QueryFilter.Of(filter);
            uint count = RapierNative.fr2_body_contact_points(world, Id(body), Id(other), native, contactPoints, (uint)contactPoints.Length);
            if (count > contactPoints.Length)
            {
                contactPoints = new RapierNative.ContactPoint2D[count];
                count = RapierNative.fr2_body_contact_points(world, Id(body), Id(other), native, contactPoints, (uint)contactPoints.Length);
            }

            AddContactPoints(count, into);
            return into.Count;
        }

        public void ContactPoints(RapierCollider collider, RapierCollider other, List<ContactPointInfo2D> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            uint count = RapierNative.fr2_collider_contact_points(world, Id(collider.Body), (uint)collider.Index, Id(other.Body), (uint)other.Index, contactPoints, (uint)contactPoints.Length);
            if (count > contactPoints.Length)
            {
                contactPoints = new RapierNative.ContactPoint2D[count];
                count = RapierNative.fr2_collider_contact_points(world, Id(collider.Body), (uint)collider.Index, Id(other.Body), (uint)other.Index, contactPoints, (uint)contactPoints.Length);
            }

            AddContactPoints(count, into);
        }

        public void Dispose()
        {
            if (world != IntPtr.Zero)
            {
                RapierNative.fr2_world_destroy(world);
                world = IntPtr.Zero;
            }
        }

        private static RapierSnapshot Expect(PhysicsSnapshot snapshot) =>
            snapshot as RapierSnapshot ?? throw new ArgumentException("the snapshot was not created by a 2D Rapier world", nameof(snapshot));

        private static RayHit2D HitOf(in RapierNative.RayHit2D hit) =>
            new RayHit2D(new BodyHandle((int)hit.Body), new Vector2(hit.PointX, hit.PointY), new Vector2(hit.NormalX, hit.NormalY), hit.Distance);

        private static ArgumentException Missing(BodyHandle body) => new ArgumentException($"body {body.Value} is not in this world", nameof(body));

        private static uint Id(BodyHandle body) => body.IsValid ? (uint)body.Value : 0u;

        private void AddContactPoints(uint count, List<ContactPointInfo2D> into)
        {
            for (int index = 0; index < count; index++)
            {
                RapierNative.ContactPoint2D point = contactPoints[index];
                into.Add(new ContactPointInfo2D(new Vector2(point.PointX, point.PointY), new Vector2(point.NormalX, point.NormalY), point.Depth));
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
