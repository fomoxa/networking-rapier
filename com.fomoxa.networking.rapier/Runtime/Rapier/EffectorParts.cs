using System;

namespace Fomoxa.Networking.Rapier
{
    public enum EffectorKind2D
    {
        Platform = 1,
        Surface = 2,
        Area = 3,
        Buoyancy = 4,
        Point = 5,
    }

    public enum EffectorSelection
    {
        Rigidbody = 0,
        Collider = 1,
    }

    public enum EffectorForceMode
    {
        Constant = 0,
        InverseLinear = 1,
        InverseSquared = 2,
    }

    public readonly struct EffectorHandle : IEquatable<EffectorHandle>
    {
        public EffectorHandle(int value)
        {
            Value = value;
        }

        public int Value { get; }

        public bool IsValid => Value != 0;

        public bool Equals(EffectorHandle other) => Value == other.Value;

        public override bool Equals(object obj) => obj is EffectorHandle other && Equals(other);

        public override int GetHashCode() => Value;
    }
}
