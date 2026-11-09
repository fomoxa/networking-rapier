using System;
using System.Numerics;
using Fomoxa.Networking.Simulation;
using NUnit.Framework;

namespace Fomoxa.Networking.Rapier.Tests
{
    public sealed class RapierJointTest
    {
        private const float Tick = 1f / 60f;
        private static readonly Vector3 Gravity = new Vector3(0f, -9.81f, 0f);
        private static readonly Quaternion AlongZ = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2f);

        [Test]
        public void AHingeToTheWorldKeepsItsAnchorAndTurnsOnlyAboutItsAxis()
        {
            using (var world = new RapierWorld(Gravity))
            {
                BodyHandle bar = world.CreateBody(Bar(new Vector3(1f, 5f, 0f)));
                world.CreateJoint(bar, default, new JointDesc(JointKind.Revolute, new Vector3(-1f, 0f, 0f), AlongZ, new Vector3(0f, 5f, 0f), AlongZ));

                float lowest = float.MaxValue;
                for (int tick = 0; tick < 90; tick++)
                {
                    world.Step(Tick);
                    lowest = MathF.Min(lowest, world.GetBody(bar).Position.Y);
                }

                BodyState state = world.GetBody(bar);
                Vector3 anchor = state.Position + Vector3.Transform(new Vector3(-1f, 0f, 0f), state.Rotation);
                Assert.Less(Vector3.Distance(anchor, new Vector3(0f, 5f, 0f)), 0.01f);
                Assert.AreEqual(4f, lowest, 0.05f);
                Assert.AreEqual(0f, state.Position.Z, 1e-3f);
                Vector3 axis = Vector3.Transform(Vector3.UnitZ, state.Rotation);
                Assert.AreEqual(1f, axis.Z, 1e-3f);
            }
        }

        [Test]
        public void LimitsHoldARevoluteAndAPrismaticJointAndAVelocityMotorTurnsAHinge()
        {
            using (var world = new RapierWorld(Gravity))
            {
                BodyHandle limited = world.CreateBody(Bar(new Vector3(1f, 5f, 0f)));
                world.CreateJoint(limited, default, new JointDesc(JointKind.Revolute, new Vector3(-1f, 0f, 0f), AlongZ, new Vector3(0f, 5f, 0f), AlongZ).WithLimit(new JointLimit(-0.5f, 0.5f)));
                BodyHandle slider = world.CreateBody(Bar(new Vector3(10f, 5f, 0f)));
                Quaternion down = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI / 2f);
                world.CreateJoint(slider, default, new JointDesc(JointKind.Prismatic, Vector3.Zero, down, new Vector3(10f, 5f, 0f), down).WithLimit(new JointLimit(-0.25f, 0.25f)));

                Run(world, 120);

                float angle = 2f * MathF.Asin(world.GetBody(limited).Rotation.Z);
                Assert.AreEqual(-0.5f, angle, 0.02f);
                Assert.AreEqual(4.75f, world.GetBody(slider).Position.Y, 0.01f);
                Assert.AreEqual(10f, world.GetBody(slider).Position.X, 1e-3f);
            }

            using (var world = new RapierWorld(Vector3.Zero))
            {
                BodyHandle wheel = world.CreateBody(Bar(new Vector3(0f, 0f, 0f)));
                world.CreateJoint(wheel, default, new JointDesc(JointKind.Revolute, Vector3.Zero, AlongZ, Vector3.Zero, AlongZ).WithMotor(JointMotorDesc.Velocity(2f, 1000f, 1000f)));

                Run(world, 30);

                Assert.AreEqual(2f, MathF.Abs(world.GetBody(wheel).AngularVelocity.Z), 0.05f);
            }
        }

        [Test]
        public void ARopeLetsTheBodyCloserButNotFartherAndASpringPullsTowardsItsRestLength()
        {
            using (var world = new RapierWorld(Gravity))
            {
                BodyHandle hanging = world.CreateBody(Ball(new Vector3(0f, 5f, 0f)));
                world.CreateJoint(hanging, default, JointDesc.Rope(Vector3.Zero, new Vector3(0f, 7f, 0f), 3f));
                BodyHandle sprung = world.CreateBody(Ball(new Vector3(10f, 5f, 0f)));
                world.CreateJoint(sprung, default, JointDesc.Spring(Vector3.Zero, new Vector3(10f, 7f, 0f), 2f, 200f, 20f));

                Run(world, 10);
                float early = world.GetBody(hanging).Position.Y;
                Run(world, 290);

                Assert.Less(early, 5f);
                Assert.Greater(early, 4f);
                Assert.AreEqual(4f, world.GetBody(hanging).Position.Y, 0.02f);
                float stretch = (7f - world.GetBody(sprung).Position.Y) - 2f;
                Assert.AreEqual(9.81f / 200f, stretch, 0.01f);
            }
        }

        [Test]
        public void AFixedJointKeepsTwoBodiesTogether()
        {
            using (var world = new RapierWorld(Gravity))
            {
                world.AddStatic(new[] { new ColliderDesc(BodyShape.Box(new Vector3(20f, 0.5f, 20f)), new Vector3(0f, -0.5f, 0f), Quaternion.Identity, ColliderMaterial.Default, 0, false) });
                BodyHandle left = world.CreateBody(Ball(new Vector3(-0.6f, 3f, 0f)));
                BodyHandle right = world.CreateBody(Ball(new Vector3(0.6f, 4f, 0f)));
                world.CreateJoint(left, right, new JointDesc(JointKind.Fixed, new Vector3(0.6f, 0.5f, 0f), Quaternion.Identity, new Vector3(-0.6f, -0.5f, 0f), Quaternion.Identity));

                Run(world, 240);

                Vector3 offset = world.GetBody(right).Position - world.GetBody(left).Position;
                Vector3 expected = Vector3.Transform(new Vector3(1.2f, 1f, 0f), world.GetBody(left).Rotation);
                Assert.Less(Vector3.Distance(offset, expected), 0.02f);
            }
        }

        [Test]
        public void LoadKeepsTheCurrentJointsWithTheirHandlesAndRemovingABodyRemovesItsJoints()
        {
            using (var world = new RapierWorld(Gravity))
            {
                BodyHandle early = world.CreateBody(Ball(new Vector3(0f, 5f, 0f)));
                JointHandle removed = world.CreateJoint(early, default, JointDesc.Rope(Vector3.Zero, new Vector3(0f, 6f, 0f), 1f));
                BodyHandle late = world.CreateBody(Ball(new Vector3(5f, 5f, 0f)));
                PhysicsSnapshot saved = world.CreateSnapshot();
                world.Save(saved);

                JointHandle created = world.CreateJoint(late, default, JointDesc.Rope(Vector3.Zero, new Vector3(5f, 6f, 0f), 1f));
                world.RemoveJoint(removed);
                world.Load(saved);
                Run(world, 120);

                Assert.IsTrue(world.Contains(created));
                Assert.IsFalse(world.Contains(removed));
                Assert.AreEqual(5f, world.GetBody(late).Position.Y, 0.02f);
                Assert.Less(world.GetBody(early).Position.Y, 0f);

                world.RemoveBody(late);
                Assert.IsFalse(world.Contains(created));
                Assert.IsFalse(world.RemoveJoint(created));
            }
        }

        [Test]
        public void SetJointChangesTheMotorAndALoadedJointKeepsTheDescriptionOfItsSnapshot()
        {
            using (var world = new RapierWorld(Vector3.Zero))
            {
                BodyHandle wheel = world.CreateBody(Bar(Vector3.Zero));
                var turning = new JointDesc(JointKind.Revolute, Vector3.Zero, AlongZ, Vector3.Zero, AlongZ).WithMotor(JointMotorDesc.Velocity(1f, 1000f, 1000f));
                JointHandle hinge = world.CreateJoint(wheel, default, turning);
                Run(world, 30);
                PhysicsSnapshot saved = world.CreateSnapshot();
                world.Save(saved);

                world.SetJoint(hinge, turning.WithMotor(JointMotorDesc.Velocity(-3f, 1000f, 1000f)));
                Run(world, 30);
                float retuned = world.GetBody(wheel).AngularVelocity.Z;
                world.Load(saved);
                Run(world, 30);

                Assert.AreEqual(3f, MathF.Abs(retuned), 0.05f);
                Assert.AreEqual(1f, MathF.Abs(world.GetBody(wheel).AngularVelocity.Z), 0.05f);
                Assert.Less(retuned * world.GetBody(wheel).AngularVelocity.Z, 0f);
            }
        }

        [Test]
        public void AJointNeedsTwoDifferentBodiesOfTheWorldAndAValidDescription()
        {
            using (var world = new RapierWorld(Gravity))
            {
                BodyHandle ball = world.CreateBody(Ball(Vector3.Zero));
                var hinge = new JointDesc(JointKind.Revolute, Vector3.Zero, Quaternion.Identity, Vector3.Zero, Quaternion.Identity);

                Assert.Throws<ArgumentException>(() => world.CreateJoint(default, ball, hinge));
                Assert.Throws<ArgumentException>(() => world.CreateJoint(ball, ball, hinge));
                Assert.Throws<ArgumentException>(() => world.CreateJoint(ball, new BodyHandle(99), hinge));
                Assert.Throws<ArgumentException>(() => world.CreateJoint(ball, default, JointDesc.Rope(Vector3.Zero, Vector3.Zero, 0f)));
                Assert.Throws<ArgumentException>(() => world.SetJoint(new JointHandle(7), hinge));
                Assert.Throws<ArgumentException>(() => new JointLimit(1f, -1f));
                Assert.IsFalse(world.Contains(default(JointHandle)));
            }
        }

        private static BodyDesc Bar(Vector3 position) =>
            new BodyDesc(BodyKind.Dynamic, new[] { new ColliderDesc(BodyShape.Box(new Vector3(1f, 0.1f, 0.1f)), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 0, false) }, position, Quaternion.Identity, 1f);

        private static BodyDesc Ball(Vector3 position) =>
            new BodyDesc(BodyKind.Dynamic, new[] { new ColliderDesc(BodyShape.Sphere(0.5f), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 0, false) }, position, Quaternion.Identity, 1f);

        private static void Run(RapierWorld world, int ticks)
        {
            for (int tick = 0; tick < ticks; tick++)
            {
                world.Step(Tick);
            }
        }
    }
}
