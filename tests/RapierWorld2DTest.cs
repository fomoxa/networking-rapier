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
        public void FiltersChooseLayersAndTriggersAndSkipABody()
        {
            using (var world = new RapierWorld2D(Vector2.Zero))
            {
                BodyHandle trigger = world.CreateBody(Square(new Vector2(2f, 0f), 0, true));
                BodyHandle near = world.CreateBody(Square(new Vector2(5f, 0f), 0, false));
                BodyHandle far = world.CreateBody(Square(new Vector2(8f, 0f), 3, false));
                var solid = new QueryFilter(-1, default, false);
                BodyShape2D row = BodyShape2D.Box(new Vector2(5f, 1f));
                var middle = new Vector2(5f, 0f);
                var results = new BodyHandle[4];

                AssertRay(world, QueryFilter.All, trigger, 1.5f);
                AssertRay(world, solid, near, 4.5f);
                AssertRay(world, new QueryFilter(-1, near, false), far, 7.5f);
                AssertRay(world, new QueryFilter(1 << 3, default, true), far, 7.5f);
                Assert.IsFalse(world.Raycast(Vector2.Zero, Vector2.UnitX, 100f, new QueryFilter(1 << 4, default, true), out _));
                Assert.AreEqual(3, world.Overlap(row, middle, 0f, QueryFilter.All, results));
                Assert.AreEqual(2, world.Overlap(row, middle, 0f, solid, results));
                Assert.AreEqual(1, world.Overlap(row, middle, 0f, new QueryFilter(1, near, true), results));
                Assert.AreEqual(trigger.Value, results[0].Value);
            }
        }

        [Test]
        public void ShapeCastsAndOverlapsTakeABoxACircleAndACapsule()
        {
            using (var world = new RapierWorld2D(Vector2.Zero))
            {
                BodyHandle target = world.CreateBody(Square(new Vector2(5f, 0f), 0, false));
                var shapes = new[]
                {
                    (BodyShape2D.Box(new Vector2(0.5f, 0.5f)), 0f, 4f),
                    (BodyShape2D.Circle(0.5f), 0f, 4f),
                    (BodyShape2D.Capsule(0.5f, 0.5f), MathF.PI / 2f, 3.5f),
                };
                var results = new BodyHandle[4];

                foreach ((BodyShape2D shape, float rotation, float distance) in shapes)
                {
                    Assert.IsTrue(world.ShapeCast(shape, Vector2.Zero, rotation, Vector2.UnitX, 100f, QueryFilter.All, out RayHit2D hit), shape.Kind.ToString());
                    Assert.AreEqual(target.Value, hit.Body.Value);
                    Assert.AreEqual(distance, hit.Distance, 1e-4f, shape.Kind.ToString());
                    Assert.AreEqual(-1f, hit.Normal.X, 1e-4f);
                    Assert.AreEqual(1, world.Overlap(shape, new Vector2(4.2f, 0f), rotation, QueryFilter.All, results), shape.Kind.ToString());
                    Assert.AreEqual(0, world.Overlap(shape, new Vector2(4.2f, 2f), rotation, QueryFilter.All, results), shape.Kind.ToString());
                }

                BodyShape2D polygon = BodyShape2D.ConvexPolygon(new[] { Vector2.Zero, Vector2.UnitX, Vector2.UnitY });
                Assert.Throws<ArgumentException>(() => world.Overlap(polygon, Vector2.Zero, 0f, QueryFilter.All, results));
                Assert.Throws<ArgumentException>(() => world.ShapeCast(polygon, Vector2.Zero, 0f, Vector2.UnitX, 1f, QueryFilter.All, out _));
            }
        }

        [Test]
        public void AQueryStartingInsideAColliderHitsItAtDistanceZero()
        {
            using (var world = new RapierWorld2D(Vector2.Zero))
            {
                BodyHandle inside = world.CreateBody(Square(Vector2.Zero, 0, false));
                BodyHandle beyond = world.CreateBody(Square(new Vector2(5f, 0f), 0, false));
                var origin = new Vector2(0.1f, 0f);

                Assert.IsTrue(world.Raycast(origin, Vector2.UnitX, 100f, QueryFilter.All, out RayHit2D ray));
                Assert.IsTrue(world.ShapeCast(BodyShape2D.Circle(0.2f), origin, 0f, Vector2.UnitX, 100f, QueryFilter.All, out RayHit2D cast));
                Assert.IsTrue(world.Raycast(origin, Vector2.UnitX, 100f, new QueryFilter(-1, inside, true), out RayHit2D skipped));

                Assert.AreEqual((inside.Value, 0f), (ray.Body.Value, ray.Distance));
                Assert.AreEqual((inside.Value, 0f), (cast.Body.Value, cast.Distance));
                Assert.AreEqual(beyond.Value, skipped.Body.Value);
                Assert.AreEqual(4.4f, skipped.Distance, 1e-4f);
            }
        }

        [Test]
        public void QueriesSeeBodiesAndStaticsChangedSinceTheLastStep()
        {
            using (var world = new RapierWorld2D(Vector2.Zero))
            {
                BodyHandle body = world.CreateBody(Square(new Vector2(5f, 0f), 0, false));
                world.Step(Tick);
                PhysicsSnapshot snapshot = world.CreateSnapshot();
                world.Save(snapshot);

                world.SetBody(body, new BodyState2D { Position = new Vector2(10f, 0f) });
                AssertRay(world, QueryFilter.All, body, 9.5f);
                world.Load(snapshot);
                AssertRay(world, QueryFilter.All, body, 4.5f);
                StaticGroup wall = world.AddStatic(new[] { new ColliderDesc2D(BodyShape2D.Box(new Vector2(0.5f, 1f)), new Vector2(3f, 0f), 0f, ColliderMaterial.Default, 0, false) });
                AssertRay(world, QueryFilter.All, default, 2.5f);
                world.RemoveStatic(wall);
                world.RemoveBody(body);
                Assert.IsFalse(world.Raycast(Vector2.Zero, Vector2.UnitX, 100f, QueryFilter.All, out _));
            }
        }

        [Test]
        public void QueriesGiveTheSameAnswersBeforeAndAfterTheStepThatIndexesTheBodies()
        {
            using (var world = new RapierWorld2D(Vector2.Zero))
            {
                BodyShape2D[] shapes = { BodyShape2D.Box(new Vector2(0.4f, 0.3f)), BodyShape2D.Circle(0.45f), BodyShape2D.Capsule(0.3f, 0.4f) };
                for (int index = 0; index < 64; index++)
                {
                    var position = new Vector2(index % 8 * 1.5f, index / 8 * 1.5f);
                    var collider = new ColliderDesc2D(shapes[index % 3], Vector2.Zero, 0f, ColliderMaterial.Default, 0, false);
                    world.CreateBody(new BodyDesc2D(BodyKind.Kinematic, new[] { collider }, position, index * 0.125f, 1f));
                }

                List<string> before = QueryAnswers(world);
                world.Step(Tick);

                CollectionAssert.AreEqual(before, QueryAnswers(world));
            }
        }

        [Test]
        public void AFallingBodyIsMovedUntilItSleepsAndAWrittenOrLoadedBodyIsMovedOnce()
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                world.AddStatic(new[] { Ground() });
                BodyHandle ball = world.CreateBody(Circle(new Vector2(0f, 2f), 1f));
                BodyHandle parked = world.CreateBody(Circle(new Vector2(5f, 0.5f), 1f));
                world.SetKind(parked, BodyKind.Kinematic);
                BodyHandle fixedBody = world.CreateBody(Circle(new Vector2(8f, 0.5f), 1f));
                world.SetKind(fixedBody, BodyKind.Static);
                BodyHandle removed = world.CreateBody(Circle(new Vector2(-5f, 2f), 1f));
                PhysicsSnapshot snapshot = world.CreateSnapshot();
                world.Save(snapshot);

                world.Step(Tick);
                List<BodyHandle> falling = Moved(world);
                world.SetBody(removed, new BodyState2D { Position = new Vector2(-5f, 3f) });
                world.RemoveBody(removed);
                List<BodyHandle> afterRemove = Moved(world);
                Run(world, 600);
                Moved(world);
                world.Step(Tick);
                List<BodyHandle> resting = Moved(world);
                world.SetBody(parked, new BodyState2D { Position = new Vector2(5f, 1f) });
                world.SetBody(fixedBody, new BodyState2D { Position = new Vector2(8f, 1f) });
                world.Step(Tick);
                List<BodyHandle> written = Moved(world);
                Run(world, 120);
                Moved(world);
                world.Step(Tick);
                List<BodyHandle> asleepAgain = Moved(world);
                world.Load(snapshot);
                List<BodyHandle> loaded = Moved(world);

                CollectionAssert.AreEquivalent(new[] { ball, parked, fixedBody, removed }, falling);
                CollectionAssert.DoesNotContain(afterRemove, removed);
                CollectionAssert.IsEmpty(resting);
                CollectionAssert.AreEquivalent(new[] { parked, fixedBody }, written);
                CollectionAssert.IsEmpty(asleepAgain);
                CollectionAssert.AreEquivalent(new[] { ball, parked, fixedBody }, loaded);
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

        [Test]
        public void ImpulsesChangeTheVelocityAtOnceAndForcesAndTorquesActAtTheNextStep()
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                const float inertia = 0.5f * 2f * 0.5f * 0.5f;
                const float seconds = 0.1f;
                BodyHandle spun = world.CreateBody(Disc(BodyKind.Dynamic, 0f));
                BodyHandle struck = world.CreateBody(Disc(BodyKind.Dynamic, 10f));
                BodyHandle twisted = world.CreateBody(Disc(BodyKind.Dynamic, 20f));
                BodyHandle pushed = world.CreateBody(Disc(BodyKind.Dynamic, 30f));
                BodyHandle carried = world.CreateBody(Disc(BodyKind.Kinematic, 40f));

                world.AddTorqueImpulse(spun, 0.5f);
                world.AddImpulseAtPoint(struck, new Vector2(0f, 1f), world.GetBody(struck).Position + new Vector2(0.5f, 0f));
                world.AddTorque(twisted, 1f);
                world.AddForceAtPoint(pushed, new Vector2(0f, 1f), world.GetBody(pushed).Position + new Vector2(0.5f, 0f));
                world.AddTorqueImpulse(carried, 0.5f);
                world.AddImpulseAtPoint(carried, new Vector2(0f, 1f), world.GetBody(carried).Position + new Vector2(0.5f, 0f));

                Assert.AreEqual(0.5f / inertia, world.GetBody(spun).AngularVelocity, 1e-4f);
                Assert.AreEqual(0.5f, world.GetBody(struck).Velocity.Y, 1e-5f);
                Assert.AreEqual(0.5f / inertia, world.GetBody(struck).AngularVelocity, 1e-4f);
                Assert.AreEqual(0f, world.GetBody(twisted).AngularVelocity);
                Assert.AreEqual(Vector2.Zero, world.GetBody(pushed).Velocity);

                world.Step(seconds);

                Assert.AreEqual(seconds / inertia, world.GetBody(twisted).AngularVelocity, 1e-3f);
                Assert.AreEqual(seconds / 2f, world.GetBody(pushed).Velocity.Y, 1e-4f);
                Assert.AreEqual(0.5f * seconds / inertia, world.GetBody(pushed).AngularVelocity, 1e-3f);

                world.Step(seconds);

                Assert.AreEqual(seconds / inertia, world.GetBody(twisted).AngularVelocity, 1e-3f);
                Assert.AreEqual(seconds / 2f, world.GetBody(pushed).Velocity.Y, 1e-4f);
                Assert.AreEqual(Vector2.Zero, world.GetBody(carried).Velocity);
                Assert.AreEqual(0f, world.GetBody(carried).AngularVelocity);
            }
        }

        [Test]
        public void OnlyContinuousCollisionStopsAFastBodyAtAThinKinematicWallAndAThinStaticWallAlwaysStopsIt()
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                world.CreateBody(new BodyDesc2D(BodyKind.Kinematic, new[] { Wall(Vector2.Zero) }, new Vector2(12f, 0f), 0f, 0f, new BodyMotion2D(BodyLocks2D.None, 0f, 0f, 0f)));
                world.AddStatic(new[] { Wall(new Vector2(12f, 10f)) });
                BodyHandle continuous = Bullet(world, -1f, true);
                BodyHandle discrete = Bullet(world, 1f, false);
                BodyHandle againstStatic = Bullet(world, 10f, false);

                Run(world, 3);

                Assert.Less(world.GetBody(continuous).Position.X, 12f);
                Assert.Greater(world.GetBody(discrete).Position.X, 14f);
                Assert.Less(world.GetBody(againstStatic).Position.X, 12f);
            }
        }

        private static BodyDesc2D Disc(BodyKind kind, float x) =>
            new BodyDesc2D(kind, new[] { new ColliderDesc2D(BodyShape2D.Circle(0.5f), Vector2.Zero, 0f, ColliderMaterial.Default, 0, false) }, new Vector2(x, 0f), 0f, 2f, new BodyMotion2D(BodyLocks2D.None, 0f, 0f, 0f));

        private static ColliderDesc2D Wall(Vector2 position) =>
            new ColliderDesc2D(BodyShape2D.Box(new Vector2(0.05f, 2f)), position, 0f, ColliderMaterial.Default, 0, false);

        private static BodyHandle Bullet(RapierWorld2D world, float y, bool continuous)
        {
            var motion = new BodyMotion2D(BodyLocks2D.None, 0f, 0f, 0f, continuous);
            BodyHandle bullet = world.CreateBody(new BodyDesc2D(BodyKind.Dynamic, new[] { new ColliderDesc2D(BodyShape2D.Circle(0.1f), Vector2.Zero, 0f, ColliderMaterial.Default, 0, false) }, new Vector2(0f, y), 0f, 1f, motion));
            world.AddImpulse(bullet, new Vector2(300f, 0f));
            return bullet;
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

        private static List<BodyHandle> Moved(RapierWorld2D world)
        {
            var moved = new List<BodyHandle>();
            world.CollectMoved(moved);
            return moved;
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

        private static BodyDesc2D Square(Vector2 position, int layer, bool trigger) =>
            new BodyDesc2D(BodyKind.Kinematic, new[] { new ColliderDesc2D(BodyShape2D.Box(new Vector2(0.5f, 0.5f)), Vector2.Zero, 0f, ColliderMaterial.Default, layer, trigger) }, position, 0f, 1f);

        private static void AssertRay(RapierWorld2D world, in QueryFilter filter, BodyHandle body, float distance)
        {
            Assert.IsTrue(world.Raycast(Vector2.Zero, Vector2.UnitX, 100f, filter, out RayHit2D hit));
            Assert.AreEqual(body.Value, hit.Body.Value);
            Assert.AreEqual(distance, hit.Distance, 1e-4f);
        }

        private static List<string> QueryAnswers(RapierWorld2D world)
        {
            var answers = new List<string>();
            var found = new BodyHandle[64];
            for (int index = 0; index < 16; index++)
            {
                var origin = new Vector2(index * 0.7f - 1f, 20f);
                var direction = new Vector2(0.1f, -1f);
                world.Raycast(origin, direction, 30f, QueryFilter.All, out RayHit2D ray);
                world.ShapeCast(BodyShape2D.Circle(0.3f), origin, 0f, direction, 30f, QueryFilter.All, out RayHit2D cast);
                int count = world.Overlap(BodyShape2D.Box(new Vector2(0.7f, 0.7f)), new Vector2(index * 0.7f, index * 0.6f), 0.25f, QueryFilter.All, found);
                var bodies = new int[count];
                for (int slot = 0; slot < count; slot++)
                {
                    bodies[slot] = found[slot].Value;
                }

                answers.Add($"{ray.Body.Value} {ray.Distance:R} {ray.Point} {ray.Normal} {cast.Body.Value} {cast.Distance:R} {cast.Point} {cast.Normal} {string.Join(",", bodies)}");
            }

            return answers;
        }

        private static BodyDesc2D Circle(Vector2 position, float mass, int layer = 0) =>
            new BodyDesc2D(BodyKind.Dynamic, new[] { new ColliderDesc2D(BodyShape2D.Circle(0.5f), Vector2.Zero, 0f, ColliderMaterial.Default, layer, false) }, position, 0f, mass);
    }
}
