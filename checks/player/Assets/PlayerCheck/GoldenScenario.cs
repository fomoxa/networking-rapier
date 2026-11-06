using System;
using System.Collections.Generic;
using System.Numerics;
using Fomoxa.Networking.Rapier;
using Fomoxa.Networking.Simulation;

namespace Fomoxa.Rapier.PlayerCheck
{
    public static class GoldenScenario
    {
        private const int Steps = 900;
        private const int ReportEvery = 150;
        private const int SnapshotAt = 450;
        private const int BodyCount = 60;
        private const float Seconds = 1f / 30f;

        private static readonly ColliderMaterial Material = new ColliderMaterial(0.6f, 0.1f, CombineRule.Average, CombineRule.Average);

        public static List<string> Run()
        {
            var lines = new List<string>();
            Run3D(lines);
            Run2D(lines);
            return lines;
        }

        private static void Run3D(List<string> lines)
        {
            using (var world = new RapierWorld(new Vector3(0f, -9.81f, 0f)))
            {
                Vector3[] groundPoints = { new Vector3(-50f, 0f, -50f), new Vector3(50f, 2f, -50f), new Vector3(50f, 0f, 50f), new Vector3(-50f, 3f, 50f) };
                int[] groundTriangles = { 0, 2, 1, 0, 3, 2 };
                world.AddStatic(new[] { Collider(BodyShape.TriangleMesh(groundPoints, groundTriangles)) });

                Vector3[] hullPoints = { new Vector3(0.4f, 0f, 0f), new Vector3(-0.4f, 0f, 0f), new Vector3(0f, 0.5f, 0f), new Vector3(0f, -0.5f, 0.1f), new Vector3(0f, 0f, 0.6f), new Vector3(0.1f, 0.1f, -0.6f) };
                var rotation = new Quaternion(0.1f, 0.2f, 0.3f, 0.92736185f);
                var motion = new BodyMotion(BodyLocks.None, true, 0.02f, 0.03f);
                var bodies = new BodyHandle[BodyCount];
                for (int index = 0; index < BodyCount; index++)
                {
                    var position = new Vector3(index % 7 * 1.125f - 3f, 6f + index * 0.875f, index % 5 * 0.875f - 2f);
                    float mass = 1f + index * 0.0625f;
                    bodies[index] = world.CreateBody(new BodyDesc(BodyKind.Dynamic, new[] { Collider(Shape3D(index, hullPoints)) }, position, rotation, mass, motion));
                }

                PhysicsSnapshot saved = world.CreateSnapshot();
                ulong atEnd = 0;
                for (int tick = 1; tick <= Steps; tick++)
                {
                    Step3D(world, bodies, tick);
                    if (tick % ReportEvery == 0)
                    {
                        lines.Add($"3d {tick} {world.StateHash:x16}");
                    }

                    if (tick == SnapshotAt)
                    {
                        world.Save(saved);
                    }

                    if (tick == Steps)
                    {
                        atEnd = world.StateHash;
                    }
                }

                world.Load(saved);
                for (int tick = SnapshotAt + 1; tick <= Steps; tick++)
                {
                    Step3D(world, bodies, tick);
                }

                lines.Add($"3d replay {Flag(world.StateHash == atEnd)}");

                bool found = world.Raycast(new Vector3(0.5f, 40f, 0.25f), new Vector3(0.02f, -1f, 0.01f), 100f, out RayHit hit);
                lines.Add($"3d ray {Flag(found)} {hit.Body.Value} {BitConverter.SingleToInt32Bits(hit.Distance):x8}");
            }
        }

        private static void Run2D(List<string> lines)
        {
            using (var world = new RapierWorld2D(new Vector2(0f, -9.81f)))
            {
                Vector2[] groundPoints = { new Vector2(-50f, 3f), new Vector2(-10f, 0f), new Vector2(10f, 0.5f), new Vector2(50f, 2f) };
                world.AddStatic(new[] { Collider(BodyShape2D.Polyline(groundPoints)) });

                Vector2[] polygonPoints = { new Vector2(-0.4f, -0.3f), new Vector2(0.4f, -0.3f), new Vector2(0.5f, 0.2f), new Vector2(0f, 0.5f), new Vector2(-0.5f, 0.2f) };
                var motion = new BodyMotion2D(BodyLocks2D.None, 1f, 0.02f, 0.03f);
                var bodies = new BodyHandle[BodyCount];
                for (int index = 0; index < BodyCount; index++)
                {
                    var position = new Vector2(index % 9 * 1.125f - 4f, 6f + index * 0.875f);
                    float mass = 1f + index * 0.0625f;
                    bodies[index] = world.CreateBody(new BodyDesc2D(BodyKind.Dynamic, new[] { Collider(Shape2D(index, polygonPoints)) }, position, index * 0.125f, mass, motion));
                }

                PhysicsSnapshot saved = world.CreateSnapshot();
                ulong atEnd = 0;
                for (int tick = 1; tick <= Steps; tick++)
                {
                    Step2D(world, bodies, tick);
                    if (tick % ReportEvery == 0)
                    {
                        lines.Add($"2d {tick} {world.StateHash:x16}");
                    }

                    if (tick == SnapshotAt)
                    {
                        world.Save(saved);
                    }

                    if (tick == Steps)
                    {
                        atEnd = world.StateHash;
                    }
                }

                world.Load(saved);
                for (int tick = SnapshotAt + 1; tick <= Steps; tick++)
                {
                    Step2D(world, bodies, tick);
                }

                lines.Add($"2d replay {Flag(world.StateHash == atEnd)}");
            }
        }

        private static void Step3D(RapierWorld world, BodyHandle[] bodies, int tick)
        {
            if (tick % 37 == 0)
            {
                world.AddImpulse(bodies[tick % bodies.Length], new Vector3(1.5f, 3.25f, -0.75f));
            }

            world.AddForce(bodies[tick * 3 % bodies.Length], new Vector3(4f, 0f, 2.5f));
            world.Step(Seconds);
        }

        private static void Step2D(RapierWorld2D world, BodyHandle[] bodies, int tick)
        {
            if (tick % 37 == 0)
            {
                world.AddImpulse(bodies[tick % bodies.Length], new Vector2(1.5f, 3.25f));
            }

            world.AddForce(bodies[tick * 3 % bodies.Length], new Vector2(4f, 0f));
            world.Step(Seconds);
        }

        private static BodyShape Shape3D(int index, Vector3[] hullPoints)
        {
            switch (index % 4)
            {
                case 0: return BodyShape.Box(new Vector3(0.4f, 0.3f, 0.5f));
                case 1: return BodyShape.Sphere(0.45f);
                case 2: return BodyShape.Capsule(0.3f, 0.4f);
                default: return BodyShape.ConvexHull(hullPoints);
            }
        }

        private static BodyShape2D Shape2D(int index, Vector2[] polygonPoints)
        {
            switch (index % 4)
            {
                case 0: return BodyShape2D.Box(new Vector2(0.4f, 0.3f));
                case 1: return BodyShape2D.Circle(0.45f);
                case 2: return BodyShape2D.Capsule(0.3f, 0.4f);
                default: return BodyShape2D.ConvexPolygon(polygonPoints);
            }
        }

        private static ColliderDesc Collider(BodyShape shape) => new ColliderDesc(shape, Vector3.Zero, Quaternion.Identity, Material, 0, false);

        private static ColliderDesc2D Collider(BodyShape2D shape) => new ColliderDesc2D(shape, Vector2.Zero, 0f, Material, 0, false);

        private static string Flag(bool value) => value ? "true" : "false";
    }
}
