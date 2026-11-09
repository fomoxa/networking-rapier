using System;

namespace Fomoxa.Networking.Rapier
{
    public enum JointKind
    {
        Fixed,
        Revolute,
        Prismatic,
        Spherical,
        Rope,
        Spring,
    }

    public readonly struct JointLimit
    {
        public JointLimit(float min, float max)
        {
            if (float.IsNaN(min) || float.IsNaN(max) || min > max)
            {
                throw new ArgumentException("the limit needs min <= max", nameof(min));
            }

            Min = min;
            Max = max;
            IsEnabled = true;
        }

        public float Min { get; }

        public float Max { get; }

        public bool IsEnabled { get; }
    }

    public readonly struct JointMotorDesc
    {
        public JointMotorDesc(float targetPosition, float targetVelocity, float stiffness, float damping, float maxForce)
        {
            if (stiffness < 0f || damping < 0f || maxForce < 0f)
            {
                throw new ArgumentException("stiffness, damping and maxForce must not be negative");
            }

            TargetPosition = targetPosition;
            TargetVelocity = targetVelocity;
            Stiffness = stiffness;
            Damping = damping;
            MaxForce = maxForce;
            IsEnabled = true;
        }

        public float TargetPosition { get; }

        public float TargetVelocity { get; }

        public float Stiffness { get; }

        public float Damping { get; }

        public float MaxForce { get; }

        public bool IsEnabled { get; }

        public static JointMotorDesc Velocity(float targetVelocity, float damping, float maxForce) => new JointMotorDesc(0f, targetVelocity, 0f, damping, maxForce);

        public static JointMotorDesc Position(float targetPosition, float stiffness, float damping, float maxForce) => new JointMotorDesc(targetPosition, 0f, stiffness, damping, maxForce);
    }

    public readonly struct JointSoftness
    {
        public JointSoftness(float frequency, float dampingRatio)
        {
            if (!(frequency > 0f) || dampingRatio < 0f)
            {
                throw new ArgumentException("the frequency must be positive and the damping ratio not negative");
            }

            Frequency = frequency;
            DampingRatio = dampingRatio;
        }

        public float Frequency { get; }

        public float DampingRatio { get; }

        public bool IsSet => Frequency > 0f;
    }
}
