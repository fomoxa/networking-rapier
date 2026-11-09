using System;
using System.Numerics;

namespace Fomoxa.Networking.Rapier
{
    public readonly struct CharacterSettings2D
    {
        private const float QuarterTurn = (float)(Math.PI / 4);

        private readonly bool custom;
        private readonly Vector2 up;
        private readonly CharacterLength offset;
        private readonly bool slide;
        private readonly CharacterAutostep? autostep;
        private readonly float maxSlopeClimbAngle;
        private readonly float minSlopeSlideAngle;
        private readonly CharacterLength? snapToGround;
        private readonly float normalNudgeFactor;
        private readonly float pushMass;

        private CharacterSettings2D(
            Vector2 up,
            CharacterLength offset,
            bool slide,
            CharacterAutostep? autostep,
            float maxSlopeClimbAngle,
            float minSlopeSlideAngle,
            CharacterLength? snapToGround,
            float normalNudgeFactor,
            float pushMass)
        {
            custom = true;
            this.up = up;
            this.offset = offset;
            this.slide = slide;
            this.autostep = autostep;
            this.maxSlopeClimbAngle = maxSlopeClimbAngle;
            this.minSlopeSlideAngle = minSlopeSlideAngle;
            this.snapToGround = snapToGround;
            this.normalNudgeFactor = normalNudgeFactor;
            this.pushMass = pushMass;
        }

        public static CharacterSettings2D Default => default;

        public Vector2 Up => custom ? up : Vector2.UnitY;

        public CharacterLength Offset => custom ? offset : CharacterLength.Relative(0.01f);

        public bool Slide => !custom || slide;

        public CharacterAutostep? Autostep => custom ? autostep : null;

        public float MaxSlopeClimbAngle => custom ? maxSlopeClimbAngle : QuarterTurn;

        public float MinSlopeSlideAngle => custom ? minSlopeSlideAngle : QuarterTurn;

        public CharacterLength? SnapToGround => custom ? snapToGround : CharacterLength.Relative(0.2f);

        public float NormalNudgeFactor => custom ? normalNudgeFactor : 1e-4f;

        public float PushMass => custom ? pushMass : 0f;

        public CharacterSettings2D WithUp(Vector2 value)
        {
            if (!(value.LengthSquared() > 0f) || float.IsInfinity(value.LengthSquared()))
            {
                throw new ArgumentException("up must be a finite, non-zero direction", nameof(value));
            }

            return new CharacterSettings2D(value, Offset, Slide, Autostep, MaxSlopeClimbAngle, MinSlopeSlideAngle, SnapToGround, NormalNudgeFactor, PushMass);
        }

        public CharacterSettings2D WithOffset(CharacterLength value) =>
            new CharacterSettings2D(Up, CharacterChecks.Offset(value), Slide, Autostep, MaxSlopeClimbAngle, MinSlopeSlideAngle, SnapToGround, NormalNudgeFactor, PushMass);

        public CharacterSettings2D WithSlide(bool value) =>
            new CharacterSettings2D(Up, Offset, value, Autostep, MaxSlopeClimbAngle, MinSlopeSlideAngle, SnapToGround, NormalNudgeFactor, PushMass);

        public CharacterSettings2D WithAutostep(CharacterAutostep? value) =>
            new CharacterSettings2D(Up, Offset, Slide, value, MaxSlopeClimbAngle, MinSlopeSlideAngle, SnapToGround, NormalNudgeFactor, PushMass);

        public CharacterSettings2D WithSlopeAngles(float maxClimb, float minSlide) =>
            new CharacterSettings2D(Up, Offset, Slide, Autostep, CharacterChecks.Angle(maxClimb, nameof(maxClimb)), CharacterChecks.Angle(minSlide, nameof(minSlide)), SnapToGround, NormalNudgeFactor, PushMass);

        public CharacterSettings2D WithSnapToGround(CharacterLength? value) =>
            new CharacterSettings2D(Up, Offset, Slide, Autostep, MaxSlopeClimbAngle, MinSlopeSlideAngle, value, NormalNudgeFactor, PushMass);

        public CharacterSettings2D WithNormalNudgeFactor(float value) =>
            new CharacterSettings2D(Up, Offset, Slide, Autostep, MaxSlopeClimbAngle, MinSlopeSlideAngle, SnapToGround, CharacterChecks.Amount(value, nameof(value)), PushMass);

        public CharacterSettings2D WithPushMass(float value) =>
            new CharacterSettings2D(Up, Offset, Slide, Autostep, MaxSlopeClimbAngle, MinSlopeSlideAngle, SnapToGround, NormalNudgeFactor, CharacterChecks.Amount(value, nameof(value)));
    }
}
