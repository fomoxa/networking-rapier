using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Rapier
{
    internal sealed class RapierColliders : IDisposable
    {
        private readonly List<GCHandle> pinned = new List<GCHandle>();

        public RapierColliders(IReadOnlyList<ColliderDesc> colliders)
        {
            Native = new RapierNative.Collider[colliders.Count];
            for (int index = 0; index < colliders.Count; index++)
            {
                Native[index] = Convert(colliders[index]);
            }
        }

        public RapierNative.Collider[] Native { get; }

        public void Dispose()
        {
            foreach (GCHandle handle in pinned)
            {
                handle.Free();
            }

            pinned.Clear();
        }

        private RapierNative.Collider Convert(in ColliderDesc collider)
        {
            BodyShape shape = collider.Shape;
            ColliderMaterial material = collider.Material;
            var native = new RapierNative.Collider
            {
                Shape = (uint)shape.Kind,
                PositionX = collider.Position.X,
                PositionY = collider.Position.Y,
                PositionZ = collider.Position.Z,
                RotationX = collider.Rotation.X,
                RotationY = collider.Rotation.Y,
                RotationZ = collider.Rotation.Z,
                RotationW = collider.Rotation.W,
                HalfExtentsX = shape.HalfExtents.X,
                HalfExtentsY = shape.HalfExtents.Y,
                HalfExtentsZ = shape.HalfExtents.Z,
                Radius = shape.Radius,
                HalfHeight = shape.HalfHeight,
                Friction = material.Friction,
                Restitution = material.Restitution,
                FrictionCombine = (uint)material.FrictionCombine,
                RestitutionCombine = (uint)material.RestitutionCombine,
                Layer = (uint)collider.Layer,
                IsTrigger = collider.IsTrigger ? 1u : 0u,
            };
            IReadOnlyList<Vector3> points = shape.Points;
            if (points.Count > 0)
            {
                var flat = new float[points.Count * 3];
                for (int index = 0; index < points.Count; index++)
                {
                    flat[index * 3] = points[index].X;
                    flat[index * 3 + 1] = points[index].Y;
                    flat[index * 3 + 2] = points[index].Z;
                }

                native.Points = Pin(flat);
                native.PointCount = (uint)points.Count;
            }

            IReadOnlyList<int> triangles = shape.Triangles;
            if (triangles.Count > 0)
            {
                var indices = new uint[triangles.Count];
                for (int index = 0; index < indices.Length; index++)
                {
                    indices[index] = (uint)triangles[index];
                }

                native.Triangles = Pin(indices);
                native.TriangleIndexCount = (uint)indices.Length;
            }

            return native;
        }

        private IntPtr Pin(object array)
        {
            GCHandle handle = GCHandle.Alloc(array, GCHandleType.Pinned);
            pinned.Add(handle);
            return handle.AddrOfPinnedObject();
        }
    }
}
