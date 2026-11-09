using System;
using System.Numerics;
using Fomoxa.Networking.Simulation;
using NUnit.Framework;

namespace Fomoxa.Networking.Rapier.Tests
{
    public sealed class RapierEffector2DTest
    {
        private const float Tick = 1f / 60f;
        private const float HalfTurn = MathF.PI;
        private static readonly Vector2 Gravity = new Vector2(0f, -9.81f);

        [Test]
        public void AnAreaPushesEachBodyOnceFromTheNextStepAndDampsAtOnce()
        {
            using (var world = new RapierWorld2D(Vector2.Zero))
            {
                StaticGroup area = world.AddStatic(new[] { Box(new Vector2(5f), Vector2.Zero, true), Box(new Vector2(5f), Vector2.Zero, true) });
                world.AddEffector(area, new[] { 0, 1 }, EffectorDesc2D.Area(0f, true, 10f, EffectorSelection.Rigidbody, 0f, 0f));
                BodyHandle pushed = world.CreateBody(Dynamic(Vector2.Zero, 1f));
                BodyHandle damped = world.CreateBody(Dynamic(new Vector2(30f, 0f), 1f));
                StaticGroup drag = world.AddStatic(new[] { Box(new Vector2(5f), new Vector2(30f, 0f), true) });
                world.AddEffector(drag, new[] { 0 }, EffectorDesc2D.Area(0f, true, 0f, EffectorSelection.Rigidbody, 1f, 0f));
                world.SetBody(damped, new BodyState2D { Position = new Vector2(30f, 0f), Velocity = new Vector2(5f, 0f) });

                world.Step(Tick);
                Assert.AreEqual(0f, world.GetBody(pushed).Velocity.X, 1e-6f);
                Assert.AreEqual(5f / (1f + Tick), world.GetBody(damped).Velocity.X, 1e-4f);

                world.Step(Tick);
                Assert.AreEqual(10f * Tick, world.GetBody(pushed).Velocity.X, 1e-4f);
            }
        }

        [Test]
        public void APointPullsWithAForceThatFallsOffBeyondOneUnit()
        {
            Assert.AreEqual(5f, PointForce(EffectorForceMode.InverseLinear, 2f, 1f), 1e-3f);
            Assert.AreEqual(10f, PointForce(EffectorForceMode.InverseLinear, 0.5f, 1f), 1e-3f);
            Assert.AreEqual(2.5f, PointForce(EffectorForceMode.InverseSquared, 2f, 1f), 1e-3f);
            Assert.AreEqual(0.625f, PointForce(EffectorForceMode.InverseSquared, 2f, 2f), 1e-3f);
            Assert.AreEqual(10f, PointForce(EffectorForceMode.Constant, 2f, 2f), 1e-3f);
        }

        [Test]
        public void BuoyancyLiftsTheSubmergedAreaAndScalesWithGravityScale()
        {
            Assert.AreEqual(9.81f, Lift(new Vector2(0f, -3f), 1f, 1f), 1e-2f);
            Assert.AreEqual(19.62f, Lift(new Vector2(0f, -3f), 2f, 1f), 1e-2f);
            Assert.AreEqual(4.905f, Lift(Vector2.Zero, 1f, 1f), 0.05f);
            Assert.AreEqual(19.62f, Lift(new Vector2(0f, -3f), 1f, 2f), 1e-2f);
        }

        [Test]
        public void ASurfaceMovesWhatRestsOnItTowardsItsSpeed()
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                StaticGroup belt = world.AddStatic(new[] { Box(new Vector2(20f, 0.5f), new Vector2(0f, -0.5f), false) });
                world.AddEffector(belt, new[] { 0 }, EffectorDesc2D.Surface(5f, 0.1f, false, false, false));
                BodyHandle crate = world.CreateBody(Dynamic(new Vector2(0f, 0.5f), 1f));
                for (int tick = 0; tick < 5; tick++)
                {
                    world.Step(Tick);
                }

                float speed = world.GetBody(crate).Velocity.X;
                Assert.Greater(speed, 1.5f);
                for (int tick = 0; tick < 115; tick++)
                {
                    world.Step(Tick);
                }

                Assert.AreEqual(5f, world.GetBody(crate).Velocity.X, 0.01f);
            }
        }

        [Test]
        public void AOneWayPlatformLetsABodyThroughFromBelowAndHoldsItFromAbove()
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                StaticGroup ledge = world.AddStatic(new[] { Box(new Vector2(2f, 0.25f), Vector2.Zero, false) });
                world.AddEffector(ledge, new[] { 0 }, EffectorDesc2D.Platform(true, false, 0f, HalfTurn, false, false, HalfTurn / 180f));
                BodyHandle rising = world.CreateBody(Dynamic(new Vector2(0f, -2f), 1f));
                world.SetBody(rising, new BodyState2D { Position = new Vector2(0f, -2f), Velocity = new Vector2(0f, 10f) });
                for (int tick = 0; tick < 180; tick++)
                {
                    world.Step(Tick);
                }

                Assert.AreEqual(0.75f, world.GetBody(rising).Position.Y, 0.02f);
                Assert.AreEqual(0f, world.GetBody(rising).Velocity.Y, 0.01f);
            }
        }

        [Test]
        public void AColliderMaskReplacesTheLayerMatrixOfTheEffectorColliders()
        {
            using (var world = new RapierWorld2D(Vector2.Zero))
            {
                StaticGroup area = world.AddStatic(new[] { Box(new Vector2(5f), Vector2.Zero, true) });
                EffectorHandle effector = world.AddEffector(area, new[] { 0 }, EffectorDesc2D.Area(0f, true, 10f, EffectorSelection.Rigidbody, 0f, 0f).WithColliderMask(1u << 5));
                BodyHandle body = world.CreateBody(Dynamic(Vector2.Zero, 1f));
                world.Step(Tick);
                world.Step(Tick);
                Assert.AreEqual(0f, world.GetBody(body).Velocity.X, 1e-6f);

                world.SetEffector(effector, EffectorDesc2D.Area(0f, true, 10f, EffectorSelection.Rigidbody, 0f, 0f));
                world.Step(Tick);
                world.Step(Tick);
                Assert.Greater(world.GetBody(body).Velocity.X, 0.1f);
            }
        }

        [Test]
        public void EffectorsAreRefusedForBadCollidersAndGoWithTheirBody()
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                BodyHandle body = world.CreateBody(Dynamic(Vector2.Zero, 1f));
                EffectorDesc2D area = EffectorDesc2D.Area(0f, true, 1f, EffectorSelection.Rigidbody, 0f, 0f);
                Assert.Throws<ArgumentException>(() => world.AddEffector(body, new[] { 1 }, area));
                Assert.Throws<ArgumentException>(() => world.AddEffector(body, Array.Empty<int>(), area));
                Assert.Throws<ArgumentException>(() => world.AddEffector(body, new[] { 0 }, default));
                Assert.Throws<ArgumentException>(() => world.AddEffector(body, new[] { 0 }, EffectorDesc2D.Area(float.NaN, true, 1f, EffectorSelection.Rigidbody, 0f, 0f)));
                EffectorHandle effector = world.AddEffector(body, new[] { 0 }, area);
                Assert.Throws<ArgumentException>(() => world.AddEffector(body, new[] { 0 }, area));
                Assert.IsTrue(world.Contains(effector));

                world.RemoveBody(body);

                Assert.IsFalse(world.Contains(effector));
                Assert.IsFalse(world.RemoveEffector(effector));
                Assert.Throws<ArgumentException>(() => world.SetEffector(effector, area));
            }
        }

        [Test]
        public void LoadKeepsTheEffectorsOfNowWithTheDescriptionsOfTheSnapshotAndReplaysBitForBit()
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                StaticGroup ledge = world.AddStatic(new[] { Box(new Vector2(2f, 0.25f), Vector2.Zero, false), Box(new Vector2(5f), new Vector2(10f, 0f), true) });
                world.AddEffector(ledge, new[] { 0 }, EffectorDesc2D.Platform(true, false, 0f, HalfTurn, false, false, HalfTurn / 180f));
                EffectorHandle area = world.AddEffector(ledge, new[] { 1 }, EffectorDesc2D.Area(HalfTurn / 2f, true, 15f, EffectorSelection.Rigidbody, 0.5f, 0.5f));
                BodyHandle rising = world.CreateBody(Dynamic(new Vector2(0f, -2f), 1f));
                world.SetBody(rising, new BodyState2D { Position = new Vector2(0f, -2f), Velocity = new Vector2(0f, 9f) });
                BodyHandle floating = world.CreateBody(Dynamic(new Vector2(10f, 0f), 1f));
                for (int tick = 0; tick < 12; tick++)
                {
                    world.Step(Tick);
                }

                PhysicsSnapshot saved = world.CreateSnapshot();
                world.Save(saved);
                ulong[] first = Run(world, 120);

                world.SetEffector(area, EffectorDesc2D.Area(0f, true, 50f, EffectorSelection.Rigidbody, 0f, 0f));
                EffectorHandle later = world.AddEffector(floating, new[] { 0 }, EffectorDesc2D.Area(0f, true, 1f, EffectorSelection.Rigidbody, 0f, 0f));
                world.Load(saved);
                ulong[] second = Run(world, 120);

                CollectionAssert.AreEqual(first, second);
                Assert.IsTrue(world.Contains(later));
                Assert.Greater(world.GetBody(floating).Velocity.Y, 0f);
                Assert.AreEqual(0.75f, world.GetBody(rising).Position.Y, 0.05f);
            }
        }

        [Test]
        public void MoveCharacterPassesUpThroughAOneWayPlatformAndLandsOnIt()
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                StaticGroup ledge = world.AddStatic(new[] { Box(new Vector2(2f, 0.25f), Vector2.Zero, false) });
                world.AddEffector(ledge, new[] { 0 }, EffectorDesc2D.Platform(true, false, 0f, HalfTurn, false, false, HalfTurn / 180f));
                BodyHandle character = world.CreateBody(new BodyDesc2D(BodyKind.Kinematic, new[] { new ColliderDesc2D(BodyShape2D.Capsule(0.3f, 0.5f), Vector2.Zero, 0f, ColliderMaterial.Default, 0, false) }, new Vector2(0f, -1.5f), 0f, 1f));
                world.Step(Tick);

                Assert.AreEqual(1f, world.MoveCharacter(character, new Vector2(0f, 1f), Tick, CharacterSettings2D.Default).Moved.Y, 1e-3f);
                world.Step(Tick);
                Assert.AreEqual(1f, world.MoveCharacter(character, new Vector2(0f, 1f), Tick, CharacterSettings2D.Default).Moved.Y, 1e-3f);
                world.Step(Tick);
                Assert.AreEqual(1f, world.MoveCharacter(character, new Vector2(0f, 1f), Tick, CharacterSettings2D.Default).Moved.Y, 1e-3f);
                world.Step(Tick);

                CharacterMove2D landing = world.MoveCharacter(character, new Vector2(0f, -2f), Tick, CharacterSettings2D.Default);
                Assert.IsTrue(landing.Grounded);
                Assert.AreEqual(-0.45f, landing.Moved.Y, 0.03f);
            }
        }

        private static ulong[] Run(RapierWorld2D world, int ticks)
        {
            var hashes = new ulong[ticks];
            for (int tick = 0; tick < ticks; tick++)
            {
                world.Step(Tick);
                hashes[tick] = world.StateHash;
            }

            return hashes;
        }

        private static float PointForce(EffectorForceMode mode, float distance, float scale)
        {
            using (var world = new RapierWorld2D(Vector2.Zero))
            {
                StaticGroup point = world.AddStatic(new[] { new ColliderDesc2D(BodyShape2D.Circle(10f), Vector2.Zero, 0f, ColliderMaterial.Default, 0, true) });
                world.AddEffector(point, new[] { 0 }, EffectorDesc2D.Point(-10f, scale, EffectorSelection.Rigidbody, EffectorSelection.Rigidbody, mode, 0f, 0f));
                BodyHandle body = world.CreateBody(Dynamic(new Vector2(distance, 0f), 1f));
                world.Step(Tick);
                world.Step(Tick);
                return -world.GetBody(body).Velocity.X / Tick;
            }
        }

        private static float Lift(Vector2 at, float density, float gravityScale)
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                StaticGroup fluid = world.AddStatic(new[] { Box(new Vector2(10f, 5f), new Vector2(0f, -5f), true) });
                world.AddEffector(fluid, new[] { 0 }, EffectorDesc2D.Buoyancy(0f, density, 0f, 0f, 0f, 0f));
                BodyHandle body = world.CreateBody(new BodyDesc2D(BodyKind.Dynamic, new[] { Box(new Vector2(0.5f), Vector2.Zero, false) }, at, 0f, 1f, new BodyMotion2D(BodyLocks2D.None, gravityScale, 0f, 0f)));
                world.Step(Tick);
                float before = world.GetBody(body).Velocity.Y;
                world.Step(Tick);
                return (world.GetBody(body).Velocity.Y - before) / Tick + (9.81f * gravityScale);
            }
        }

        private static BodyDesc2D Dynamic(Vector2 position, float mass) =>
            new BodyDesc2D(BodyKind.Dynamic, new[] { Box(new Vector2(0.5f), Vector2.Zero, false) }, position, 0f, mass, new BodyMotion2D(BodyLocks2D.None, 1f, 0f, 0f));

        private static ColliderDesc2D Box(Vector2 halfExtents, Vector2 position, bool isTrigger) =>
            new ColliderDesc2D(BodyShape2D.Box(halfExtents), position, 0f, ColliderMaterial.Default, 0, isTrigger);
    }
}
