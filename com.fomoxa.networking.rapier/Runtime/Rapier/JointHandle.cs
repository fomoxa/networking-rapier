using System;

namespace Fomoxa.Networking.Rapier
{
    public readonly struct JointHandle : IEquatable<JointHandle>
    {
        public JointHandle(int value)
        {
            Value = value;
        }

        public int Value { get; }

        public bool IsValid => Value != 0;

        public bool Equals(JointHandle other) => Value == other.Value;

        public override bool Equals(object obj) => obj is JointHandle other && Equals(other);

        public override int GetHashCode() => Value;
    }
}
