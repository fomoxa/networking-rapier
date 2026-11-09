using System.Numerics;

namespace Fomoxa.Networking.Rapier
{
    public readonly struct JointDesc
    {
        public JointDesc(JointKind kind, Vector3 anchorA, Quaternion frameA, Vector3 anchorB, Quaternion frameB)
            : this(kind, anchorA, frameA, anchorB, frameB, default, default, default, default, 0f, 0f, 0f, default, false)
        {
        }

        private JointDesc(
            JointKind kind,
            Vector3 anchorA,
            Quaternion frameA,
            Vector3 anchorB,
            Quaternion frameB,
            JointLimit limit,
            JointLimit swingLimitY,
            JointLimit swingLimitZ,
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
            SwingLimitY = swingLimitY;
            SwingLimitZ = swingLimitZ;
            Motor = motor;
            Length = length;
            Stiffness = stiffness;
            Damping = damping;
            Softness = softness;
            ContactsEnabled = contactsEnabled;
        }

        public JointKind Kind { get; }

        public Vector3 AnchorA { get; }

        public Quaternion FrameA { get; }

        public Vector3 AnchorB { get; }

        public Quaternion FrameB { get; }

        public JointLimit Limit { get; }

        public JointLimit SwingLimitY { get; }

        public JointLimit SwingLimitZ { get; }

        public JointMotorDesc Motor { get; }

        public float Length { get; }

        public float Stiffness { get; }

        public float Damping { get; }

        public JointSoftness Softness { get; }

        public bool ContactsEnabled { get; }

        public static JointDesc Rope(Vector3 anchorA, Vector3 anchorB, float maxLength) =>
            new JointDesc(JointKind.Rope, anchorA, Quaternion.Identity, anchorB, Quaternion.Identity).WithLength(maxLength);

        public static JointDesc Spring(Vector3 anchorA, Vector3 anchorB, float restLength, float stiffness, float damping) =>
            new JointDesc(JointKind.Spring, anchorA, Quaternion.Identity, anchorB, Quaternion.Identity, default, default, default, default, restLength, stiffness, damping, default, false);

        public JointDesc WithLimit(JointLimit limit) =>
            new JointDesc(Kind, AnchorA, FrameA, AnchorB, FrameB, limit, SwingLimitY, SwingLimitZ, Motor, Length, Stiffness, Damping, Softness, ContactsEnabled);

        public JointDesc WithSwingLimits(JointLimit y, JointLimit z) =>
            new JointDesc(Kind, AnchorA, FrameA, AnchorB, FrameB, Limit, y, z, Motor, Length, Stiffness, Damping, Softness, ContactsEnabled);

        public JointDesc WithMotor(JointMotorDesc motor) =>
            new JointDesc(Kind, AnchorA, FrameA, AnchorB, FrameB, Limit, SwingLimitY, SwingLimitZ, motor, Length, Stiffness, Damping, Softness, ContactsEnabled);

        public JointDesc WithLength(float length) =>
            new JointDesc(Kind, AnchorA, FrameA, AnchorB, FrameB, Limit, SwingLimitY, SwingLimitZ, Motor, length, Stiffness, Damping, Softness, ContactsEnabled);

        public JointDesc WithSoftness(JointSoftness softness) =>
            new JointDesc(Kind, AnchorA, FrameA, AnchorB, FrameB, Limit, SwingLimitY, SwingLimitZ, Motor, Length, Stiffness, Damping, softness, ContactsEnabled);

        public JointDesc WithContacts(bool enabled) =>
            new JointDesc(Kind, AnchorA, FrameA, AnchorB, FrameB, Limit, SwingLimitY, SwingLimitZ, Motor, Length, Stiffness, Damping, Softness, enabled);
    }
}
