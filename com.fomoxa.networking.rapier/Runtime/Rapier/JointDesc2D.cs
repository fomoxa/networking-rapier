using System.Numerics;

namespace Fomoxa.Networking.Rapier
{
    public readonly struct JointDesc2D
    {
        public JointDesc2D(JointKind kind, Vector2 anchorA, float frameA, Vector2 anchorB, float frameB)
            : this(kind, anchorA, frameA, anchorB, frameB, default, default, 0f, 0f, 0f, default, false)
        {
        }

        private JointDesc2D(
            JointKind kind,
            Vector2 anchorA,
            float frameA,
            Vector2 anchorB,
            float frameB,
            JointLimit limit,
            JointMotorDesc motor,
            float length,
            float stiffness,
            float damping,
            JointSoftness softness,
            bool contactsEnabled)
        {
            Kind = kind;
            AnchorA = anchorA;
            FrameA = frameA;
            AnchorB = anchorB;
            FrameB = frameB;
            Limit = limit;
            Motor = motor;
            Length = length;
            Stiffness = stiffness;
            Damping = damping;
            Softness = softness;
            ContactsEnabled = contactsEnabled;
        }

        public JointKind Kind { get; }

        public Vector2 AnchorA { get; }

        public float FrameA { get; }

        public Vector2 AnchorB { get; }

        public float FrameB { get; }

        public JointLimit Limit { get; }

        public JointMotorDesc Motor { get; }

        public float Length { get; }

        public float Stiffness { get; }

        public float Damping { get; }

        public JointSoftness Softness { get; }

        public bool ContactsEnabled { get; }

        public static JointDesc2D Rope(Vector2 anchorA, Vector2 anchorB, float maxLength) =>
            new JointDesc2D(JointKind.Rope, anchorA, 0f, anchorB, 0f).WithLength(maxLength);

        public static JointDesc2D Spring(Vector2 anchorA, Vector2 anchorB, float restLength, float stiffness, float damping) =>
            new JointDesc2D(JointKind.Spring, anchorA, 0f, anchorB, 0f, default, default, restLength, stiffness, damping, default, false);

        public JointDesc2D WithLimit(JointLimit limit) =>
            new JointDesc2D(Kind, AnchorA, FrameA, AnchorB, FrameB, limit, Motor, Length, Stiffness, Damping, Softness, ContactsEnabled);

        public JointDesc2D WithMotor(JointMotorDesc motor) =>
            new JointDesc2D(Kind, AnchorA, FrameA, AnchorB, FrameB, Limit, motor, Length, Stiffness, Damping, Softness, ContactsEnabled);

        public JointDesc2D WithLength(float length) =>
            new JointDesc2D(Kind, AnchorA, FrameA, AnchorB, FrameB, Limit, Motor, length, Stiffness, Damping, Softness, ContactsEnabled);

        public JointDesc2D WithSoftness(JointSoftness softness) =>
            new JointDesc2D(Kind, AnchorA, FrameA, AnchorB, FrameB, Limit, Motor, Length, Stiffness, Damping, softness, ContactsEnabled);

        public JointDesc2D WithContacts(bool enabled) =>
            new JointDesc2D(Kind, AnchorA, FrameA, AnchorB, FrameB, Limit, Motor, Length, Stiffness, Damping, Softness, enabled);
    }
}
