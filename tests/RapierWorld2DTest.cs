using System;
using System.Collections.Generic;
using System.Numerics;
using Fomoxa.Networking.Simulation;
using NUnit.Framework;

namespace Fomoxa.Networking.Rapier.Tests
{
    public sealed class RapierWorld2DTest
    {
        private const float Tick = 1f / 60f;
        private const ulong PileStateHash = 0x8DC0037A8661A2C5UL;
        private static readonly Vector2 Gravity = new Vector2(0f, -9.81f);

        [Test]
        public void ACircleFallsOntoAPolylineAndHasTheGivenMass()
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                world.AddStatic(new[] { Line(0) });
                BodyHandle ball = world.CreateBody(Circle(new Vector2(0f, 5f), 3f));

                Run(world, 300);

                Assert.AreEqual(0.5f, world.GetBody(ball).Position.Y, 0.05f);
                Assert.AreEqual(3f, world.GetMass(ball), 1e-4f);
                Assert.AreEqual(BodyKind.Dynamic, world.GetKind(ball));
            }
        }

        [Test]
        public void ABodyOfConvexPiecesLandsOnABox()
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                world.AddStatic(new[] { new ColliderDesc2D(BodyShape2D.Box(new Vector2(10f, 0.5f)), new Vector2(0f, -0.5f), 0f, ColliderMaterial.Default, 0, false) });
                ColliderDesc2D[] pieces =
                {
                    new ColliderDesc2D(BodyShape2D.ConvexPolygon(new[] { new Vector2(0f, 0f), new Vector2(2f, 0f), new Vector2(2f, 1f), new Vector2(0f, 1f) }), Vector2.Zero, 0f, ColliderMaterial.Default, 0, false),
                    new ColliderDesc2D(BodyShape2D.ConvexPolygon(new[] { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 2f), new Vector2(0f, 2f) }), Vector2.Zero, 0f, ColliderMaterial.Default, 0, false),
                };
                BodyHandle shape = world.CreateBody(new BodyDesc2D(BodyKind.Dynamic, pieces, new Vector2(-1f, 3f), 0f, 3f));

                Run(world, 300);

                BodyState2D state = world.GetBody(shape);
                Assert.AreEqual(0f, state.Position.Y, 0.05f);
                Assert.AreEqual(0f, state.Rotation, 1e-3f);
                Assert.AreEqual(3f, world.GetMass(shape), 1e-4f);
            }
        }

        [Test]
        public void AForceLastsOneStepAndAnImpulseAndASpinChangeTheBodyAtOnce()
        {
            using (var world = new RapierWorld2D(Vector2.Zero))
            {
                BodyHandle ball = world.CreateBody(Circle(Vector2.Zero, 2f));

                world.AddImpulse(ball, new Vector2(4f, 0f));
                float afterImpulse = world.GetBody(ball).Velocity.X;
                world.AddForce(ball, new Vector2(120f, 0f));
                world.Step(Tick);
                float afterForce = world.GetBody(ball).Velocity.X;
                world.SetBody(ball, new BodyState2D { Position = Vector2.Zero, Rotation = 0f, AngularVelocity = 6f });
                Run(world, 15);

                Assert.AreEqual(2f, afterImpulse, 1e-4f);
                Assert.AreEqual(3f, afterForce, 1e-4f);
                Assert.AreEqual(1.5f, world.GetBody(ball).Rotation, 1e-3f);
            }
        }

        [Test]
        public void LoadingASnapshotReplaysTheSameStepsAndKeepsBodiesCreatedSince()
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                world.AddStatic(new[] { Line(0) });
                BodyHandle removed = default;
                for (int index = 0; index < 5; index++)
                {
                    removed = world.CreateBody(Circle(new Vector2(index * 0.3f, 2f + index), 1f));
                }

                Run(world, 30);
                PhysicsSnapshot snapshot = world.CreateSnapshot();
                world.Save(snapshot);
                Run(world, 60);
                ulong first = world.StateHash;
                world.Load(snapshot);
                Run(world, 60);
                ulong replayed = world.StateHash;
                world.RemoveBody(removed);
                BodyHandle added = world.CreateBody(Circle(new Vector2(8f, 1f), 1f));

                world.Load(snapshot);

                Assert.AreEqual(first, replayed);
                Assert.IsFalse(world.Contains(removed));
                Assert.IsTrue(world.Contains(added));
                Assert.AreEqual(8f, world.GetBody(added).Position.X, 1e-4f);
            }
        }

        [Test]
        public void APileReachesTheSameStateOnEveryPlatform()
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                world.AddStatic(new[] { Line(0) });
                for (int index = 0; index < 20; index++)
                {
                    world.CreateBody(new BodyDesc2D(BodyKind.Dynamic, BodyShape2D.Box(new Vector2(0.4f)), new Vector2(index % 4 * 0.5f, 1f + index * 0.9f), index * 0.3f, 1f));
                }

                Run(world, 240);

                TestContext.Out.WriteLine($"2D pile state hash 0x{world.StateHash:X16}");
                Assert.AreEqual(PileStateHash, world.StateHash);
            }
        }

        [Test]
        public void LayersAndTriggersLetBodiesThroughAndQueriesFindBodies()
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                var masks = new uint[32];
                for (int layer = 0; layer < 32; layer++)
                {
                    masks[layer] = uint.MaxValue;
                }

                masks[1] &= ~(1u << 2);
                masks[2] &= ~(1u << 1);
                world.SetLayerCollisions(masks);
                world.AddStatic(new[] { Line(1), new ColliderDesc2D(BodyShape2D.Box(new Vector2(1f, 0.5f)), new Vector2(30f, -0.5f), 0f, ColliderMaterial.Default, 0, true) });
                BodyHandle ghost = world.CreateBody(Circle(new Vector2(0f, 2f), 1f, layer: 2));
                BodyHandle solid = world.CreateBody(Circle(new Vector2(3f, 2f), 1f, layer: 3));
                BodyHandle triggered = world.CreateBody(Circle(new Vector2(30f, 2f), 1f));
                var found = new BodyHandle[4];

                Assert.IsTrue(world.Raycast(new Vector2(3f, 10f), new Vector2(0f, -1f), 100f, out RayHit2D hit));
                int count = world.Overlap(new Vector2(3f, 2f), 0.1f, found);
                Run(world, 120);

                Assert.AreEqual((solid.Value, 2.5f), (hit.Body.Value, hit.Point.Y));
                Assert.AreEqual(1, count);
                Assert.AreEqual(solid.Value, found[0].Value);
                Assert.Less(world.GetBody(ghost).Position.Y, -1f);
                Assert.AreEqual(0.5f, world.GetBody(solid).Position.Y, 0.05f);
                Assert.Less(world.GetBody(triggered).Position.Y, -1f);
            }
        }

        [Test]
        public void ADisposedWorldHasNoBodiesAndRefusesNewOnes()
        {
            var world = new RapierWorld2D(Gravity);
            BodyHandle ball = world.CreateBody(Circle(Vector2.Zero, 1f));

            world.Dispose();

            Assert.IsFalse(world.Contains(ball));
            Assert.IsFalse(new PhysicsBody2D(world, ball).IsValid);
            Assert.Throws<ArgumentException>(() => world.CreateBody(Circle(Vector2.Zero, 1f)));
        }

        [Test]
        public void TouchingSetsFollowContactsSensorsKinematicBodiesAndLoad()
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                world.AddStatic(new[] { Line(0), new ColliderDesc2D(BodyShape2D.Box(new Vector2(1f, 1f)), new Vector2(20f, 5f), 0f, ColliderMaterial.Default, 0, true) });
                BodyHandle ball = world.CreateBody(Circle(new Vector2(0f, 2f), 1f));
                BodyHandle carried = world.CreateBody(new BodyDesc2D(BodyKind.Kinematic, BodyShape2D.Circle(0.5f), new Vector2(20.5f, 5f), 0f, 0f));

                Run(world, 120);

                CollectionAssert.AreEqual(new[] { RapierCollider.Static(0) }, Touching(world, RapierCollider.OfBody(ball, 0)));
                CollectionAssert.AreEqual(new[] { RapierCollider.OfBody(carried, 0) }, Touching(world, RapierCollider.Static(1)));

                PhysicsSnapshot snapshot = world.CreateSnapshot();
                world.Save(snapshot);
                world.SetBody(carried, new BodyState2D { Position = new Vector2(40f, 5f) });
                Run(world, 2);

                CollectionAssert.IsEmpty(Touching(world, RapierCollider.Static(1)));

                world.Load(snapshot);

                CollectionAssert.AreEqual(new[] { RapierCollider.OfBody(carried, 0) }, Touching(world, RapierCollider.Static(1)));
            }
        }

        [Test]
        public void AStaticGroupHoldsABallUntilItIsRemovedAndItsIndicesAreNotReused()
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                StaticGroup ground = world.AddStatic(new[] { Ground() });
                BodyHandle ball = world.CreateBody(Circle(new Vector2(0f, 2f), 1f));

                Run(world, 120);

                Assert.IsTrue(ground.IsValid);
                Assert.AreEqual((0, 1), (ground.FirstIndex, ground.Count));
                Assert.AreEqual(0.5f, world.GetBody(ball).Position.Y, 0.05f);
                CollectionAssert.AreEqual(new[] { RapierCollider.Static(0) }, Touching(world, RapierCollider.OfBody(ball, 0)));

                world.RemoveStatic(ground);
                world.RemoveStatic(ground);
                Run(world, 60);

                Assert.Less(world.GetBody(ball).Position.Y, -1f);
                StaticGroup again = world.AddStatic(new[] { Ground() });
                StaticGroup empty = world.AddStatic(Array.Empty<ColliderDesc2D>());
                Assert.AreEqual((1, 1), (again.FirstIndex, again.Count));
                Assert.AreEqual((2, 0), (empty.FirstIndex, empty.Count));
                Assert.AreNotEqual(again.Id, ground.Id);
                Assert.AreNotEqual(empty.Id, again.Id);
            }
        }

        [Test]
        public void LoadKeepsAStaticGroupAddedSinceTheSnapshotAndDropsOneRemovedSince()
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                PhysicsSnapshot before = world.CreateSnapshot();
                world.Save(before);
                StaticGroup ground = world.AddStatic(new[] { Ground() });

                world.Load(before);

                Assert.IsTrue(world.Raycast(new Vector2(0f, 5f), new Vector2(0f, -1f), 10f, out RayHit2D kept));
                Assert.AreEqual(0f, kept.Point.Y, 1e-4f);
                BodyHandle ball = world.CreateBody(Circle(new Vector2(0f, 2f), 1f));
                Run(world, 120);
                CollectionAssert.AreEqual(new[] { RapierCollider.Static(0) }, Touching(world, RapierCollider.OfBody(ball, 0)));

                PhysicsSnapshot withGround = world.CreateSnapshot();
                world.Save(withGround);
                world.RemoveStatic(ground);
                world.Load(withGround);

                Assert.IsFalse(world.Raycast(new Vector2(0f, 5f), new Vector2(0f, -10f), 3f, out _));
                Run(world, 60);
                Assert.Less(world.GetBody(ball).Position.Y, -1f);
            }
        }

        [Test]
        public void TheMotionOfABodyLocksScalesGravityAndDamps()
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                BodyHandle free = world.CreateBody(Box(new Vector2(0f, 0f), BodyMotion2D.Default));
                BodyHandle heavy = world.CreateBody(Box(new Vector2(10f, 0f), new BodyMotion2D(BodyLocks2D.None, 1.5f, 0f, 0f)));
                BodyHandle upright = world.CreateBody(Box(new Vector2(20f, 0f), new BodyMotion2D(BodyLocks2D.Rotation | BodyLocks2D.PositionX, 1f, 0f, 0f)));
                BodyHandle damped = world.CreateBody(Box(new Vector2(30f, 0f), new BodyMotion2D(BodyLocks2D.None, 0f, 1f, 0f)));
                world.SetBody(upright, new BodyState2D { Position = new Vector2(20f, 0f), Velocity = new Vector2(5f, 0f), AngularVelocity = 4f });
                world.SetBody(free, new BodyState2D { AngularVelocity = 4f });
                world.SetBody(damped, new BodyState2D { Position = new Vector2(30f, 0f), Velocity = new Vector2(6f, 0f) });

                Run(world, 30);

                Assert.AreEqual(1.5f, world.GetBody(heavy).Position.Y / world.GetBody(free).Position.Y, 1e-3f);
                Assert.AreEqual(2f, world.GetBody(free).Rotation, 0.05f);
                Assert.AreEqual((20f, 0f), (world.GetBody(upright).Position.X, world.GetBody(upright).Rotation));
                Assert.AreEqual(world.GetBody(free).Position.Y, world.GetBody(upright).Position.Y, 1e-4f);
                Assert.AreEqual(0f, world.GetBody(damped).Position.Y);
                Assert.Less(world.GetBody(damped).Velocity.X, 6f * 0.65f);
                Assert.Greater(world.GetBody(damped).Velocity.X, 6f * 0.55f);
            }
        }

        [Test]
        public void ABodyCreatedAfterTheSnapshotKeepsItsMotionWhenLoadPutsItBack()
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                PhysicsSnapshot before = world.CreateSnapshot();
                world.Save(before);
                BodyHandle upright = world.CreateBody(Box(Vector2.Zero, new BodyMotion2D(BodyLocks2D.Rotation, 0f, 0f, 0f)));
                world.SetBody(upright, new BodyState2D { AngularVelocity = 4f });

                world.Load(before);
                world.SetBody(upright, new BodyState2D { AngularVelocity = 4f });
                Run(world, 30);

                BodyState2D state = world.GetBody(upright);
                Assert.AreEqual((0f, 0f), (state.Rotation, state.Position.Y));
            }
        }

        private static ColliderDesc2D Ground() =>
            new ColliderDesc2D(BodyShape2D.Box(new Vector2(10f, 0.5f)), new Vector2(0f, -0.5f), 0f, ColliderMaterial.Default, 0, false);

        private static BodyDesc2D Box(Vector2 position, BodyMotion2D motion) =>
            new BodyDesc2D(BodyKind.Dynamic, new[] { new ColliderDesc2D(BodyShape2D.Box(new Vector2(0.5f, 0.5f)), Vector2.Zero, 0f, ColliderMaterial.Default, 0, false) }, position, 0f, 1f, motion);

        private static List<RapierCollider> Touching(RapierWorld2D world, RapierCollider collider)
        {
            var touching = new List<RapierCollider>();
            world.Touching(collider, touching);
            return touching;
        }

        private static void Run(RapierWorld2D world, int ticks)
        {
            for (int tick = 0; tick < ticks; tick++)
            {
                world.Step(Tick);
            }
        }

        private static ColliderDesc2D Line(int layer) =>
            new ColliderDesc2D(BodyShape2D.Polyline(new[] { new Vector2(-10f, 0f), new Vector2(10f, 0f) }), Vector2.Zero, 0f, ColliderMaterial.Default, layer, false);

        private static BodyDesc2D Circle(Vector2 position, float mass, int layer = 0) =>
            new BodyDesc2D(BodyKind.Dynamic, new[] { new ColliderDesc2D(BodyShape2D.Circle(0.5f), Vector2.Zero, 0f, ColliderMaterial.Default, layer, false) }, position, 0f, mass);
    }
}
