using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Fomoxa.Networking.Simulation;
using NUnit.Framework;

namespace Fomoxa.Networking.Rapier.Tests
{
    public sealed class RapierWorldTest
    {
        private const float Tick = 1f / 60f;
        private const ulong PileStateHash = 0xF1860E86303F3AEDUL;
        private static readonly Vector3 Gravity = new Vector3(0f, -9.81f, 0f);

        [Test]
        public void ADynamicBodyFallsOntoAStaticGroundAndHasTheGivenMass()
        {
            using (var world = new RapierWorld(Gravity))
            {
                world.AddStatic(new[] { Ground(0) });
                BodyHandle ball = world.CreateBody(Ball(new Vector3(0f, 5f, 0f), 2f));

                Run(world, 300);

                Assert.AreEqual(0.5f, world.GetBody(ball).Position.Y, 0.05f);
                Assert.AreEqual(2f, world.GetMass(ball), 1e-4f);
                Assert.AreEqual(BodyKind.Dynamic, world.GetKind(ball));
            }
        }

        [Test]
        public void AForceLastsOneStepAndAnImpulseChangesTheVelocityAtOnce()
        {
            using (var world = new RapierWorld(Vector3.Zero))
            {
                BodyHandle ball = world.CreateBody(Ball(Vector3.Zero, 2f));

                world.AddImpulse(ball, new Vector3(4f, 0f, 0f));
                float afterImpulse = world.GetBody(ball).Velocity.X;
                world.AddForce(ball, new Vector3(120f, 0f, 0f));
                world.Step(Tick);
                float afterForce = world.GetBody(ball).Velocity.X;
                world.Step(Tick);

                Assert.AreEqual(2f, afterImpulse, 1e-4f);
                Assert.AreEqual(3f, afterForce, 1e-4f);
                Assert.AreEqual(3f, world.GetBody(ball).Velocity.X, 1e-4f);
            }
        }

        [Test]
        public void LoadingASnapshotReplaysTheSameSteps()
        {
            using (var world = new RapierWorld(Gravity))
            {
                world.AddStatic(new[] { Ground(0) });
                for (int index = 0; index < 5; index++)
                {
                    world.CreateBody(Ball(new Vector3(index * 0.3f, 2f + index, 0f), 1f));
                }

                Run(world, 30);
                PhysicsSnapshot snapshot = world.CreateSnapshot();
                world.Save(snapshot);
                Run(world, 60);
                ulong first = world.StateHash;

                world.Load(snapshot);
                Run(world, 60);

                Assert.AreEqual(first, world.StateHash);
            }
        }

        [Test]
        public void LoadKeepsBodiesCreatedAfterTheSnapshotAndDoesNotRecreateRemovedOnes()
        {
            using (var world = new RapierWorld(Vector3.Zero))
            {
                BodyHandle kept = world.CreateBody(Ball(Vector3.Zero, 1f));
                BodyHandle removed = world.CreateBody(Ball(new Vector3(5f, 0f, 0f), 1f));
                world.SetBody(kept, new BodyState { Position = Vector3.Zero, Rotation = Quaternion.Identity, Velocity = Vector3.UnitX });
                PhysicsSnapshot snapshot = world.CreateSnapshot();
                world.Save(snapshot);
                world.Step(1f);
                world.RemoveBody(removed);
                BodyHandle added = world.CreateBody(Ball(new Vector3(-5f, 0f, 0f), 1f));
                world.SetBody(added, new BodyState { Position = new Vector3(-6f, 0f, 0f), Rotation = Quaternion.Identity });

                world.Load(snapshot);

                Assert.AreEqual(0f, world.GetBody(kept).Position.X, 1e-5f);
                Assert.IsFalse(world.Contains(removed));
                Assert.IsTrue(world.Contains(added));
                Assert.AreEqual(-6f, world.GetBody(added).Position.X, 1e-5f);
                Assert.AreNotEqual(kept.Value, added.Value);
                Assert.AreNotEqual(removed.Value, added.Value);
            }
        }

        [Test]
        public void ABodyThatIsNotRewindableKeepsItsCurrentStateOnLoad()
        {
            using (var world = new RapierWorld(Vector3.Zero))
            {
                BodyHandle proxy = world.CreateBody(Ball(Vector3.Zero, 1f));
                world.SetKind(proxy, BodyKind.Kinematic);
                world.SetRewindable(proxy, false);
                PhysicsSnapshot snapshot = world.CreateSnapshot();
                world.Save(snapshot);
                world.SetBody(proxy, new BodyState { Position = new Vector3(3f, 0f, 0f), Rotation = Quaternion.Identity });

                world.Load(snapshot);

                Assert.AreEqual(3f, world.GetBody(proxy).Position.X, 1e-5f);
                Assert.AreEqual(BodyKind.Kinematic, world.GetKind(proxy));
            }
        }

        [Test]
        public void TwoWorldsGivenTheSameCommandsReachTheSameState()
        {
            using (var first = Pile())
            using (var second = Pile())
            {
                Run(first, 240);
                Run(second, 240);

                Assert.AreEqual(first.StateHash, second.StateHash);
            }
        }

        [Test]
        public void APileReachesTheSameStateOnEveryPlatform()
        {
            using (RapierWorld world = Pile())
            {
                Run(world, 240);

                TestContext.Out.WriteLine($"pile state hash 0x{world.StateHash:X16}");
                Assert.AreEqual(PileStateHash, world.StateHash);
            }
        }

        [Test]
        public void LayersThatDoNotCollideAndTriggersLetBodiesThrough()
        {
            using (var world = new RapierWorld(Gravity))
            {
                var masks = new uint[32];
                for (int layer = 0; layer < 32; layer++)
                {
                    masks[layer] = uint.MaxValue;
                }

                masks[1] &= ~(1u << 2);
                masks[2] &= ~(1u << 1);
                world.SetLayerCollisions(masks);
                world.AddStatic(new[] { Ground(1) });
                BodyHandle ghost = world.CreateBody(Ball(new Vector3(0f, 2f, 0f), 1f, layer: 2));
                BodyHandle solid = world.CreateBody(Ball(new Vector3(3f, 2f, 0f), 1f, layer: 3));
                world.AddStatic(new[] { new ColliderDesc(BodyShape.Box(new Vector3(1f, 0.5f, 1f)), new Vector3(30f, -0.5f, 0f), Quaternion.Identity, ColliderMaterial.Default, 0, true) });
                BodyHandle triggered = world.CreateBody(Ball(new Vector3(30f, 2f, 0f), 1f));

                Run(world, 120);

                Assert.Less(world.GetBody(ghost).Position.Y, -1f);
                Assert.AreEqual(0.5f, world.GetBody(solid).Position.Y, 0.05f);
                Assert.Less(world.GetBody(triggered).Position.Y, -1f);
            }
        }

        [Test]
        public void RaysAndOverlapsFindBodies()
        {
            using (var world = new RapierWorld(Vector3.Zero))
            {
                world.AddStatic(new[] { Ground(0) });
                BodyHandle ball = world.CreateBody(Ball(new Vector3(0f, 3f, 0f), 1f));
                BodyHandle other = world.CreateBody(Ball(new Vector3(0.6f, 3f, 0f), 1f));
                var found = new BodyHandle[4];

                Assert.IsTrue(world.Raycast(new Vector3(0f, 10f, 0f), new Vector3(0f, -2f, 0f), 100f, out RayHit hit));
                int count = world.Overlap(new Vector3(0.3f, 3f, 0f), 0.2f, found);

                Assert.AreEqual(ball.Value, hit.Body.Value);
                Assert.AreEqual(3.5f, hit.Point.Y, 1e-4f);
                Assert.AreEqual(6.5f, hit.Distance, 1e-4f);
                Assert.AreEqual(1f, hit.Normal.Y, 1e-4f);
                Assert.AreEqual(2, count);
                CollectionAssert.AreEqual(new[] { ball.Value, other.Value }, new[] { found[0].Value, found[1].Value });
                Assert.IsFalse(world.Raycast(new Vector3(0f, 10f, 0f), Vector3.UnitY, 100f, out _));
            }
        }

        [Test]
        public void FiltersChooseLayersAndTriggersAndSkipABody()
        {
            using (var world = new RapierWorld(Vector3.Zero))
            {
                BodyHandle trigger = world.CreateBody(Cube(new Vector3(0f, 0f, 2f), 0, true));
                BodyHandle near = world.CreateBody(Cube(new Vector3(0f, 0f, 5f), 0, false));
                BodyHandle far = world.CreateBody(Cube(new Vector3(0f, 0f, 8f), 3, false));
                var solid = new QueryFilter(-1, default, false);
                BodyShape column = BodyShape.Box(new Vector3(1f, 1f, 5f));
                var middle = new Vector3(0f, 0f, 5f);
                var results = new BodyHandle[4];

                AssertRay(world, QueryFilter.All, trigger, 1.5f);
                AssertRay(world, solid, near, 4.5f);
                AssertRay(world, new QueryFilter(-1, near, false), far, 7.5f);
                AssertRay(world, new QueryFilter(1 << 3, default, true), far, 7.5f);
                Assert.IsFalse(world.Raycast(Vector3.Zero, Vector3.UnitZ, 100f, new QueryFilter(1 << 4, default, true), out _));
                Assert.AreEqual(3, world.Overlap(column, middle, Quaternion.Identity, QueryFilter.All, results));
                Assert.AreEqual(2, world.Overlap(column, middle, Quaternion.Identity, solid, results));
                Assert.AreEqual(1, world.Overlap(column, middle, Quaternion.Identity, new QueryFilter(1, near, true), results));
                Assert.AreEqual(trigger.Value, results[0].Value);
            }
        }

        [Test]
        public void ShapeCastsAndOverlapsTakeABoxASphereAndACapsule()
        {
            using (var world = new RapierWorld(Vector3.Zero))
            {
                BodyHandle target = world.CreateBody(Cube(new Vector3(0f, 0f, 5f), 0, false));
                Quaternion lying = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f);
                var shapes = new[]
                {
                    (BodyShape.Box(new Vector3(0.5f, 0.5f, 0.5f)), Quaternion.Identity, 4f),
                    (BodyShape.Sphere(0.5f), Quaternion.Identity, 4f),
                    (BodyShape.Capsule(0.5f, 0.5f), lying, 3.5f),
                };
                var results = new BodyHandle[4];

                foreach ((BodyShape shape, Quaternion rotation, float distance) in shapes)
                {
                    Assert.IsTrue(world.ShapeCast(shape, Vector3.Zero, rotation, Vector3.UnitZ, 100f, QueryFilter.All, out RayHit hit), shape.Kind.ToString());
                    Assert.AreEqual(target.Value, hit.Body.Value);
                    Assert.AreEqual(distance, hit.Distance, 1e-4f, shape.Kind.ToString());
                    Assert.AreEqual(-1f, hit.Normal.Z, 1e-4f);
                    Assert.AreEqual(1, world.Overlap(shape, new Vector3(0f, 0f, 4.2f), rotation, QueryFilter.All, results), shape.Kind.ToString());
                    Assert.AreEqual(0, world.Overlap(shape, new Vector3(0f, 2f, 4.2f), rotation, QueryFilter.All, results), shape.Kind.ToString());
                }

                BodyShape hull = BodyShape.ConvexHull(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ });
                Assert.Throws<ArgumentException>(() => world.Overlap(hull, Vector3.Zero, Quaternion.Identity, QueryFilter.All, results));
                Assert.Throws<ArgumentException>(() => world.ShapeCast(hull, Vector3.Zero, Quaternion.Identity, Vector3.UnitZ, 1f, QueryFilter.All, out _));
            }
        }

        [Test]
        public void AQueryStartingInsideAColliderHitsItAtDistanceZero()
        {
            using (var world = new RapierWorld(Vector3.Zero))
            {
                BodyHandle inside = world.CreateBody(Cube(Vector3.Zero, 0, false));
                BodyHandle beyond = world.CreateBody(Cube(new Vector3(0f, 0f, 5f), 0, false));
                var origin = new Vector3(0f, 0f, 0.1f);

                Assert.IsTrue(world.Raycast(origin, Vector3.UnitZ, 100f, QueryFilter.All, out RayHit ray));
                Assert.IsTrue(world.ShapeCast(BodyShape.Sphere(0.2f), origin, Quaternion.Identity, Vector3.UnitZ, 100f, QueryFilter.All, out RayHit cast));
                Assert.IsTrue(world.Raycast(origin, Vector3.UnitZ, 100f, new QueryFilter(-1, inside, true), out RayHit skipped));

                Assert.AreEqual((inside.Value, 0f), (ray.Body.Value, ray.Distance));
                Assert.AreEqual((inside.Value, 0f), (cast.Body.Value, cast.Distance));
                Assert.AreEqual(beyond.Value, skipped.Body.Value);
                Assert.AreEqual(4.4f, skipped.Distance, 1e-4f);
            }
        }

        [Test]
        public void QueriesSeeBodiesAndStaticsChangedSinceTheLastStep()
        {
            using (var world = new RapierWorld(Vector3.Zero))
            {
                BodyHandle body = world.CreateBody(Cube(new Vector3(0f, 0f, 5f), 0, false));
                world.Step(Tick);
                PhysicsSnapshot snapshot = world.CreateSnapshot();
                world.Save(snapshot);

                world.SetBody(body, new BodyState { Position = new Vector3(0f, 0f, 10f), Rotation = Quaternion.Identity });
                AssertRay(world, QueryFilter.All, body, 9.5f);
                world.Load(snapshot);
                AssertRay(world, QueryFilter.All, body, 4.5f);
                StaticGroup wall = world.AddStatic(new[] { new ColliderDesc(BodyShape.Box(new Vector3(1f, 1f, 0.5f)), new Vector3(0f, 0f, 3f), Quaternion.Identity, ColliderMaterial.Default, 0, false) });
                AssertRay(world, QueryFilter.All, default, 2.5f);
                world.RemoveStatic(wall);
                world.RemoveBody(body);
                Assert.IsFalse(world.Raycast(Vector3.Zero, Vector3.UnitZ, 100f, QueryFilter.All, out _));
            }
        }

        [Test]
        public void QueriesGiveTheSameAnswersBeforeAndAfterTheStepThatIndexesTheBodies()
        {
            using (var world = new RapierWorld(Vector3.Zero))
            {
                BodyShape[] shapes = { BodyShape.Box(new Vector3(0.4f, 0.3f, 0.5f)), BodyShape.Sphere(0.45f), BodyShape.Capsule(0.3f, 0.4f) };
                var rotation = new Quaternion(0.1f, 0.2f, 0.3f, 0.92736185f);
                for (int index = 0; index < 64; index++)
                {
                    var position = new Vector3(index % 4 * 1.5f, index / 4 % 4 * 1.5f, index / 16 * 1.5f);
                    var collider = new ColliderDesc(shapes[index % 3], Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 0, false);
                    world.CreateBody(new BodyDesc(BodyKind.Kinematic, new[] { collider }, position, rotation, 1f));
                }

                List<string> before = QueryAnswers(world);
                world.Step(Tick);

                CollectionAssert.AreEqual(before, QueryAnswers(world));
            }
        }

        [Test]
        public void HullsFallOntoTriangleMeshes()
        {
            using (var world = new RapierWorld(Gravity))
            {
                Vector3[] floor = { new Vector3(-10f, 0f, -10f), new Vector3(10f, 0f, -10f), new Vector3(10f, 0f, 10f), new Vector3(-10f, 0f, 10f) };
                world.AddStatic(new[] { new ColliderDesc(BodyShape.TriangleMesh(floor, new[] { 0, 2, 1, 0, 3, 2 }), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 0, false) });
                Vector3[] cube = { new Vector3(-0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f), new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, 0.5f, -0.5f) };
                BodyHandle box = world.CreateBody(new BodyDesc(BodyKind.Dynamic, BodyShape.ConvexHull(cube), new Vector3(0f, 3f, 0f), Quaternion.Identity, 1f));

                Run(world, 300);

                Assert.AreEqual(0.5f, world.GetBody(box).Position.Y, 0.05f);
            }
        }

        [Test]
        public void ADisposedWorldHasNoBodiesAndRefusesNewOnes()
        {
            var world = new RapierWorld(Gravity);
            BodyHandle ball = world.CreateBody(Ball(Vector3.Zero, 1f));

            world.Dispose();

            Assert.IsTrue(world.IsDisposed);
            Assert.IsFalse(world.Contains(ball));
            Assert.IsFalse(new PhysicsBody(world, ball).IsValid);
            Assert.Throws<ArgumentException>(() => world.CreateBody(Ball(Vector3.Zero, 1f)));
        }

        [Test]
        public void TheSnapshotOfASceneSizedWorldIsMeasured()
        {
            using (var world = new RapierWorld(Gravity))
            {
                const int cells = 100;
                var vertices = new Vector3[(cells + 1) * (cells + 1)];
                for (int z = 0; z <= cells; z++)
                {
                    for (int x = 0; x <= cells; x++)
                    {
                        vertices[z * (cells + 1) + x] = new Vector3(x, (x + z) % 3 * 0.1f, z);
                    }
                }

                var triangles = new int[cells * cells * 6];
                for (int z = 0, next = 0; z < cells; z++)
                {
                    for (int x = 0; x < cells; x++)
                    {
                        int corner = z * (cells + 1) + x;
                        triangles[next++] = corner;
                        triangles[next++] = corner + cells + 1;
                        triangles[next++] = corner + cells + 2;
                        triangles[next++] = corner;
                        triangles[next++] = corner + cells + 2;
                        triangles[next++] = corner + 1;
                    }
                }

                world.AddStatic(new[] { new ColliderDesc(BodyShape.TriangleMesh(vertices, triangles), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 0, false) });
                for (int index = 0; index < 100; index++)
                {
                    world.CreateBody(Ball(new Vector3(index % 10 * 5f + 2f, 2f, index / 10 * 5f + 2f), 1f));
                }

                Run(world, 60);
                PhysicsSnapshot snapshot = world.CreateSnapshot();
                world.Save(snapshot);
                var clock = Stopwatch.StartNew();
                const int rounds = 50;
                for (int round = 0; round < rounds; round++)
                {
                    world.Save(snapshot);
                }

                double saveMilliseconds = clock.Elapsed.TotalMilliseconds / rounds;
                clock.Restart();
                for (int round = 0; round < rounds; round++)
                {
                    world.Load(snapshot);
                }

                double loadMilliseconds = clock.Elapsed.TotalMilliseconds / rounds;
                clock.Restart();
                for (int round = 0; round < rounds; round++)
                {
                    world.Step(Tick);
                }

                double stepMilliseconds = clock.Elapsed.TotalMilliseconds / rounds;
                TestContext.Out.WriteLine($"triangles {triangles.Length / 3}, bodies 100: save {saveMilliseconds:F3} ms, load {loadMilliseconds:F3} ms, step {stepMilliseconds:F3} ms");
                Assert.Greater(saveMilliseconds, 0d);
            }
        }

        [Test]
        public void ABallRestingOnTheGroundTouchesTheStaticColliderOfItsIndex()
        {
            using (var world = new RapierWorld(Gravity))
            {
                world.AddStatic(new[] { new ColliderDesc(BodyShape.Box(new Vector3(1f)), new Vector3(50f, 0f, 0f), Quaternion.Identity, ColliderMaterial.Default, 0, false), Ground(0) });
                BodyHandle ball = world.CreateBody(Ball(new Vector3(0f, 2f, 0f), 1f));

                Run(world, 120);

                CollectionAssert.AreEqual(new[] { RapierCollider.Static(1) }, Touching(world, RapierCollider.OfBody(ball, 0)));
                CollectionAssert.AreEqual(new[] { RapierCollider.OfBody(ball, 0) }, Touching(world, RapierCollider.Static(1)));
                CollectionAssert.IsEmpty(Touching(world, RapierCollider.Static(0)));
                CollectionAssert.IsEmpty(Touching(world, RapierCollider.OfBody(ball, 5)));
            }
        }

        [Test]
        public void ASensorFindsDynamicAndKinematicBodiesButNotLayersItIgnores()
        {
            using (var world = new RapierWorld(Vector3.Zero))
            {
                var masks = new uint[32];
                for (int layer = 0; layer < 32; layer++)
                {
                    masks[layer] = uint.MaxValue;
                }

                masks[1] &= ~(1u << 2);
                masks[2] &= ~(1u << 1);
                world.SetLayerCollisions(masks);
                world.AddStatic(new[] { new ColliderDesc(BodyShape.Box(new Vector3(2f)), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 1, true) });
                BodyHandle ball = world.CreateBody(Ball(new Vector3(-1f, 0f, 0f), 1f));
                BodyHandle carried = world.CreateBody(new BodyDesc(BodyKind.Kinematic, BodyShape.Sphere(0.5f), new Vector3(1f, 0f, 0f), Quaternion.Identity, 0f));
                world.CreateBody(Ball(new Vector3(0f, 1.2f, 0f), 1f, layer: 2));
                var pair = new[]
                {
                    new ColliderDesc(BodyShape.Sphere(0.25f), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 0, false),
                    new ColliderDesc(BodyShape.Sphere(0.25f), new Vector3(10f, 0f, 0f), Quaternion.Identity, ColliderMaterial.Default, 0, false),
                };
                BodyHandle reaching = world.CreateBody(new BodyDesc(BodyKind.Dynamic, pair, new Vector3(-10f, -1.5f, 0f), Quaternion.Identity, 1f));

                world.Step(Tick);

                List<RapierCollider> zone = Touching(world, RapierCollider.Static(0));
                CollectionAssert.AreEquivalent(new[] { RapierCollider.OfBody(ball, 0), RapierCollider.OfBody(carried, 0), RapierCollider.OfBody(reaching, 1) }, zone);
                CollectionAssert.IsEmpty(Touching(world, RapierCollider.OfBody(ball, 0)));
            }
        }

        [Test]
        public void LoadBringsBackTheTouchingSetOfTheSavedTick()
        {
            using (var world = new RapierWorld(Vector3.Zero))
            {
                world.AddStatic(new[] { new ColliderDesc(BodyShape.Box(new Vector3(1f)), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 0, true) });
                BodyHandle ball = world.CreateBody(Ball(new Vector3(-3f, 0f, 0f), 1f));
                world.SetBody(ball, new BodyState { Position = new Vector3(-3f, 0f, 0f), Rotation = Quaternion.Identity, Velocity = new Vector3(6f, 0f, 0f) });
                PhysicsSnapshot snapshot = world.CreateSnapshot();
                int ticks = 0;
                while (Touching(world, RapierCollider.Static(0)).Count == 0 && ticks < 120)
                {
                    world.Step(Tick);
                    ticks++;
                }

                world.Save(snapshot);
                while (Touching(world, RapierCollider.Static(0)).Count > 0 && ticks < 240)
                {
                    world.Step(Tick);
                    ticks++;
                }

                CollectionAssert.IsEmpty(Touching(world, RapierCollider.Static(0)));

                world.Load(snapshot);

                CollectionAssert.AreEqual(new[] { RapierCollider.OfBody(ball, 0) }, Touching(world, RapierCollider.Static(0)));
                Assert.Throws<ArgumentNullException>(() => world.Touching(RapierCollider.Static(0), null));
            }
        }

        [Test]
        public void AStaticGroupComesAndGoesAndLoadFollowsIt()
        {
            using (var world = new RapierWorld(Gravity))
            {
                PhysicsSnapshot before = world.CreateSnapshot();
                world.Save(before);
                StaticGroup ground = world.AddStatic(new[] { Ground(0) });
                world.Load(before);
                BodyHandle ball = world.CreateBody(Ball(new Vector3(0f, 2f, 0f), 1f));

                Run(world, 120);

                Assert.AreEqual((0, 1), (ground.FirstIndex, ground.Count));
                Assert.AreEqual(0.5f, world.GetBody(ball).Position.Y, 0.05f);
                PhysicsSnapshot resting = world.CreateSnapshot();
                world.Save(resting);
                world.RemoveStatic(ground);
                world.Load(resting);
                Run(world, 60);

                Assert.Less(world.GetBody(ball).Position.Y, -1f);
                Assert.AreEqual(1, world.AddStatic(new[] { Ground(0) }).FirstIndex);
            }
        }

        [Test]
        public void AFallingBodyIsMovedUntilItSleepsAndAWrittenOrLoadedBodyIsMovedOnce()
        {
            using (var world = new RapierWorld(Gravity))
            {
                world.AddStatic(new[] { Ground(0) });
                BodyHandle ball = world.CreateBody(Ball(new Vector3(0f, 2f, 0f), 1f));
                BodyHandle parked = world.CreateBody(Ball(new Vector3(5f, 0.5f, 0f), 1f));
                world.SetKind(parked, BodyKind.Kinematic);
                BodyHandle fixedBody = world.CreateBody(Ball(new Vector3(8f, 0.5f, 0f), 1f));
                world.SetKind(fixedBody, BodyKind.Static);
                BodyHandle removed = world.CreateBody(Ball(new Vector3(-5f, 2f, 0f), 1f));
                PhysicsSnapshot snapshot = world.CreateSnapshot();
                world.Save(snapshot);

                world.Step(Tick);
                List<BodyHandle> falling = Moved(world);
                world.SetBody(removed, new BodyState { Position = new Vector3(-5f, 3f, 0f), Rotation = Quaternion.Identity });
                world.RemoveBody(removed);
                List<BodyHandle> afterRemove = Moved(world);
                Run(world, 600);
                Moved(world);
                world.Step(Tick);
                List<BodyHandle> resting = Moved(world);
                world.SetBody(parked, new BodyState { Position = new Vector3(5f, 1f, 0f), Rotation = Quaternion.Identity });
                world.SetBody(fixedBody, new BodyState { Position = new Vector3(8f, 1f, 0f), Rotation = Quaternion.Identity });
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
        public void TheMotionOfABodyLocksAxesTurnsGravityOffAndDamps()
        {
            using (var world = new RapierWorld(Gravity))
            {
                BodyHandle floating = world.CreateBody(Cube(new Vector3(0f, 0f, 0f), new BodyMotion(BodyLocks.None, false, 0f, 0f)));
                BodyHandle locked = world.CreateBody(Cube(new Vector3(10f, 0f, 0f), new BodyMotion(BodyLocks.PositionX | BodyLocks.RotationX | BodyLocks.RotationZ, true, 0f, 0f)));
                BodyHandle damped = world.CreateBody(Cube(new Vector3(20f, 0f, 0f), new BodyMotion(BodyLocks.None, false, 1f, 0f)));
                world.SetBody(locked, new BodyState { Position = new Vector3(10f, 0f, 0f), Rotation = Quaternion.Identity, Velocity = new Vector3(5f, 0f, 0f), AngularVelocity = new Vector3(3f, 3f, 3f) });
                world.SetBody(damped, new BodyState { Position = new Vector3(20f, 0f, 0f), Rotation = Quaternion.Identity, Velocity = new Vector3(6f, 0f, 0f) });

                Run(world, 30);

                Assert.AreEqual(Vector3.Zero, world.GetBody(floating).Position);
                BodyState state = world.GetBody(locked);
                Assert.AreEqual(10f, state.Position.X);
                Assert.Less(state.Position.Y, -1f);
                Vector3 axis = Vector3.Transform(Vector3.UnitY, state.Rotation);
                Assert.AreEqual(1f, axis.Y, 1e-4f);
                Assert.Less(world.GetBody(damped).Velocity.X, 6f * 0.65f);
                Assert.Greater(world.GetBody(damped).Velocity.X, 6f * 0.55f);
            }
        }

        private static BodyDesc Cube(Vector3 position, BodyMotion motion) =>
            new BodyDesc(BodyKind.Dynamic, new[] { new ColliderDesc(BodyShape.Box(new Vector3(0.5f)), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 0, false) }, position, Quaternion.Identity, 1f, motion);

        private static List<RapierCollider> Touching(RapierWorld world, RapierCollider collider)
        {
            var touching = new List<RapierCollider>();
            world.Touching(collider, touching);
            return touching;
        }

        private static RapierWorld Pile()
        {
            var world = new RapierWorld(Gravity);
            world.AddStatic(new[] { Ground(0) });
            for (int index = 0; index < 20; index++)
            {
                world.CreateBody(new BodyDesc(BodyKind.Dynamic, BodyShape.Box(new Vector3(0.4f)), new Vector3(index % 4 * 0.5f, 1f + index * 0.9f, index % 3 * 0.3f), Quaternion.CreateFromYawPitchRoll(index * 0.3f, index * 0.2f, 0f), 1f));
            }

            return world;
        }

        private static List<BodyHandle> Moved(RapierWorld world)
        {
            var moved = new List<BodyHandle>();
            world.CollectMoved(moved);
            return moved;
        }

        private static void Run(RapierWorld world, int ticks)
        {
            for (int tick = 0; tick < ticks; tick++)
            {
                world.Step(Tick);
            }
        }

        private static ColliderDesc Ground(int layer) =>
            new ColliderDesc(BodyShape.Box(new Vector3(10f, 0.5f, 10f)), new Vector3(0f, -0.5f, 0f), Quaternion.Identity, ColliderMaterial.Default, layer, false);

        private static BodyDesc Cube(Vector3 position, int layer, bool trigger) =>
            new BodyDesc(BodyKind.Kinematic, new[] { new ColliderDesc(BodyShape.Box(new Vector3(0.5f, 0.5f, 0.5f)), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, layer, trigger) }, position, Quaternion.Identity, 1f);

        private static void AssertRay(RapierWorld world, in QueryFilter filter, BodyHandle body, float distance)
        {
            Assert.IsTrue(world.Raycast(Vector3.Zero, Vector3.UnitZ, 100f, filter, out RayHit hit));
            Assert.AreEqual(body.Value, hit.Body.Value);
            Assert.AreEqual(distance, hit.Distance, 1e-4f);
        }

        private static List<string> QueryAnswers(RapierWorld world)
        {
            var answers = new List<string>();
            var found = new BodyHandle[64];
            for (int index = 0; index < 16; index++)
            {
                var origin = new Vector3(index * 0.3f - 1f, 10f, index * 0.2f);
                var direction = new Vector3(0.1f, -1f, 0.05f);
                world.Raycast(origin, direction, 30f, QueryFilter.All, out RayHit ray);
                world.ShapeCast(BodyShape.Sphere(0.3f), origin, Quaternion.Identity, direction, 30f, QueryFilter.All, out RayHit cast);
                int count = world.Overlap(BodyShape.Box(new Vector3(0.7f, 0.7f, 0.7f)), new Vector3(index * 0.3f, index * 0.25f, 2f), Quaternion.Identity, QueryFilter.All, found);
                var bodies = new int[count];
                for (int slot = 0; slot < count; slot++)
                {
                    bodies[slot] = found[slot].Value;
                }

                answers.Add($"{ray.Body.Value} {ray.Distance:R} {ray.Point} {ray.Normal} {cast.Body.Value} {cast.Distance:R} {cast.Point} {cast.Normal} {string.Join(",", bodies)}");
            }

            return answers;
        }

        private static BodyDesc Ball(Vector3 position, float mass, int layer = 0) =>
            new BodyDesc(BodyKind.Dynamic, new[] { new ColliderDesc(BodyShape.Sphere(0.5f), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, layer, false) }, position, Quaternion.Identity, mass);
    }
}
