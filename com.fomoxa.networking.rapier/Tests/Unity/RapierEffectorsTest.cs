using System;
using System.Collections.Generic;
using Fomoxa.Networking.Messaging;
using Fomoxa.Networking.Rapier;
using Fomoxa.Networking.Simulation;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fomoxa.Unity.Rapier.Tests
{
    public sealed class RapierEffectorsTest
    {
        private const float Seconds = 1f / 60f;

        private readonly List<string> ignored = new List<string>();
        private Scene scene;
        private Vector2 savedGravity;
        private SimulationMode2D savedMode;
        private string label = "";

        [SetUp]
        public void SimulateByScript()
        {
            savedGravity = Physics2D.gravity;
            savedMode = Physics2D.simulationMode;
            Physics2D.simulationMode = SimulationMode2D.Script;
        }

        [TearDown]
        public void RestoreSimulation()
        {
            Physics2D.gravity = savedGravity;
            Physics2D.simulationMode = savedMode;
            if (scene.IsValid())
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void AreaForcesAnglesTargetsAndDampingMatchBox2D()
        {
            Physics2D.gravity = Vector2.zero;
            Compare("global angle", 60, 0.05f, 0.5f, () =>
            {
                AreaEffector2D area = Area(Vector2.zero, 0f);
                area.forceMagnitude = 10f;
                area.forceAngle = 30f;
                area.useGlobalAngle = true;
                Body(Vector2.zero, 2f);
            });
            Compare("local angle", 60, 0.05f, 0.5f, () =>
            {
                AreaEffector2D area = Area(Vector2.zero, 90f);
                area.forceMagnitude = 6f;
                area.forceAngle = 20f;
                area.useGlobalAngle = false;
                Body(Vector2.zero, 1f);
            });
            Compare("damping", 60, 0.05f, 0.5f, () =>
            {
                AreaEffector2D area = Area(Vector2.zero, 0f);
                area.linearDamping = 1f;
                area.angularDamping = 2f;
                Rigidbody2D body = Body(Vector2.zero, 1f);
                body.linearVelocity = new Vector2(5f, 1f);
                body.angularVelocity = 180f;
            });
            Compare("collider target", 60, 0.05f, 0.5f, () =>
            {
                AreaEffector2D area = Area(Vector2.zero, 0f);
                area.forceMagnitude = 10f;
                area.forceAngle = 90f;
                area.useGlobalAngle = true;
                area.forceTarget = EffectorSelection2D.Collider;
                Body(Vector2.zero, 1f).GetComponent<BoxCollider2D>().offset = new Vector2(1f, 0f);
            });
        }

        [Test]
        public void PointForcesFallOffAndPullTowardsTheSourceAsInBox2D()
        {
            Physics2D.gravity = Vector2.zero;
            foreach (EffectorForceMode2D mode in new[] { EffectorForceMode2D.Constant, EffectorForceMode2D.InverseLinear, EffectorForceMode2D.InverseSquared })
            {
                Compare(mode.ToString(), 30, 0.05f, 0.5f, () =>
                {
                    PointEffector2D point = Point(Vector2.zero);
                    point.forceMagnitude = -10f;
                    point.forceMode = mode;
                    point.distanceScale = 0.75f;
                    Body(new Vector2(3f, 1f), 1f);
                    PointEffector2D away = Point(new Vector2(40f, 0f));
                    away.forceMagnitude = 4f;
                    away.forceMode = mode;
                    Body(new Vector2(39f, -1f), 1f);
                });
            }

            Compare("collider source", 30, 0.05f, 0.5f, () =>
            {
                PointEffector2D point = Point(Vector2.zero);
                point.forceMagnitude = 8f;
                point.forceSource = EffectorSelection2D.Collider;
                point.forceTarget = EffectorSelection2D.Collider;
                point.GetComponent<CircleCollider2D>().offset = new Vector2(1f, 0f);
                point.linearDamping = 0.5f;
                Body(new Vector2(4f, 0f), 1f).GetComponent<BoxCollider2D>().offset = new Vector2(0f, 1f);
            });
        }

        [Test]
        public void BuoyancyFloatsBoxesCirclesAndCapsulesAsBox2DDoes()
        {
            Physics2D.gravity = new Vector2(0f, -9.81f);
            Compare("box", 30, 0.03f, 1f, () =>
            {
                Fluid(new Vector2(0f, -5f), 2f);
                Body(new Vector2(-6f, 0.2f), 0.5f);
                Body(new Vector2(6f, 0.2f), 0.5f);
            });
            Compare("tilted box", 30, 0.03f, 3f, () =>
            {
                Fluid(new Vector2(0f, -5f), 2f);
                Rigidbody2D tilted = Body(new Vector2(-2f, 0f), 0.4f);
                tilted.transform.rotation = Quaternion.Euler(0f, 0f, 30f);
                tilted.GetComponent<BoxCollider2D>().size = new Vector2(2f, 0.5f);
            });
            Compare("circle", 30, 0.03f, 1f, () =>
            {
                Fluid(new Vector2(0f, -5f), 2f);
                Rigidbody2D circle = Body(new Vector2(2f, 0.3f), 0.3f);
                circle.GetComponent<BoxCollider2D>().enabled = false;
                circle.gameObject.AddComponent<CircleCollider2D>().radius = 0.5f;
            });
            Compare("capsule", 30, 0.03f, 1f, () =>
            {
                Fluid(new Vector2(0f, -5f), 0f);
                Rigidbody2D capsule = Body(new Vector2(6f, 0.5f), 0.6f);
                capsule.GetComponent<BoxCollider2D>().enabled = false;
                capsule.gameObject.AddComponent<CapsuleCollider2D>().size = new Vector2(1f, 2f);
            });
        }

        [Test]
        public void ASurfaceDrivesWhatRestsOnItAsInBox2D()
        {
            Physics2D.gravity = new Vector2(0f, -9.81f);
            Fresh();
            SurfaceEffector2D belt = Ledge(new Vector2(0f, -0.5f), new Vector2(40f, 1f)).gameObject.AddComponent<SurfaceEffector2D>();
            belt.speed = 5f;
            belt.forceScale = 0.25f;
            Rigidbody2D crate = Body(new Vector2(-5f, 0.5f), 1f);
            SurfaceEffector2D tilted = Ledge(new Vector2(0f, -10.5f), new Vector2(40f, 1f)).gameObject.AddComponent<SurfaceEffector2D>();
            tilted.transform.rotation = Quaternion.Euler(0f, 0f, 10f);
            tilted.speed = -3f;
            Rigidbody2D sliding = Body(new Vector2(0f, -9.9f), 2f);
            sliding.transform.rotation = Quaternion.Euler(0f, 0f, 10f);

            using (RapierWorld2D world = Mirror(out Dictionary<Rigidbody2D, BodyHandle> bodies))
            {
                Run(world, 90);

                Assert.AreEqual(crate.linearVelocity.x, world.GetBody(bodies[crate]).Velocity.X, 0.05f);
                Assert.AreEqual(sliding.linearVelocity.x, world.GetBody(bodies[sliding]).Velocity.X, 0.05f);
                Assert.Greater(crate.linearVelocity.x, 4f);
                Assert.Less(sliding.linearVelocity.x, -2f);
            }
        }

        [Test]
        public void OneWayPlatformsLetBodiesThroughFromBelowAndHoldThemFromAboveAsInBox2D()
        {
            Physics2D.gravity = new Vector2(0f, -9.81f);
            Compare("one way", 150, 0.1f, 1f, () =>
            {
                Ledge(Vector2.zero, new Vector2(4f, 0.5f)).gameObject.AddComponent<PlatformEffector2D>();
                Body(new Vector2(-1.2f, -2f), 1f).linearVelocity = new Vector2(0f, 10f);
                Body(new Vector2(1.2f, 2f), 1f);
            });
            Compare("rotated", 60, 0.1f, 1f, () =>
            {
                Ledge(Vector2.zero, new Vector2(4f, 0.5f)).gameObject.AddComponent<PlatformEffector2D>().rotationalOffset = 180f;
                Body(new Vector2(0f, 2f), 1f);
            });
        }

        [Test]
        public void SidesWithoutFrictionLetAPressedBodySlideAsInBox2D()
        {
            Physics2D.gravity = new Vector2(0f, -9.81f);
            Fresh();
            BoxCollider2D tower = Ledge(Vector2.zero, new Vector2(1f, 20f));
            tower.sharedMaterial = new PhysicsMaterial2D { friction = 1f, bounciness = 0f };
            PlatformEffector2D sides = tower.gameObject.AddComponent<PlatformEffector2D>();
            sides.useOneWay = false;
            sides.useSideFriction = false;
            sides.sideArc = 10f;
            Rigidbody2D pressed = Body(new Vector2(-1f, 5f), 1f);

            using (RapierWorld2D world = Mirror(out Dictionary<Rigidbody2D, BodyHandle> bodies))
            {
                for (int step = 0; step < 60; step++)
                {
                    pressed.AddForce(new Vector2(20f, 0f));
                    world.AddForce(bodies[pressed], new System.Numerics.Vector2(20f, 0f));
                    scene.GetPhysicsScene2D().Simulate(Seconds);
                    world.Step(Seconds);
                }

                AssertSame(pressed, world, bodies[pressed], 0.1f, 1f);
                Assert.Less(pressed.position.y, 1f);
            }
        }

        [Test]
        public void VariationsAreReportedAndCountAsZero()
        {
            Fresh();
            AreaEffector2D area = Area(Vector2.zero, 0f);
            area.forceVariation = 2f;

            SceneFileEffector2D file = EffectorDescriptions.Describe(area, null, ignored);

            CollectionAssert.AreEqual(new[] { "forceVariation" }, ignored);
            Assert.AreEqual(EffectorKind2D.Area, RapierEffectors.FromFile(file).Kind);
        }

        private void Fresh()
        {
            if (scene.IsValid())
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }

            scene = EditorSceneManager.NewPreviewScene();
        }

        private void Compare(string name, int steps, float distance, float degrees, Action build)
        {
            Fresh();
            build();
            label = name;
            using (RapierWorld2D world = Mirror(out Dictionary<Rigidbody2D, BodyHandle> bodies))
            {
                var start = new Dictionary<Rigidbody2D, Vector2>();
                foreach (Rigidbody2D body in bodies.Keys)
                {
                    start.Add(body, body.position);
                }

                Run(world, steps);

                foreach (KeyValuePair<Rigidbody2D, BodyHandle> entry in bodies)
                {
                    float travelled = Vector2.Distance(start[entry.Key], entry.Key.position);
                    Assert.Greater(travelled + Mathf.Abs(entry.Key.rotation), 0.1f, $"{label}: {entry.Key.name} did not move under Box2D");
                    AssertSame(entry.Key, world, entry.Value, distance + (0.015f * travelled), degrees);
                }
            }
        }

        private RapierWorld2D Mirror(out Dictionary<Rigidbody2D, BodyHandle> bodies)
        {
            var world = new RapierWorld2D(new System.Numerics.Vector2(Physics2D.gravity.x, Physics2D.gravity.y));
            bodies = new Dictionary<Rigidbody2D, BodyHandle>();
            var statics = new List<Collider2D>();
            var colliders = new List<ColliderDesc2D>();
            var pieces = new List<Collider2D>();
            BodyDescriptions.StaticColliders2D(scene, statics);
            for (int index = 0; index < statics.Count; index++)
            {
                if (index == 0 || statics[index] != statics[index - 1])
                {
                    BodyDescriptions.DescribeStatic2D(statics[index], null, colliders);
                    while (pieces.Count < colliders.Count)
                    {
                        pieces.Add(statics[index]);
                    }
                }
            }

            if (colliders.Count > 0)
            {
                StaticGroup group = world.AddStatic(colliders);
                foreach ((Effector2D effector, List<int> indices) in Group(pieces))
                {
                    world.AddEffector(group, indices, Describe(effector, null));
                }
            }

            var found = new List<Rigidbody2D>();
            var sources = new List<Collider2D>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                root.GetComponentsInChildren(found);
                foreach (Rigidbody2D body in found)
                {
                    Assert.IsTrue(BodyDescriptions.TryDescribe2D(body.gameObject, out BodyDesc2D desc, sources));
                    BodyHandle handle = world.CreateBody(desc);
                    world.SetBody(handle, new BodyState2D
                    {
                        Position = desc.Position,
                        Rotation = desc.Rotation,
                        Velocity = new System.Numerics.Vector2(body.linearVelocity.x, body.linearVelocity.y),
                        AngularVelocity = body.angularVelocity * Mathf.Deg2Rad,
                    });
                    bodies.Add(body, handle);
                    foreach ((Effector2D effector, List<int> indices) in Group(sources))
                    {
                        world.AddEffector(handle, indices, Describe(effector, body.transform));
                    }
                }
            }

            return world;
        }

        private EffectorDesc2D Describe(Effector2D effector, Transform owner)
        {
            ignored.Clear();
            EffectorDesc2D desc = RapierEffectors.FromFile(EffectorDescriptions.Describe(effector, owner, ignored));
            CollectionAssert.IsEmpty(ignored);
            return desc;
        }

        private static List<(Effector2D Effector, List<int> Indices)> Group(List<Collider2D> sources)
        {
            var grouped = new List<(Effector2D Effector, List<int> Indices)>();
            for (int index = 0; index < sources.Count; index++)
            {
                if (!EffectorDescriptions.TryFind(sources[index], out Effector2D effector))
                {
                    continue;
                }

                int entry = grouped.FindIndex(candidate => candidate.Effector == effector);
                if (entry < 0)
                {
                    grouped.Add((effector, new List<int>()));
                    entry = grouped.Count - 1;
                }

                grouped[entry].Indices.Add(index);
            }

            return grouped;
        }

        private void Run(RapierWorld2D world, int steps)
        {
            for (int step = 0; step < steps; step++)
            {
                scene.GetPhysicsScene2D().Simulate(Seconds);
                world.Step(Seconds);
            }
        }

        private void AssertSame(Rigidbody2D box2D, RapierWorld2D world, BodyHandle body, float distance, float degrees)
        {
            BodyState2D rapier = world.GetBody(body);
            Assert.Less(Vector2.Distance(box2D.position, rapier.Position.ToUnity()), distance, $"{label}: {box2D.name} at {box2D.position} under Box2D, {rapier.Position.ToUnity()} under Rapier");
            Assert.AreEqual(0f, Mathf.DeltaAngle(box2D.rotation, rapier.Rotation * Mathf.Rad2Deg), degrees, $"{label}: {box2D.name}");
            Assert.Less(Vector2.Distance(box2D.linearVelocity, rapier.Velocity.ToUnity()), 0.05f + (0.01f * box2D.linearVelocity.magnitude), $"{label}: {box2D.name} moves at {box2D.linearVelocity} under Box2D, {rapier.Velocity.ToUnity()} under Rapier");
            Assert.AreEqual(box2D.angularVelocity, rapier.AngularVelocity * Mathf.Rad2Deg, 1f + (0.05f * Mathf.Abs(box2D.angularVelocity)), $"{label}: {box2D.name}");
        }

        private GameObject Make(string name, Vector2 at)
        {
            var created = new GameObject(name);
            SceneManager.MoveGameObjectToScene(created, scene);
            created.transform.position = at;
            return created;
        }

        private Rigidbody2D Body(Vector2 at, float mass)
        {
            GameObject body = Make("body" + at, at);
            body.AddComponent<BoxCollider2D>().size = Vector2.one;
            var rigidbody = body.AddComponent<Rigidbody2D>();
            rigidbody.useAutoMass = false;
            rigidbody.mass = mass;
            rigidbody.sleepMode = RigidbodySleepMode2D.NeverSleep;
            return rigidbody;
        }

        private AreaEffector2D Area(Vector2 at, float rotation)
        {
            GameObject area = Make("area", at);
            area.transform.rotation = Quaternion.Euler(0f, 0f, rotation);
            var box = area.AddComponent<BoxCollider2D>();
            box.size = new Vector2(40f, 40f);
            box.isTrigger = true;
            box.usedByEffector = true;
            return area.AddComponent<AreaEffector2D>();
        }

        private PointEffector2D Point(Vector2 at)
        {
            GameObject point = Make("point", at);
            var circle = point.AddComponent<CircleCollider2D>();
            circle.radius = 10f;
            circle.isTrigger = true;
            circle.usedByEffector = true;
            return point.AddComponent<PointEffector2D>();
        }

        private void Fluid(Vector2 at, float flowMagnitude)
        {
            GameObject fluid = Make("fluid", at);
            var box = fluid.AddComponent<BoxCollider2D>();
            box.size = new Vector2(20f, 10f);
            box.isTrigger = true;
            box.usedByEffector = true;
            var effector = fluid.AddComponent<BuoyancyEffector2D>();
            effector.surfaceLevel = 5f;
            effector.density = 1f;
            effector.linearDamping = 1f;
            effector.angularDamping = 1f;
            effector.flowAngle = 15f;
            effector.flowMagnitude = flowMagnitude;
        }

        private BoxCollider2D Ledge(Vector2 at, Vector2 size)
        {
            GameObject ledge = Make("ledge" + at, at);
            var box = ledge.AddComponent<BoxCollider2D>();
            box.size = size;
            box.usedByEffector = true;
            return box;
        }
    }
}
