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
