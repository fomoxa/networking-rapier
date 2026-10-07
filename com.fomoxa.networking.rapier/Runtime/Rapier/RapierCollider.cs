using System;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Networking.Rapier
{
    public readonly struct RapierCollider : IEquatable<RapierCollider>
    {
        private RapierCollider(BodyHandle body, int index)
        {
            Body = body;
            Index = index;
        }

        public BodyHandle Body { get; }

        public int Index { get; }

        public bool IsStatic => !Body.IsValid;

        public static RapierCollider Static(int index)
        {
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return new RapierCollider(default, index);
        }

        public static RapierCollider OfBody(BodyHandle body, int index)
        {
            if (!body.IsValid)
            {
                throw new ArgumentException("the body handle is not valid", nameof(body));
            }

            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return new RapierCollider(body, index);
        }

        public bool Equals(RapierCollider other) => Body.Equals(other.Body) && Index == other.Index;

        public override bool Equals(object obj) => obj is RapierCollider other && Equals(other);

        public override int GetHashCode() => (Body.Value * 397) ^ Index;

        internal static RapierCollider FromNative(RapierNative.ColliderRef native) => new RapierCollider(new BodyHandle((int)native.Body), (int)native.Index);
    }
}
