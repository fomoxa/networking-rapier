using System;
using System.Numerics;

namespace Fomoxa.Networking.Rapier
{
    public readonly struct CharacterLength
    {
        private CharacterLength(float value, bool isRelative)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            {
                throw new ArgumentException("a character length must be finite and not negative", nameof(value));
            }

            Value = value;
            IsRelative = isRelative;
        }

        public float Value { get; }

        public bool IsRelative { get; }

        public static CharacterLength Absolute(float meters) => new CharacterLength(meters, false);

        public static CharacterLength Relative(float fraction) => new CharacterLength(fraction, true);
    }

    public readonly struct CharacterAutostep
    {
        public CharacterAutostep(CharacterLength maxHeight, CharacterLength minWidth, bool includeDynamicBodies)
        {
            MaxHeight = maxHeight;
            MinWidth = minWidth;
            IncludeDynamicBodies = includeDynamicBodies;
        }

        public CharacterLength MaxHeight { get; }

        public CharacterLength MinWidth { get; }

        public bool IncludeDynamicBodies { get; }

        public static CharacterAutostep Default => new CharacterAutostep(CharacterLength.Relative(0.25f), CharacterLength.Relative(0.5f), true);
    }

    public readonly struct CharacterMove
    {
        public CharacterMove(Vector3 moved, bool grounded, bool sliding)
        {
            Moved = moved;
            Grounded = grounded;
            Sliding = sliding;
        }

        public Vector3 Moved { get; }

        public bool Grounded { get; }

        public bool Sliding { get; }
    }

    public readonly struct CharacterMove2D
    {
        public CharacterMove2D(Vector2 moved, bool grounded, bool sliding)
        {
            Moved = moved;
            Grounded = grounded;
            Sliding = sliding;
        }

        public Vector2 Moved { get; }

        public bool Grounded { get; }

        public bool Sliding { get; }
    }

    internal static class CharacterChecks
    {
        public static float Angle(float radians, string name)
        {
            if (float.IsNaN(radians) || float.IsInfinity(radians) || radians < 0f)
            {
                throw new ArgumentException("an angle must be finite and not negative", name);
            }

            return radians;
        }

        public static float Amount(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            {
                throw new ArgumentException("the value must be finite and not negative", name);
            }

            return value;
        }

        public static CharacterLength Offset(CharacterLength offset)
        {
            if (offset.Value <= 0f)
            {
                throw new ArgumentException("the offset must be greater than zero", nameof(offset));
            }

            return offset;
        }
    }
}
