using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Rapier
{
    internal static class RapierNative
    {
#if (UNITY_IOS || UNITY_WEBGL) && !UNITY_EDITOR
        private const string Library = "__Internal";
#else
        private const string Library = "fomoxa_rapier";
#endif

        [StructLayout(LayoutKind.Sequential)]
        internal struct Collider
        {
            public uint Shape;
            public float PositionX;
            public float PositionY;
            public float PositionZ;
            public float RotationX;
            public float RotationY;
            public float RotationZ;
            public float RotationW;
            public float HalfExtentsX;
            public float HalfExtentsY;
            public float HalfExtentsZ;
            public float Radius;
            public float HalfHeight;
            public IntPtr Points;
            public uint PointCount;
            public IntPtr Triangles;
            public uint TriangleIndexCount;
            public float Friction;
            public float Restitution;
            public uint FrictionCombine;
            public uint RestitutionCombine;
            public uint Layer;
            public uint IsTrigger;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BodyState
        {
            public float PositionX;
            public float PositionY;
            public float PositionZ;
            public float RotationX;
            public float RotationY;
            public float RotationZ;
            public float RotationW;
            public float VelocityX;
            public float VelocityY;
            public float VelocityZ;
            public float AngularVelocityX;
            public float AngularVelocityY;
            public float AngularVelocityZ;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ColliderRef
        {
            public uint Body;
            public uint Index;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct QueryFilter
        {
            public uint LayerMask;
            public uint ExcludeBody;
            public uint IncludeTriggers;

            internal static QueryFilter Of(in Simulation.QueryFilter filter) => new QueryFilter
            {
                LayerMask = unchecked((uint)filter.LayerMask),
                ExcludeBody = unchecked((uint)filter.ExcludeBody.Value),
                IncludeTriggers = filter.IncludeTriggers ? 1u : 0u,
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct QueryShape
        {
            public uint Shape;
            public float HalfExtentsX;
            public float HalfExtentsY;
            public float HalfExtentsZ;
            public float Radius;
            public float HalfHeight;

            internal static QueryShape Of(in BodyShape shape)
            {
                if (shape.Kind != ShapeKind.Box && shape.Kind != ShapeKind.Sphere && shape.Kind != ShapeKind.Capsule)
                {
                    throw new ArgumentException($"a {shape.Kind} cannot be a query shape; use a box, a sphere or a capsule", nameof(shape));
                }

                return new QueryShape
                {
                    Shape = (uint)shape.Kind,
                    HalfExtentsX = shape.HalfExtents.X,
                    HalfExtentsY = shape.HalfExtents.Y,
                    HalfExtentsZ = shape.HalfExtents.Z,
                    Radius = shape.Radius,
                    HalfHeight = shape.HalfHeight,
                };
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct QueryShape2D
        {
            public uint Shape;
            public float HalfExtentsX;
            public float HalfExtentsY;
            public float Radius;
            public float HalfHeight;

            internal static QueryShape2D Of(in BodyShape2D shape)
            {
                if (shape.Kind != ShapeKind2D.Box && shape.Kind != ShapeKind2D.Circle && shape.Kind != ShapeKind2D.Capsule)
                {
                    throw new ArgumentException($"a {shape.Kind} cannot be a query shape; use a box, a circle or a capsule", nameof(shape));
                }

                return new QueryShape2D
                {
                    Shape = (uint)shape.Kind,
                    HalfExtentsX = shape.HalfExtents.X,
                    HalfExtentsY = shape.HalfExtents.Y,
                    Radius = shape.Radius,
                    HalfHeight = shape.HalfHeight,
                };
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ContactPoint
        {
            public float PointX;
            public float PointY;
            public float PointZ;
            public float NormalX;
            public float NormalY;
            public float NormalZ;
            public float Depth;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ContactPoint2D
        {
            public float PointX;
            public float PointY;
            public float NormalX;
            public float NormalY;
            public float Depth;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RayHit
        {
            public uint Body;
            public float PointX;
            public float PointY;
            public float PointZ;
            public float NormalX;
            public float NormalY;
            public float NormalZ;
            public float Distance;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct JointLimit
        {
            public uint Enabled;
            public float Min;
            public float Max;

            public static JointLimit Of(in Rapier.JointLimit limit) => new JointLimit { Enabled = limit.IsEnabled ? 1u : 0u, Min = limit.Min, Max = limit.Max };
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct JointMotor
        {
            public uint Enabled;
            public float TargetPosition;
            public float TargetVelocity;
            public float Stiffness;
            public float Damping;
            public float MaxForce;

            public static JointMotor Of(in JointMotorDesc motor) => new JointMotor
            {
                Enabled = motor.IsEnabled ? 1u : 0u,
                TargetPosition = motor.TargetPosition,
                TargetVelocity = motor.TargetVelocity,
                Stiffness = motor.Stiffness,
                Damping = motor.Damping,
                MaxForce = motor.MaxForce,
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Joint
        {
            public uint Kind;
            public float Position1X;
            public float Position1Y;
            public float Position1Z;
            public float Rotation1X;
            public float Rotation1Y;
            public float Rotation1Z;
            public float Rotation1W;
            public float Position2X;
            public float Position2Y;
            public float Position2Z;
            public float Rotation2X;
            public float Rotation2Y;
            public float Rotation2Z;
            public float Rotation2W;
            public JointLimit Limit;
            public JointLimit SwingLimitY;
            public JointLimit SwingLimitZ;
            public JointMotor Motor;
            public float Length;
            public float Stiffness;
            public float Damping;
            public float SoftnessFrequency;
            public float SoftnessDampingRatio;
            public uint ContactsEnabled;

            public static Joint Of(in JointDesc desc) => new Joint
            {
                Kind = (uint)desc.Kind,
                Position1X = desc.AnchorA.X,
                Position1Y = desc.AnchorA.Y,
                Position1Z = desc.AnchorA.Z,
                Rotation1X = desc.FrameA.X,
                Rotation1Y = desc.FrameA.Y,
                Rotation1Z = desc.FrameA.Z,
                Rotation1W = desc.FrameA.W,
                Position2X = desc.AnchorB.X,
                Position2Y = desc.AnchorB.Y,
                Position2Z = desc.AnchorB.Z,
                Rotation2X = desc.FrameB.X,
                Rotation2Y = desc.FrameB.Y,
                Rotation2Z = desc.FrameB.Z,
                Rotation2W = desc.FrameB.W,
                Limit = JointLimit.Of(desc.Limit),
                SwingLimitY = JointLimit.Of(desc.SwingLimitY),
                SwingLimitZ = JointLimit.Of(desc.SwingLimitZ),
                Motor = JointMotor.Of(desc.Motor),
                Length = desc.Length,
                Stiffness = desc.Stiffness,
                Damping = desc.Damping,
                SoftnessFrequency = desc.Softness.Frequency,
                SoftnessDampingRatio = desc.Softness.DampingRatio,
                ContactsEnabled = desc.ContactsEnabled ? 1u : 0u,
            };
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Joint2D
        {
            public uint Kind;
            public float Position1X;
            public float Position1Y;
            public float Rotation1;
            public float Position2X;
            public float Position2Y;
            public float Rotation2;
            public JointLimit Limit;
            public JointMotor Motor;
            public float Length;
            public float Stiffness;
            public float Damping;
            public float SoftnessFrequency;
            public float SoftnessDampingRatio;
            public uint ContactsEnabled;

            public static Joint2D Of(in JointDesc2D desc) => new Joint2D
            {
                Kind = (uint)desc.Kind,
                Position1X = desc.AnchorA.X,
                Position1Y = desc.AnchorA.Y,
                Rotation1 = desc.FrameA,
                Position2X = desc.AnchorB.X,
                Position2Y = desc.AnchorB.Y,
                Rotation2 = desc.FrameB,
                Limit = JointLimit.Of(desc.Limit),
                Motor = JointMotor.Of(desc.Motor),
                Length = desc.Length,
                Stiffness = desc.Stiffness,
                Damping = desc.Damping,
                SoftnessFrequency = desc.Softness.Frequency,
                SoftnessDampingRatio = desc.Softness.DampingRatio,
                ContactsEnabled = desc.ContactsEnabled ? 1u : 0u,
            };
        }

        [DllImport(Library)]
        internal static extern IntPtr fr_world_create(float gravityX, float gravityY, float gravityZ);

        [DllImport(Library)]
        internal static extern void fr_world_destroy(IntPtr world);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_world_set_layers(IntPtr world, uint[] masks);

        [DllImport(Library)]
        internal static extern void fr_world_step(IntPtr world, float seconds);

        [DllImport(Library)]
        internal static extern uint fr_world_add_static(IntPtr world, Collider[] colliders, uint count, out uint first);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_world_remove_static(IntPtr world, uint group);

        [DllImport(Library)]
        internal static extern uint fr_body_create(IntPtr world, uint kind, float[] position, float[] rotation, float mass, uint locks, float gravityScale, float linearDamping, float angularDamping, [MarshalAs(UnmanagedType.U1)] bool continuousCollision, Collider[] colliders, uint count);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_body_remove(IntPtr world, uint body);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_body_contains(IntPtr world, uint body);

        [DllImport(Library)]
        internal static extern uint fr_joint_create(IntPtr world, uint body1, uint body2, in Joint joint);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_joint_remove(IntPtr world, uint joint);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_joint_contains(IntPtr world, uint joint);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_joint_set(IntPtr world, uint joint, in Joint recipe);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_body_get(IntPtr world, uint body, out BodyState state);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_body_set(IntPtr world, uint body, in BodyState state);

        [DllImport(Library)]
        internal static extern int fr_body_kind(IntPtr world, uint body);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_body_set_kind(IntPtr world, uint body, uint kind);

        [DllImport(Library)]
        internal static extern float fr_body_mass(IntPtr world, uint body);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_body_set_rewindable(IntPtr world, uint body, [MarshalAs(UnmanagedType.U1)] bool rewindable);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_body_add_force(IntPtr world, uint body, float x, float y, float z);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_body_add_impulse(IntPtr world, uint body, float x, float y, float z);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_body_add_torque(IntPtr world, uint body, float x, float y, float z);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_body_add_torque_impulse(IntPtr world, uint body, float x, float y, float z);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_body_add_force_at_point(IntPtr world, uint body, float forceX, float forceY, float forceZ, float pointX, float pointY, float pointZ);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_body_add_impulse_at_point(IntPtr world, uint body, float impulseX, float impulseY, float impulseZ, float pointX, float pointY, float pointZ);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_world_raycast(IntPtr world, in Vector3 origin, in Vector3 direction, float maxDistance, in QueryFilter filter, out RayHit hit);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_world_shape_cast(IntPtr world, in QueryShape shape, in Vector3 position, in Quaternion rotation, in Vector3 direction, float maxDistance, in QueryFilter filter, out RayHit hit);

        [DllImport(Library)]
        internal static extern uint fr_collider_touching(IntPtr world, uint body, uint index, [Out] ColliderRef[] touching, uint capacity);

        [DllImport(Library)]
        internal static extern uint fr_body_contact_points(IntPtr world, uint body, uint other, in QueryFilter filter, [Out] ContactPoint[] points, uint capacity);

        [DllImport(Library)]
        internal static extern uint fr_collider_contact_points(IntPtr world, uint body, uint index, uint otherBody, uint otherIndex, [Out] ContactPoint[] points, uint capacity);

        [DllImport(Library)]
        internal static extern uint fr_world_overlap(IntPtr world, in QueryShape shape, in Vector3 position, in Quaternion rotation, in QueryFilter filter, uint[] bodies, uint capacity);

        [DllImport(Library)]
        internal static extern uint fr_world_moved(IntPtr world);

        [DllImport(Library)]
        internal static extern uint fr_world_moved_copy(IntPtr world, uint[] bodies, uint capacity);

        [DllImport(Library)]
        internal static extern uint fr_world_snapshot(IntPtr world);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_world_snapshot_copy(IntPtr world, byte[] bytes, uint length);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_world_load(IntPtr world, byte[] bytes, uint length);

        [DllImport(Library)]
        internal static extern ulong fr_world_hash(IntPtr world);

        [StructLayout(LayoutKind.Sequential)]
        internal struct Collider2D
        {
            public uint Shape;
            public float PositionX;
            public float PositionY;
            public float Rotation;
            public float HalfExtentsX;
            public float HalfExtentsY;
            public float Radius;
            public float HalfHeight;
            public IntPtr Points;
            public uint PointCount;
            public float Friction;
            public float Restitution;
            public uint FrictionCombine;
            public uint RestitutionCombine;
            public uint Layer;
            public uint IsTrigger;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BodyState2D
        {
            public float PositionX;
            public float PositionY;
            public float Rotation;
            public float VelocityX;
            public float VelocityY;
            public float AngularVelocity;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RayHit2D
        {
            public uint Body;
            public float PointX;
            public float PointY;
            public float NormalX;
            public float NormalY;
            public float Distance;
        }

        [DllImport(Library)]
        internal static extern IntPtr fr2_world_create(float gravityX, float gravityY);

        [DllImport(Library)]
        internal static extern void fr2_world_destroy(IntPtr world);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_world_set_layers(IntPtr world, uint[] masks);

        [DllImport(Library)]
        internal static extern void fr2_world_step(IntPtr world, float seconds);

        [DllImport(Library)]
        internal static extern uint fr2_world_add_static(IntPtr world, Collider2D[] colliders, uint count, out uint first);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_world_remove_static(IntPtr world, uint group);

        [DllImport(Library)]
        internal static extern uint fr2_body_create(IntPtr world, uint kind, float x, float y, float rotation, float mass, uint locks, float gravityScale, float linearDamping, float angularDamping, [MarshalAs(UnmanagedType.U1)] bool continuousCollision, Collider2D[] colliders, uint count);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_body_remove(IntPtr world, uint body);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_body_contains(IntPtr world, uint body);

        [DllImport(Library)]
        internal static extern uint fr2_joint_create(IntPtr world, uint body1, uint body2, in Joint2D joint);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_joint_remove(IntPtr world, uint joint);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_joint_contains(IntPtr world, uint joint);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_joint_set(IntPtr world, uint joint, in Joint2D recipe);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_body_get(IntPtr world, uint body, out BodyState2D state);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_body_set(IntPtr world, uint body, in BodyState2D state);

        [DllImport(Library)]
        internal static extern int fr2_body_kind(IntPtr world, uint body);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_body_set_kind(IntPtr world, uint body, uint kind);

        [DllImport(Library)]
        internal static extern float fr2_body_mass(IntPtr world, uint body);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_body_set_rewindable(IntPtr world, uint body, [MarshalAs(UnmanagedType.U1)] bool rewindable);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_body_add_force(IntPtr world, uint body, float x, float y);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_body_add_impulse(IntPtr world, uint body, float x, float y);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_body_add_torque(IntPtr world, uint body, float torque);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_body_add_torque_impulse(IntPtr world, uint body, float torqueImpulse);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_body_add_force_at_point(IntPtr world, uint body, float forceX, float forceY, float pointX, float pointY);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_body_add_impulse_at_point(IntPtr world, uint body, float impulseX, float impulseY, float pointX, float pointY);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_world_raycast(IntPtr world, float originX, float originY, float directionX, float directionY, float maxDistance, in QueryFilter filter, out RayHit2D hit);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_world_shape_cast(IntPtr world, in QueryShape2D shape, float x, float y, float rotation, float directionX, float directionY, float maxDistance, in QueryFilter filter, out RayHit2D hit);

        [DllImport(Library)]
        internal static extern uint fr2_collider_touching(IntPtr world, uint body, uint index, [Out] ColliderRef[] touching, uint capacity);

        [DllImport(Library)]
        internal static extern uint fr2_body_contact_points(IntPtr world, uint body, uint other, in QueryFilter filter, [Out] ContactPoint2D[] points, uint capacity);

        [DllImport(Library)]
        internal static extern uint fr2_collider_contact_points(IntPtr world, uint body, uint index, uint otherBody, uint otherIndex, [Out] ContactPoint2D[] points, uint capacity);

        [DllImport(Library)]
        internal static extern uint fr2_world_overlap(IntPtr world, in QueryShape2D shape, float x, float y, float rotation, in QueryFilter filter, uint[] bodies, uint capacity);

        [DllImport(Library)]
        internal static extern uint fr2_world_moved(IntPtr world);

        [DllImport(Library)]
        internal static extern uint fr2_world_moved_copy(IntPtr world, uint[] bodies, uint capacity);

        [DllImport(Library)]
        internal static extern uint fr2_world_snapshot(IntPtr world);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_world_snapshot_copy(IntPtr world, byte[] bytes, uint length);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_world_load(IntPtr world, byte[] bytes, uint length);

        [DllImport(Library)]
        internal static extern ulong fr2_world_hash(IntPtr world);
    }
}
