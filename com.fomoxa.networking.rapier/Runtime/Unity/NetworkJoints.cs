using Fomoxa.Networking.Rapier;
using UnityEngine;

namespace Fomoxa.Unity.Rapier
{
    public static class NetworkJoints
    {
        public static JointDesc Create(JointKind kind, Vector3 anchorA, Quaternion frameA, Vector3 anchorB, Quaternion frameB) =>
            new JointDesc(kind, anchorA.ToNumerics(), frameA.ToNumerics(), anchorB.ToNumerics(), frameB.ToNumerics());

        public static JointDesc Rope(Vector3 anchorA, Vector3 anchorB, float maxLength) =>
            JointDesc.Rope(anchorA.ToNumerics(), anchorB.ToNumerics(), maxLength);

        public static JointDesc Spring(Vector3 anchorA, Vector3 anchorB, float restLength, float stiffness, float damping) =>
            JointDesc.Spring(anchorA.ToNumerics(), anchorB.ToNumerics(), restLength, stiffness, damping);

        public static JointDesc2D Create2D(JointKind kind, Vector2 anchorA, float frameA, Vector2 anchorB, float frameB) =>
            new JointDesc2D(kind, anchorA.ToNumerics(), frameA, anchorB.ToNumerics(), frameB);

        public static JointDesc2D Rope2D(Vector2 anchorA, Vector2 anchorB, float maxLength) =>
            JointDesc2D.Rope(anchorA.ToNumerics(), anchorB.ToNumerics(), maxLength);

        public static JointDesc2D Spring2D(Vector2 anchorA, Vector2 anchorB, float restLength, float stiffness, float damping) =>
            JointDesc2D.Spring(anchorA.ToNumerics(), anchorB.ToNumerics(), restLength, stiffness, damping);
    }
}
