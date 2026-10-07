using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Rapier
{
    internal sealed class RapierColliders2D : IDisposable
    {
        private readonly List<GCHandle> pinned = new List<GCHandle>();

        public RapierColliders2D(IReadOnlyList<ColliderDesc2D> colliders)
        {
            Native = new RapierNative.Collider2D[colliders.Count];
            for (int index = 0; index < colliders.Count; index++)
            {
                Native[index] = Convert(colliders[index]);
            }
        }

        public RapierNative.Collider2D[] Native { get; }

        public void Dispose()
        {
            foreach (GCHandle handle in pinned)
            {
                handle.Free();
            }

            pinned.Clear();
        }

        private RapierNative.Collider2D Convert(in ColliderDesc2D collider)
        {
            BodyShape2D shape = collider.Shape;
            ColliderMaterial material = collider.Material;
            var native = new RapierNative.Collider2D
            {
                Shape = (uint)shape.Kind,
                PositionX = collider.Position.X,
                PositionY = collider.Position.Y,
                Rotation = collider.Rotation,
                HalfExtentsX = shape.HalfExtents.X,
                HalfExtentsY = shape.HalfExtents.Y,
                Radius = shape.Radius,
                HalfHeight = shape.HalfHeight,
                Friction = material.Friction,
                Restitution = material.Restitution,
                FrictionCombine = (uint)material.FrictionCombine,
                RestitutionCombine = (uint)material.RestitutionCombine,
                Layer = (uint)collider.Layer,
                IsTrigger = collider.IsTrigger ? 1u : 0u,
            };
            IReadOnlyList<Vector2> points = shape.Points;
            if (points.Count > 0)
            {
                var flat = new float[points.Count * 2];
                for (int index = 0; index < points.Count; index++)
                {
                    flat[index * 2] = points[index].X;
                    flat[index * 2 + 1] = points[index].Y;
                }

                GCHandle handle = GCHandle.Alloc(flat, GCHandleType.Pinned);
                pinned.Add(handle);
                native.Points = handle.AddrOfPinnedObject();
                native.PointCount = (uint)points.Count;
            }

            return native;
        }
    }
}
