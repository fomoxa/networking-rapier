using System;
using System.Numerics;
using Fomoxa.Networking.Simulation;
using NUnit.Framework;

namespace Fomoxa.Networking.Rapier.Tests
{
    public sealed class RapierJoint2DTest
    {
        private const float Tick = 1f / 60f;
        private static readonly Vector2 Gravity = new Vector2(0f, -9.81f);

        [Test]
        public void AHingeKeepsItsAnchorAndALimitStopsItAndASliderMovesAlongItsAxisOnly()
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                BodyHandle free = world.CreateBody(Bar(new Vector2(1f, 5f)));
                world.CreateJoint(free, default, new JointDesc2D(JointKind.Revolute, new Vector2(-1f, 0f), 0f, new Vector2(0f, 5f), 0f));
                BodyHandle limited = world.CreateBody(Bar(new Vector2(11f, 5f)));
                world.CreateJoint(limited, default, new JointDesc2D(JointKind.Revolute, new Vector2(-1f, 0f), 0f, new Vector2(10f, 5f), 0f).WithLimit(new JointLimit(-0.5f, 0.5f)));
                BodyHandle slider = world.CreateBody(Bar(new Vector2(20f, 5f)));
                world.CreateJoint(slider, default, new JointDesc2D(JointKind.Prismatic, Vector2.Zero, -MathF.PI / 2f, new Vector2(20f, 5f), -MathF.PI / 2f).WithLimit(new JointLimit(-0.25f, 0.25f)));

                Run(world, 120);

                BodyState2D state = world.GetBody(free);
                Vector2 anchor = state.Position + Rotate(new Vector2(-1f, 0f), state.Rotation);
                Assert.Less(Vector2.Distance(anchor, new Vector2(0f, 5f)), 0.01f);
                Assert.AreEqual(-0.5f, world.GetBody(limited).Rotation, 0.02f);
                Assert.AreEqual(4.75f, world.GetBody(slider).Position.Y, 0.01f);
                Assert.AreEqual(20f, world.GetBody(slider).Position.X, 1e-3f);
                Assert.AreEqual(0f, world.GetBody(slider).Rotation, 1e-3f);
            }
        }

        [Test]
        public void ARopeAndASpringHoldABallAndAMotorTurnsAWheel()
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                BodyHandle hanging = world.CreateBody(Disc(new Vector2(0f, 5f)));
                world.CreateJoint(hanging, default, JointDesc2D.Rope(Vector2.Zero, new Vector2(0f, 7f), 3f));
                BodyHandle fixedLength = world.CreateBody(Disc(new Vector2(5f, 5f)));
                world.CreateJoint(fixedLength, default, JointDesc2D.Rope(Vector2.Zero, new Vector2(5f, 3f), 2f).WithLimit(new JointLimit(2f, 2f)));
                BodyHandle sprung = world.CreateBody(Disc(new Vector2(10f, 5f)));
                world.CreateJoint(sprung, default, JointDesc2D.Spring(Vector2.Zero, new Vector2(10f, 7f), 2f, 200f, 20f));

                Run(world, 300);

                Assert.AreEqual(4f, world.GetBody(hanging).Position.Y, 0.02f);
                Assert.AreEqual(2f, Vector2.Distance(world.GetBody(fixedLength).Position, new Vector2(5f, 3f)), 0.02f);
                Assert.AreEqual(9.81f / 200f, (7f - world.GetBody(sprung).Position.Y) - 2f, 0.01f);
            }

            using (var world = new RapierWorld2D(Vector2.Zero))
            {
                BodyHandle wheel = world.CreateBody(Bar(Vector2.Zero));
                world.CreateJoint(wheel, default, new JointDesc2D(JointKind.Revolute, Vector2.Zero, 0f, Vector2.Zero, 0f).WithMotor(JointMotorDesc.Velocity(2f, 1000f, 1000f)));

                Run(world, 30);

                Assert.AreEqual(2f, MathF.Abs(world.GetBody(wheel).AngularVelocity), 0.05f);
            }
        }

        [Test]
        public void LoadKeepsTheCurrentJointsAndRemovingABodyRemovesItsJoints()
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                BodyHandle early = world.CreateBody(Disc(new Vector2(0f, 5f)));
                JointHandle removed = world.CreateJoint(early, default, JointDesc2D.Rope(Vector2.Zero, new Vector2(0f, 6f), 1f));
                BodyHandle late = world.CreateBody(Disc(new Vector2(5f, 5f)));
                PhysicsSnapshot saved = world.CreateSnapshot();
                world.Save(saved);

                JointHandle created = world.CreateJoint(late, default, JointDesc2D.Rope(Vector2.Zero, new Vector2(5f, 6f), 1f));
                world.RemoveJoint(removed);
                world.Load(saved);
                Run(world, 120);

                Assert.IsTrue(world.Contains(created));
                Assert.IsFalse(world.Contains(removed));
                Assert.AreEqual(5f, world.GetBody(late).Position.Y, 0.02f);
                Assert.Less(world.GetBody(early).Position.Y, 0f);

                world.RemoveBody(late);
                Assert.IsFalse(world.Contains(created));
                Assert.Throws<ArgumentException>(() => world.CreateJoint(early, default, new JointDesc2D(JointKind.Spherical, Vector2.Zero, 0f, Vector2.Zero, 0f)));
            }
        }

        private static Vector2 Rotate(Vector2 vector, float angle) =>
            new Vector2((vector.X * MathF.Cos(angle)) - (vector.Y * MathF.Sin(angle)), (vector.X * MathF.Sin(angle)) + (vector.Y * MathF.Cos(angle)));

        private static BodyDesc2D Bar(Vector2 position) =>
            new BodyDesc2D(BodyKind.Dynamic, new[] { new ColliderDesc2D(BodyShape2D.Box(new Vector2(1f, 0.1f)), Vector2.Zero, 0f, ColliderMaterial.Default, 0, false) }, position, 0f, 1f);

        private static BodyDesc2D Disc(Vector2 position) =>
            new BodyDesc2D(BodyKind.Dynamic, new[] { new ColliderDesc2D(BodyShape2D.Circle(0.5f), Vector2.Zero, 0f, ColliderMaterial.Default, 0, false) }, position, 0f, 1f);

        private static void Run(RapierWorld2D world, int ticks)
        {
            for (int tick = 0; tick < ticks; tick++)
            {
                world.Step(Tick);
            }
        }
    }
}
