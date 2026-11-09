using System;
using System.Numerics;
using Fomoxa.Networking.Messaging;

namespace Fomoxa.Networking.Rapier
{
    public static class RapierEffectors
    {
        public static EffectorDesc2D FromFile(SceneFileEffector2D file)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            EffectorDesc2D desc;
            switch ((EffectorKind2D)file.Kind)
            {
                case EffectorKind2D.Platform:
                    desc = EffectorDesc2D.Platform(file.OneWay, file.OneWayGrouping, file.RotationalOffset, file.SurfaceArc, file.SideFriction, file.SideBounce, file.SideArc);
                    break;
                case EffectorKind2D.Surface:
                    desc = EffectorDesc2D.Surface(file.Speed, file.ForceScale, file.ContactForce, file.Friction, file.Bounce);
                    break;
                case EffectorKind2D.Area:
                    desc = EffectorDesc2D.Area(file.ForceAngle, file.GlobalAngle, file.ForceMagnitude, Selection(file.ForceTarget), file.LinearDamping, file.AngularDamping);
                    break;
                case EffectorKind2D.Buoyancy:
                    desc = EffectorDesc2D.Buoyancy(file.SurfaceLevel, file.Density, file.LinearDamping, file.AngularDamping, file.FlowAngle, file.FlowMagnitude);
                    break;
                case EffectorKind2D.Point:
                    if (file.ForceMode > (byte)EffectorForceMode.InverseSquared)
                    {
                        throw new ArgumentException($"effector force mode {file.ForceMode} is unknown", nameof(file));
                    }

                    desc = EffectorDesc2D.Point(file.ForceMagnitude, file.DistanceScale, Selection(file.ForceSource), Selection(file.ForceTarget), (EffectorForceMode)file.ForceMode, file.LinearDamping, file.AngularDamping);
                    break;
                default:
                    throw new ArgumentException($"effector kind {file.Kind} is unknown", nameof(file));
            }

            return desc.WithFrame(new Vector2(file.OriginX, file.OriginY), file.Rotation).WithColliderMask(file.UseColliderMask ? file.ColliderMask : (uint?)null);
        }

        private static EffectorSelection Selection(byte value) =>
            value <= (byte)EffectorSelection.Collider ? (EffectorSelection)value : throw new ArgumentException($"effector selection {value} is unknown", nameof(value));
    }
}
