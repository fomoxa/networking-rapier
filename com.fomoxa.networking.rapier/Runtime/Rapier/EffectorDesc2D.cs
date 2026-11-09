using System.Numerics;

namespace Fomoxa.Networking.Rapier
{
    public readonly struct EffectorDesc2D
    {
        private readonly Fields fields;

        private EffectorDesc2D(in Fields fields)
        {
            this.fields = fields;
        }

        public EffectorKind2D Kind => fields.Kind;

        public Vector2 Origin => fields.Origin;

        public float Rotation => fields.Rotation;

        public uint? ColliderMask => fields.ColliderMask;

        public bool OneWay => fields.OneWay;

        public bool OneWayGrouping => fields.OneWayGrouping;

        public float RotationalOffset => fields.RotationalOffset;

        public float SurfaceArc => fields.SurfaceArc;

        public bool SideFriction => fields.SideFriction;

        public bool SideBounce => fields.SideBounce;

        public float SideArc => fields.SideArc;

        public float Speed => fields.Speed;

        public float ForceScale => fields.ForceScale;

        public bool ContactForce => fields.ContactForce;

        public bool Friction => fields.Friction;

        public bool Bounce => fields.Bounce;

        public float ForceAngle => fields.ForceAngle;

        public bool GlobalAngle => fields.GlobalAngle;

        public float ForceMagnitude => fields.ForceMagnitude;

        public EffectorSelection ForceSource => fields.ForceSource;

        public EffectorSelection ForceTarget => fields.ForceTarget;

        public EffectorForceMode ForceMode => fields.ForceMode;

        public float DistanceScale => fields.DistanceScale;

        public float LinearDamping => fields.LinearDamping;

        public float AngularDamping => fields.AngularDamping;

        public float Density => fields.Density;

        public float SurfaceLevel => fields.SurfaceLevel;

        public float FlowAngle => fields.FlowAngle;

        public float FlowMagnitude => fields.FlowMagnitude;

        public static EffectorDesc2D Platform(bool oneWay, bool oneWayGrouping, float rotationalOffset, float surfaceArc, bool sideFriction, bool sideBounce, float sideArc) =>
            new EffectorDesc2D(new Fields
            {
                Kind = EffectorKind2D.Platform,
                OneWay = oneWay,
                OneWayGrouping = oneWayGrouping,
                RotationalOffset = rotationalOffset,
                SurfaceArc = surfaceArc,
                SideFriction = sideFriction,
                SideBounce = sideBounce,
                SideArc = sideArc,
            });

        public static EffectorDesc2D Surface(float speed, float forceScale, bool contactForce, bool friction, bool bounce) =>
            new EffectorDesc2D(new Fields
            {
                Kind = EffectorKind2D.Surface,
                Speed = speed,
                ForceScale = forceScale,
                ContactForce = contactForce,
                Friction = friction,
                Bounce = bounce,
            });

        public static EffectorDesc2D Area(float forceAngle, bool globalAngle, float forceMagnitude, EffectorSelection forceTarget, float linearDamping, float angularDamping) =>
            new EffectorDesc2D(new Fields
            {
                Kind = EffectorKind2D.Area,
                ForceAngle = forceAngle,
                GlobalAngle = globalAngle,
                ForceMagnitude = forceMagnitude,
                ForceTarget = forceTarget,
                LinearDamping = linearDamping,
                AngularDamping = angularDamping,
            });

        public static EffectorDesc2D Buoyancy(float surfaceLevel, float density, float linearDamping, float angularDamping, float flowAngle, float flowMagnitude) =>
            new EffectorDesc2D(new Fields
            {
                Kind = EffectorKind2D.Buoyancy,
                SurfaceLevel = surfaceLevel,
                Density = density,
                LinearDamping = linearDamping,
                AngularDamping = angularDamping,
                FlowAngle = flowAngle,
                FlowMagnitude = flowMagnitude,
            });

        public static EffectorDesc2D Point(float forceMagnitude, float distanceScale, EffectorSelection forceSource, EffectorSelection forceTarget, EffectorForceMode forceMode, float linearDamping, float angularDamping) =>
            new EffectorDesc2D(new Fields
            {
                Kind = EffectorKind2D.Point,
                ForceMagnitude = forceMagnitude,
                DistanceScale = distanceScale,
                ForceSource = forceSource,
                ForceTarget = forceTarget,
                ForceMode = forceMode,
                LinearDamping = linearDamping,
                AngularDamping = angularDamping,
            });

        public EffectorDesc2D WithFrame(Vector2 origin, float rotation)
        {
            Fields copy = fields;
            copy.Origin = origin;
            copy.Rotation = rotation;
            return new EffectorDesc2D(copy);
        }

        public EffectorDesc2D WithColliderMask(uint? mask)
        {
            Fields copy = fields;
            copy.ColliderMask = mask;
            return new EffectorDesc2D(copy);
        }

        private struct Fields
        {
            public EffectorKind2D Kind;
            public Vector2 Origin;
            public float Rotation;
            public uint? ColliderMask;
            public bool OneWay;
            public bool OneWayGrouping;
            public float RotationalOffset;
            public float SurfaceArc;
            public bool SideFriction;
            public bool SideBounce;
            public float SideArc;
            public float Speed;
            public float ForceScale;
            public bool ContactForce;
            public bool Friction;
            public bool Bounce;
            public float ForceAngle;
            public bool GlobalAngle;
            public float ForceMagnitude;
            public EffectorSelection ForceSource;
            public EffectorSelection ForceTarget;
            public EffectorForceMode ForceMode;
            public float DistanceScale;
            public float LinearDamping;
            public float AngularDamping;
            public float Density;
            public float SurfaceLevel;
            public float FlowAngle;
            public float FlowMagnitude;
        }
    }
}
