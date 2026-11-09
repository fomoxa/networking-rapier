using System;
using System.Collections.Generic;
using Fomoxa.Networking.Rapier;
using Fomoxa.Networking.Simulation;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity.Rapier.Tests
{
    public sealed class RapierJointsTest
    {
        private const float Seconds = 1f / 60f;

        private readonly List<string> ignored = new List<string>();
        private Scene scene;

        [SetUp]
        public void CreateScene() => scene = EditorSceneManager.NewPreviewScene();

        [TearDown]
        public void CloseScene() => EditorSceneManager.ClosePreviewScene(scene);

        [Test]
        public void HingeLimitsMotorsAndSpringsTurnTheSameWayAsPhysX()
        {
            HingeJoint limited = Hinge(new Vector3(0f, 5f, 0f), true);
            limited.useLimits = true;
            limited.limits = new JointLimits { min = -40f, max = 10f, bounceMinVelocity = 0.2f };
            HingeJoint motor = Hinge(new Vector3(10f, 5f, 0f), false);
            motor.useMotor = true;
            motor.motor = new UnityEngine.JointMotor { targetVelocity = 90f, force = 100f };
            HingeJoint spring = Hinge(new Vector3(20f, 5f, 0f), false);
            spring.useSpring = true;
            spring.spring = new JointSpring { spring = 50f, damper = 5f, targetPosition = 30f };

            using (RapierWorld world = Mirror(out Dictionary<Rigidbody, BodyHandle> bodies))
            {
                Run(world, 180);

                AssertAngle(-40f, limited.transform, world, bodies[limited.GetComponent<Rigidbody>()]);
                Assert.AreEqual(90f, motor.GetComponent<Rigidbody>().angularVelocity.z * Mathf.Rad2Deg, 3f);
                Assert.AreEqual(motor.GetComponent<Rigidbody>().angularVelocity.z, world.GetBody(bodies[motor.GetComponent<Rigidbody>()]).AngularVelocity.Z, 0.05f);
                AssertAngle(spring.transform.eulerAngles.z, spring.transform, world, bodies[spring.GetComponent<Rigidbody>()]);
                Assert.AreEqual(30f, Mathf.DeltaAngle(0f, spring.transform.eulerAngles.z), 3f);
            }
        }

        [Test]
        public void ACharacterJointTwistsWithinTheSameLimitsAsPhysX()
        {
            GameObject bar = Bar(new Vector3(1f, 5f, 0f), true);
            var joint = bar.AddComponent<CharacterJoint>();
            joint.anchor = new Vector3(-1f, 0f, 0f);
            joint.axis = Vector3.forward;
            joint.swingAxis = Vector3.up;
            joint.lowTwistLimit = new SoftJointLimit { limit = -40f };
            joint.highTwistLimit = new SoftJointLimit { limit = 10f };
            joint.swing1Limit = new SoftJointLimit { limit = 5f };
            joint.swing2Limit = new SoftJointLimit { limit = 5f };

            using (RapierWorld world = Mirror(out Dictionary<Rigidbody, BodyHandle> bodies))
            {
                Run(world, 180);

                AssertAngle(-10f, bar.transform, world, bodies[bar.GetComponent<Rigidbody>()]);
            }
        }

        [Test]
        public void AFixedJointHoldsTheOffsetAndSpringJointsHangWhereThoseOfPhysXHang()
        {
            GameObject left = Bar(new Vector3(0f, 5f, 0f), true);
            GameObject right = Bar(new Vector3(2f, 6f, 0f), true);
            right.AddComponent<FixedJoint>().connectedBody = left.GetComponent<Rigidbody>();
            GameObject pulledBack = Bar(new Vector3(10f, 5f, 0f), true);
            Spring(pulledBack, new Vector3(10f, 7f, 0f), 0f);
            GameObject hanging = Bar(new Vector3(15f, 5f, 0f), true);
            Spring(hanging, new Vector3(15f, 7f, 0f), 2f);
            GameObject defaulted = Bar(new Vector3(20f, 5f, 0f), true);
            var plain = defaulted.AddComponent<SpringJoint>();
            plain.spring = 200f;
            plain.damper = 20f;

            using (RapierWorld world = Mirror(out Dictionary<Rigidbody, BodyHandle> bodies))
            {
                Run(world, 300);

                BodyState leftState = world.GetBody(bodies[left.GetComponent<Rigidbody>()]);
                BodyState rightState = world.GetBody(bodies[right.GetComponent<Rigidbody>()]);
                Vector3 offset = Quaternion.Inverse(leftState.Rotation.ToUnity()) * (rightState.Position - leftState.Position).ToUnity();
                Assert.Less(Vector3.Distance(new Vector3(2f, 1f, 0f), offset), 0.02f);
                foreach (GameObject sprung in new[] { pulledBack, hanging, defaulted })
                {
                    Assert.AreEqual(sprung.transform.position.y, world.GetBody(bodies[sprung.GetComponent<Rigidbody>()]).Position.Y, 0.01f);
                }

                Assert.AreEqual(7f - 0.0746f, pulledBack.transform.position.y, 0.005f);
                Assert.AreEqual(7f - 2.0743f, hanging.transform.position.y, 0.005f);
            }
        }

        [Test]
        public void Hinge2DLimitsAndMotorsAndSlider2DAxesMatchBox2D()
        {
            HingeJoint2D limited = Hinge2D(new Vector2(0f, 5f), 1f);
            limited.useLimits = true;
            limited.limits = new JointAngleLimits2D { min = -40f, max = 10f };
            HingeJoint2D motor = Hinge2D(new Vector2(10f, 5f), 0f);
            motor.useMotor = true;
            motor.motor = new JointMotor2D { motorSpeed = 90f, maxMotorTorque = 100f };
            GameObject sliding = Bar2D(new Vector2(20f, 5f), 1f);
            sliding.transform.rotation = Quaternion.Euler(0f, 0f, 30f);
            var slider = sliding.AddComponent<SliderJoint2D>();
            slider.autoConfigureAngle = false;
            slider.autoConfigureConnectedAnchor = true;
            slider.angle = 0f;
            slider.useLimits = true;
            slider.limits = new JointTranslationLimits2D { min = -1f, max = 1f };

            using (RapierWorld2D world = Mirror2D(out Dictionary<Rigidbody2D, BodyHandle> bodies))
            {
                Run2D(world, 180);

                AssertAngle2D(limited.transform, world, bodies[limited.GetComponent<Rigidbody2D>()]);
                Assert.AreEqual(-10f, Mathf.DeltaAngle(0f, limited.transform.eulerAngles.z), 3f);
                float spin = motor.GetComponent<Rigidbody2D>().angularVelocity * Mathf.Deg2Rad;
                Assert.AreEqual(spin, world.GetBody(bodies[motor.GetComponent<Rigidbody2D>()]).AngularVelocity, 0.05f);
                Vector2 slid = world.GetBody(bodies[sliding.GetComponent<Rigidbody2D>()]).Position.ToUnity();
                Assert.Less(Vector2.Distance(sliding.transform.position, slid), 0.05f);
                Assert.Greater(Vector2.Distance(new Vector2(20f, 5f), slid), 0.9f);
            }
        }

        [Test]
        public void DistanceAndSpringJoints2DHoldTheSameLengthsAsBox2D()
        {
            GameObject rope = Bar2D(new Vector2(2f, 5f), 1f);
            var distance = rope.AddComponent<DistanceJoint2D>();
            distance.autoConfigureConnectedAnchor = false;
            distance.connectedAnchor = new Vector2(0f, 5f);
            distance.maxDistanceOnly = false;
            GameObject ball = Bar2D(new Vector2(10f, 5f), 1f);
            var spring = ball.AddComponent<SpringJoint2D>();
            spring.autoConfigureConnectedAnchor = false;
            spring.connectedAnchor = new Vector2(10f, 7f);
            spring.autoConfigureDistance = false;
            spring.distance = 2f;
            spring.frequency = 2f;
            spring.dampingRatio = 1f;

            using (RapierWorld2D world = Mirror2D(out Dictionary<Rigidbody2D, BodyHandle> bodies))
            {
                Run2D(world, 300);

                Vector2 swung = world.GetBody(bodies[rope.GetComponent<Rigidbody2D>()]).Position.ToUnity();
                Assert.AreEqual(2f, Vector2.Distance(new Vector2(0f, 5f), swung), 0.02f);
                Assert.AreEqual(2f, Vector2.Distance(new Vector2(0f, 5f), rope.transform.position), 0.02f);
                Assert.AreEqual(ball.transform.position.y, world.GetBody(bodies[ball.GetComponent<Rigidbody2D>()]).Position.Y, 0.05f);
            }
        }

        [Test]
        public void IgnoredParametersAreNamedAndBreakThresholdsAreFound()
        {
            HingeJoint hinge = Hinge(new Vector3(0f, 5f, 0f), true);
            hinge.massScale = 2f;
            hinge.useMotor = true;
            hinge.useSpring = true;
            hinge.breakForce = 100f;
            var spring = Bar(new Vector3(5f, 5f, 0f), true).AddComponent<SpringJoint>();
            spring.minDistance = 0.5f;
            var configurable = Bar(new Vector3(10f, 5f, 0f), true).AddComponent<ConfigurableJoint>();

            Assert.IsTrue(RapierJoints.TryDescribe(hinge, null, ignored, out JointDesc desc));
            CollectionAssert.AreEqual(new[] { "massScale", "useSpring with useMotor" }, ignored);
            Assert.IsTrue(desc.Motor.IsEnabled);
            Assert.IsTrue(RapierJoints.Breaks(hinge));
            ignored.Clear();
            Assert.IsTrue(RapierJoints.TryDescribe(spring, null, ignored, out _));
            CollectionAssert.AreEqual(new[] { "minDistance" }, ignored);
            Assert.IsFalse(RapierJoints.Breaks(spring));
            Assert.IsFalse(RapierJoints.TryDescribe(configurable, null, ignored, out _));
        }

        private HingeJoint Hinge(Vector3 pivot, bool gravity)
        {
            GameObject bar = Bar(pivot + new Vector3(1f, 0f, 0f), gravity);
            var hinge = bar.AddComponent<HingeJoint>();
            hinge.anchor = new Vector3(-1f, 0f, 0f);
            hinge.axis = Vector3.forward;
            return hinge;
        }

        private static void Spring(GameObject body, Vector3 connectedAnchor, float maxDistance)
        {
            var spring = body.AddComponent<SpringJoint>();
            spring.autoConfigureConnectedAnchor = false;
            spring.connectedAnchor = connectedAnchor;
            spring.spring = 200f;
            spring.damper = 20f;
            spring.maxDistance = maxDistance;
        }

        private HingeJoint2D Hinge2D(Vector2 pivot, float gravityScale)
        {
            GameObject bar = Bar2D(pivot + new Vector2(1f, 0f), gravityScale);
            var hinge = bar.AddComponent<HingeJoint2D>();
            hinge.anchor = new Vector2(-1f, 0f);
            return hinge;
        }

        private GameObject Bar(Vector3 position, bool gravity)
        {
            var bar = new GameObject("Bar");
            SceneManager.MoveGameObjectToScene(bar, scene);
            bar.transform.position = position;
            bar.AddComponent<BoxCollider>().size = new Vector3(2f, 0.2f, 0.2f);
            var body = bar.AddComponent<Rigidbody>();
            body.mass = 1f;
            body.useGravity = gravity;
            body.angularDamping = 0f;
            return bar;
        }

        private GameObject Bar2D(Vector2 position, float gravityScale)
        {
            var bar = new GameObject("Bar2D");
            SceneManager.MoveGameObjectToScene(bar, scene);
            bar.transform.position = position;
            bar.AddComponent<BoxCollider2D>().size = new Vector2(2f, 0.2f);
            var body = bar.AddComponent<Rigidbody2D>();
            body.mass = 1f;
            body.gravityScale = gravityScale;
            body.angularDamping = 0f;
            return bar;
        }

        private RapierWorld Mirror(out Dictionary<Rigidbody, BodyHandle> bodies)
        {
            var world = new RapierWorld(Physics.gravity.ToNumerics());
            bodies = new Dictionary<Rigidbody, BodyHandle>();
            var found = new List<Rigidbody>();
            var joints = new List<Joint>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                root.GetComponentsInChildren(found);
                foreach (Rigidbody body in found)
                {
                    Assert.IsTrue(BodyDescriptions.TryDescribe(body.gameObject, out BodyDesc desc));
                    bodies.Add(body, world.CreateBody(desc));
                }
            }

            foreach (KeyValuePair<Rigidbody, BodyHandle> entry in bodies)
            {
                entry.Key.GetComponents(joints);
                foreach (Joint joint in joints)
                {
                    Assert.IsTrue(RapierJoints.TryDescribe(joint, joint.connectedBody != null ? joint.connectedBody.transform : null, ignored, out JointDesc desc));
                    world.CreateJoint(entry.Value, joint.connectedBody != null ? bodies[joint.connectedBody] : default, desc);
                }
            }

            return world;
        }

        private RapierWorld2D Mirror2D(out Dictionary<Rigidbody2D, BodyHandle> bodies)
        {
            var world = new RapierWorld2D(new System.Numerics.Vector2(Physics2D.gravity.x, Physics2D.gravity.y));
            bodies = new Dictionary<Rigidbody2D, BodyHandle>();
            var found = new List<Rigidbody2D>();
            var joints = new List<Joint2D>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                root.GetComponentsInChildren(found);
                foreach (Rigidbody2D body in found)
                {
                    Assert.IsTrue(BodyDescriptions.TryDescribe2D(body.gameObject, out BodyDesc2D desc));
                    bodies.Add(body, world.CreateBody(desc));
                }
            }

            foreach (KeyValuePair<Rigidbody2D, BodyHandle> entry in bodies)
            {
                entry.Key.GetComponents(joints);
                foreach (Joint2D joint in joints)
                {
                    Assert.IsTrue(RapierJoints.TryDescribe2D(joint, joint.connectedBody != null ? joint.connectedBody.transform : null, ignored, out JointDesc2D desc));
                    world.CreateJoint(entry.Value, joint.connectedBody != null ? bodies[joint.connectedBody] : default, desc);
                }
            }

            return world;
        }

        private void Run(RapierWorld world, int steps)
        {
            for (int step = 0; step < steps; step++)
            {
                scene.GetPhysicsScene().Simulate(Seconds);
                world.Step(Seconds);
            }
        }

        private void Run2D(RapierWorld2D world, int steps)
        {
            for (int step = 0; step < steps; step++)
            {
                scene.GetPhysicsScene2D().Simulate(Seconds);
                world.Step(Seconds);
            }
        }

        private static void AssertAngle(float expected, Transform physX, RapierWorld world, BodyHandle body)
        {
            float rapier = world.GetBody(body).Rotation.ToUnity().eulerAngles.z;
            Assert.AreEqual(expected, Mathf.DeltaAngle(0f, physX.eulerAngles.z), 3f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(physX.eulerAngles.z, rapier), 3f);
        }

        private static void AssertAngle2D(Transform box2D, RapierWorld2D world, BodyHandle body)
        {
            float rapier = world.GetBody(body).Rotation * Mathf.Rad2Deg;
            Assert.AreEqual(0f, Mathf.DeltaAngle(box2D.eulerAngles.z, rapier), 3f);
        }
    }
}
