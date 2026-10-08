using System;
using System.Runtime.InteropServices;

namespace Fomoxa.Networking.Rapier
{
    internal static class RapierNative
    {
        private const string Library = "fomoxa_rapier";

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
        internal static extern uint fr_body_create(IntPtr world, uint kind, float[] position, float[] rotation, float mass, uint locks, float gravityScale, float linearDamping, float angularDamping, Collider[] colliders, uint count);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_body_remove(IntPtr world, uint body);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr_body_contains(IntPtr world, uint body);

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
        internal static extern bool fr_world_raycast(IntPtr world, float[] origin, float[] direction, float maxDistance, out RayHit hit);

        [DllImport(Library)]
        internal static extern uint fr_collider_touching(IntPtr world, uint body, uint index, [Out] ColliderRef[] touching, uint capacity);

        [DllImport(Library)]
        internal static extern uint fr_world_overlap(IntPtr world, float x, float y, float z, float radius, uint[] bodies, uint capacity);

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
        internal static extern uint fr2_body_create(IntPtr world, uint kind, float x, float y, float rotation, float mass, uint locks, float gravityScale, float linearDamping, float angularDamping, Collider2D[] colliders, uint count);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_body_remove(IntPtr world, uint body);

        [DllImport(Library)]
        [return: MarshalAs(UnmanagedType.U1)]
        internal static extern bool fr2_body_contains(IntPtr world, uint body);

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
        internal static extern bool fr2_world_raycast(IntPtr world, float originX, float originY, float directionX, float directionY, float maxDistance, out RayHit2D hit);

        [DllImport(Library)]
        internal static extern uint fr2_collider_touching(IntPtr world, uint body, uint index, [Out] ColliderRef[] touching, uint capacity);

        [DllImport(Library)]
        internal static extern uint fr2_world_overlap(IntPtr world, float x, float y, float radius, uint[] bodies, uint capacity);

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
