using System.Collections.Generic;
using Fomoxa.Networking.Rapier;
using UnityEngine;

namespace Fomoxa.Unity.Rapier
{
    internal static class RapierJoints
    {
        public static bool TryDescribe(Joint joint, Transform connected, List<string> ignored, out JointDesc desc)
        {
            Transform self = joint.transform;
            Vector3 anchor = self.TransformPoint(joint.anchor);
            Vector3 connectedAnchor = ConnectedAnchor(joint, connected, anchor);
            Quaternion frame = Frame(self.TransformDirection(joint.axis), self.TransformDirection(Vector3.up));
            IgnoredOfJoint(joint, ignored);
            switch (joint)
            {
                case CharacterJoint character:
                    desc = Describe(JointKind.Spherical, self, connected, anchor, connectedAnchor, Frame(self.TransformDirection(character.axis), self.TransformDirection(character.swingAxis)), joint.enableCollision)
                        .WithLimit(Radians(character.lowTwistLimit.limit, character.highTwistLimit.limit))
                        .WithSwingLimits(Radians(-character.swing1Limit.limit, character.swing1Limit.limit), Radians(-character.swing2Limit.limit, character.swing2Limit.limit));
                    IgnoredOfCharacter(character, ignored);
                    return true;
                case HingeJoint hinge:
                    desc = Describe(JointKind.Revolute, self, connected, anchor, connectedAnchor, frame, joint.enableCollision);
                    if (hinge.useLimits)
                    {
                        desc = desc.WithLimit(Radians(-hinge.limits.max, -hinge.limits.min));
                        IgnoredOfLimits(hinge.limits, ignored);
                    }

                    if (hinge.useMotor)
                    {
                        desc = desc.WithMotor(JointMotorDesc.Velocity(-hinge.motor.targetVelocity * Mathf.Deg2Rad, float.MaxValue, hinge.motor.force));
                        if (hinge.motor.freeSpin)
                        {
                            ignored.Add("motor.freeSpin");
                        }

                        if (hinge.useSpring)
                        {
                            ignored.Add("useSpring with useMotor");
                        }
                    }
                    else if (hinge.useSpring)
                    {
                        JointSpring spring = hinge.spring;
                        desc = desc.WithMotor(JointMotorDesc.Position(-spring.targetPosition * Mathf.Deg2Rad, spring.spring, spring.damper, float.MaxValue));
                    }

                    return true;
                case SpringJoint spring:
                    desc = JointDesc.Spring(Local(self, anchor), Local(connected, connectedAnchor), spring.maxDistance + spring.tolerance, spring.spring, spring.damper)
                        .WithContacts(joint.enableCollision);
                    if (spring.minDistance != 0f)
                    {
                        ignored.Add("minDistance");
                    }

                    return true;
                case FixedJoint _:
                    desc = Describe(JointKind.Fixed, self, connected, anchor, connectedAnchor, self.rotation, joint.enableCollision);
                    return true;
                default:
                    desc = default;
                    return false;
            }
        }

        public static bool TryDescribe2D(Joint2D joint, Transform connected, List<string> ignored, out JointDesc2D desc)
        {
            Transform self = joint.transform;
            var anchored = joint as AnchoredJoint2D;
            Vector2 anchor = self.TransformPoint(anchored != null ? (Vector3)anchored.anchor : Vector3.zero);
            Vector2 connectedAnchor = anchored == null || anchored.autoConfigureConnectedAnchor
                ? anchor
                : connected != null ? (Vector2)connected.TransformPoint(anchored.connectedAnchor) : anchored.connectedAnchor;
            float angle = Angle(self);
            switch (joint)
            {
                case FixedJoint2D weld:
                    desc = Describe2D(JointKind.Fixed, self, connected, anchor, connectedAnchor, angle, joint.enableCollision);
                    if (weld.frequency > 0f)
                    {
                        desc = desc.WithSoftness(new JointSoftness(weld.frequency, weld.dampingRatio));
                    }

                    return true;
                case HingeJoint2D hinge:
                    desc = Describe2D(JointKind.Revolute, self, connected, anchor, connectedAnchor, angle, joint.enableCollision);
                    if (hinge.useLimits)
                    {
                        desc = desc.WithLimit(Radians(hinge.limits.min, hinge.limits.max));
                    }

                    if (hinge.useMotor)
                    {
                        desc = desc.WithMotor(JointMotorDesc.Velocity(hinge.motor.motorSpeed * Mathf.Deg2Rad, float.MaxValue, hinge.motor.maxMotorTorque));
                    }

                    return true;
                case SliderJoint2D slider:
                    desc = Describe2D(JointKind.Prismatic, self, connected, anchor, connectedAnchor, angle + (slider.angle * Mathf.Deg2Rad), joint.enableCollision);
                    if (slider.useLimits)
                    {
                        desc = desc.WithLimit(new JointLimit(slider.limits.min, slider.limits.max));
                    }

                    if (slider.useMotor)
                    {
                        desc = desc.WithMotor(JointMotorDesc.Velocity(slider.motor.motorSpeed, float.MaxValue, slider.motor.maxMotorTorque));
                    }

                    return true;
                case SpringJoint2D spring:
                    float distance = spring.autoConfigureDistance ? Vector2.Distance(anchor, connectedAnchor) : spring.distance;
                    if (spring.frequency > 0f)
                    {
                        float mass = ReducedMass(self, connected);
                        float omega = 2f * Mathf.PI * spring.frequency;
                        desc = JointDesc2D.Spring(Local2D(self, anchor), Local2D(connected, connectedAnchor), distance, mass * omega * omega, 2f * mass * spring.dampingRatio * omega);
                    }
                    else
                    {
                        desc = JointDesc2D.Rope(Local2D(self, anchor), Local2D(connected, connectedAnchor), distance).WithLimit(new JointLimit(distance, distance));
                    }

                    desc = desc.WithContacts(joint.enableCollision);
                    return true;
                case DistanceJoint2D rope:
                    float length = rope.autoConfigureDistance ? Vector2.Distance(anchor, connectedAnchor) : rope.distance;
                    desc = JointDesc2D.Rope(Local2D(self, anchor), Local2D(connected, connectedAnchor), length).WithContacts(joint.enableCollision);
                    if (!rope.maxDistanceOnly)
                    {
                        desc = desc.WithLimit(new JointLimit(length, length));
                    }

                    return true;
                default:
                    desc = default;
                    return false;
            }
        }

        public static bool Breaks(Joint joint) => !float.IsPositiveInfinity(joint.breakForce) || !float.IsPositiveInfinity(joint.breakTorque);

        public static bool Breaks(Joint2D joint) => !float.IsPositiveInfinity(joint.breakForce) || !float.IsPositiveInfinity(joint.breakTorque);

        private static JointDesc Describe(JointKind kind, Transform self, Transform connected, Vector3 anchor, Vector3 connectedAnchor, Quaternion frame, bool contacts) =>
            new JointDesc(kind, Local(self, anchor), (Quaternion.Inverse(self.rotation) * frame).ToNumerics(), Local(connected, connectedAnchor), (Quaternion.Inverse(RotationOf(connected)) * frame).ToNumerics())
                .WithContacts(contacts);

        private static JointDesc2D Describe2D(JointKind kind, Transform self, Transform connected, Vector2 anchor, Vector2 connectedAnchor, float frame, bool contacts) =>
            new JointDesc2D(kind, Local2D(self, anchor), frame - Angle(self), Local2D(connected, connectedAnchor), frame - (connected != null ? Angle(connected) : 0f))
                .WithContacts(contacts);

        private static Vector3 ConnectedAnchor(Joint joint, Transform connected, Vector3 anchor)
        {
            if (joint.autoConfigureConnectedAnchor)
            {
                return anchor;
            }

            return connected != null ? connected.TransformPoint(joint.connectedAnchor) : joint.connectedAnchor;
        }

        private static Quaternion Frame(Vector3 axis, Vector3 secondary)
        {
            Vector3 x = axis.sqrMagnitude > 0f ? axis.normalized : Vector3.right;
            Vector3 y = secondary - (Vector3.Dot(secondary, x) * x);
            if (y.sqrMagnitude < 1e-8f)
            {
                y = Vector3.Cross(x, Mathf.Abs(x.y) < 0.9f ? Vector3.up : Vector3.forward);
            }

            y.Normalize();
            return Quaternion.LookRotation(Vector3.Cross(x, y), y);
        }

        private static System.Numerics.Vector3 Local(Transform body, Vector3 point) =>
            body != null ? (Quaternion.Inverse(body.rotation) * (point - body.position)).ToNumerics() : point.ToNumerics();

        private static System.Numerics.Vector2 Local2D(Transform body, Vector2 point)
        {
            if (body == null)
            {
                return new System.Numerics.Vector2(point.x, point.y);
            }

            Vector2 offset = point - (Vector2)body.position;
            float angle = -Angle(body);
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);
            return new System.Numerics.Vector2((offset.x * cos) - (offset.y * sin), (offset.x * sin) + (offset.y * cos));
        }

        private static Quaternion RotationOf(Transform body) => body != null ? body.rotation : Quaternion.identity;

        private static float Angle(Transform body) => body.eulerAngles.z * Mathf.Deg2Rad;

        private static JointLimit Radians(float min, float max) => new JointLimit(Mathf.Min(min, max) * Mathf.Deg2Rad, Mathf.Max(min, max) * Mathf.Deg2Rad);

        private static float ReducedMass(Transform self, Transform connected)
        {
            float own = self.TryGetComponent(out Rigidbody2D body) ? body.mass : 1f;
            if (connected == null || !connected.TryGetComponent(out Rigidbody2D other))
            {
                return own;
            }

            return own * other.mass / (own + other.mass);
        }

        private static void IgnoredOfJoint(Joint joint, List<string> ignored)
        {
            if (joint.massScale != 1f)
            {
                ignored.Add("massScale");
            }

            if (joint.connectedMassScale != 1f)
            {
                ignored.Add("connectedMassScale");
            }

            if (!joint.enablePreprocessing)
            {
                ignored.Add("enablePreprocessing");
            }
        }

        private static void IgnoredOfLimits(JointLimits limits, List<string> ignored)
        {
            if (limits.bounciness != 0f || limits.bounceMinVelocity != 0.2f || limits.contactDistance != 0f)
            {
                ignored.Add("limits.bounciness");
            }
        }

        private static void IgnoredOfCharacter(CharacterJoint joint, List<string> ignored)
        {
            if (joint.lowTwistLimit.bounciness != 0f || joint.highTwistLimit.bounciness != 0f || joint.swing1Limit.bounciness != 0f || joint.swing2Limit.bounciness != 0f)
            {
                ignored.Add("limit bounciness");
            }

            if (joint.twistLimitSpring.spring != 0f || joint.swingLimitSpring.spring != 0f)
            {
                ignored.Add("limit springs");
            }

            if (joint.enableProjection)
            {
                ignored.Add("enableProjection");
            }
        }
    }
}
