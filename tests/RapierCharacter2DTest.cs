using System;
using System.Numerics;
using Fomoxa.Networking.Simulation;
using NUnit.Framework;

namespace Fomoxa.Networking.Rapier.Tests
{
    public sealed class RapierCharacter2DTest
    {
        private const float Tick = 1f / 60f;
        private static readonly Vector2 Gravity = new Vector2(0f, -9.81f);
        private static readonly Vector2 Start = new Vector2(0f, 0.81f);

        [Test]
        public void TheMoveArrivesAtTheNextStepAndAWallStopsIt()
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                world.AddStatic(new[] { Ground(), Box(new Vector2(0.5f, 2f), new Vector2(2f, 2f), 0, false) });
                BodyHandle character = world.CreateBody(Character(Start));
                world.Step(Tick);

                CharacterMove2D move = world.MoveCharacter(character, new Vector2(0.5f, -0.02f), Tick, CharacterSettings2D.Default);
                Assert.AreEqual(0.5f, move.Moved.X, 1e-3f);
                Assert.IsTrue(move.Grounded);
                Assert.IsFalse(move.Sliding);
                Assert.AreEqual(0f, world.GetBody(character).Position.X, 1e-6f);
                world.Step(Tick);
                Assert.AreEqual(0.5f, world.GetBody(character).Position.X, 1e-3f);

                Assert.AreEqual(0.7f, world.MoveCharacter(character, new Vector2(3f, -0.02f), Tick, CharacterSettings2D.Default).Moved.X, 0.05f);
            }
        }

        [Test]
        public void TriggersNeverBlockTheLayerMatrixAppliesAndABodyPlacedInTheTickBlocks()
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                uint[] masks = new uint[32];
                for (int layer = 0; layer < masks.Length; layer++)
                {
                    masks[layer] = uint.MaxValue;
                }

                masks[1] = ~(1u << 2);
                masks[2] = ~(1u << 1);
                world.SetLayerCollisions(masks);
                world.AddStatic(new[] { Ground(), Box(new Vector2(0.3f, 1f), new Vector2(1f, 1f), 0, true), Box(new Vector2(0.1f, 1f), new Vector2(2f, 1f), 2, false) });
                BodyHandle character = world.CreateBody(Character(Start, 1));
                BodyHandle gate = world.CreateBody(new BodyDesc2D(BodyKind.Kinematic, new[] { Box(new Vector2(0.1f, 1f), Vector2.Zero, 0, false) }, new Vector2(0f, 20f), 0f, 1f));
                world.Step(Tick);

                Assert.AreEqual(4f, world.MoveCharacter(character, new Vector2(4f, -0.02f), Tick, CharacterSettings2D.Default).Moved.X, 1e-3f);
                world.SetBody(gate, new BodyState2D { Position = new Vector2(3f, 1f) });
                Assert.AreEqual(2.6f, world.MoveCharacter(character, new Vector2(4f, -0.02f), Tick, CharacterSettings2D.Default).Moved.X, 0.05f);
            }
        }

        [Test]
        public void SlidingAutostepAndPushMassFollowTheSettings()
        {
            Assert.IsFalse(LandOn(30f));
            Assert.IsTrue(LandOn(60f));

            using (var world = new RapierWorld2D(Gravity))
            {
                world.AddStatic(new[] { Ground() });
                BodyHandle pushed = world.CreateBody(new BodyDesc2D(BodyKind.Dynamic, new[] { Box(new Vector2(0.3f), Vector2.Zero, 0, false) }, new Vector2(1f, 0.3f), 0f, 1f));
                BodyHandle pusher = world.CreateBody(Character(Start));
                for (int tick = 0; tick < 30; tick++)
                {
                    world.Step(Tick);
                }

                world.MoveCharacter(pusher, new Vector2(1f, -0.02f), Tick, CharacterSettings2D.Default.WithPushMass(5f));
                Assert.Greater(world.GetBody(pushed).Velocity.X, 1f);
            }

            using (var world = new RapierWorld2D(Gravity))
            {
                world.AddStatic(new[] { Ground(), Box(new Vector2(2f, 0.15f), new Vector2(3f, 0.15f), 0, false) });
                BodyHandle climber = world.CreateBody(Character(Start));
                world.Step(Tick);
                CharacterSettings2D stepping = CharacterSettings2D.Default.WithAutostep(CharacterAutostep.Default);
                for (int tick = 0; tick < 40; tick++)
                {
                    world.MoveCharacter(climber, new Vector2(0.1f, -0.02f), Tick, stepping);
                    world.Step(Tick);
                }

                Assert.Greater(world.GetBody(climber).Position.X, 2.5f);
                Assert.AreEqual(1.11f, world.GetBody(climber).Position.Y, 0.02f);
            }

            using (var world = new RapierWorld2D(Gravity))
            {
                BodyHandle dynamic = world.CreateBody(new BodyDesc2D(BodyKind.Dynamic, new[] { Capsule(0, false) }, Start, 0f, 1f));
                BodyHandle circle = world.CreateBody(new BodyDesc2D(BodyKind.Kinematic, new[] { new ColliderDesc2D(BodyShape2D.Circle(0.5f), Vector2.Zero, 0f, ColliderMaterial.Default, 0, false), Capsule(0, true) }, new Vector2(5f, 5f), 0f, 1f));
                Assert.Throws<ArgumentException>(() => world.MoveCharacter(dynamic, Vector2.UnitX, Tick, CharacterSettings2D.Default));
                Assert.Throws<ArgumentException>(() => world.MoveCharacter(circle, Vector2.UnitX, -1f, CharacterSettings2D.Default));
                Assert.AreEqual(1f, world.MoveCharacter(circle, Vector2.UnitX, Tick, CharacterSettings2D.Default).Moved.X, 1e-3f);
                Assert.Throws<ArgumentException>(() => CharacterSettings2D.Default.WithUp(Vector2.Zero));
                Assert.AreEqual(Vector2.UnitY, default(CharacterSettings2D).Up);
            }
        }

        private static bool LandOn(float degrees)
        {
            using (var world = new RapierWorld2D(Gravity))
            {
                world.AddStatic(new[] { new ColliderDesc2D(BodyShape2D.Box(new Vector2(5f, 0.5f)), Vector2.Zero, degrees * MathF.PI / 180f, ColliderMaterial.Default, 0, false) });
                BodyHandle character = world.CreateBody(Character(new Vector2(0f, 4f)));
                world.Step(Tick);

                return world.MoveCharacter(character, new Vector2(0f, -3f), Tick, CharacterSettings2D.Default).Sliding;
            }
        }

        private static ColliderDesc2D Ground() => Box(new Vector2(20f, 0.5f), new Vector2(0f, -0.5f), 0, false);

        private static ColliderDesc2D Box(Vector2 halfExtents, Vector2 position, int layer, bool isTrigger) =>
            new ColliderDesc2D(BodyShape2D.Box(halfExtents), position, 0f, ColliderMaterial.Default, layer, isTrigger);

        private static ColliderDesc2D Capsule(int layer, bool isTrigger) =>
            new ColliderDesc2D(BodyShape2D.Capsule(0.3f, 0.5f), Vector2.Zero, 0f, ColliderMaterial.Default, layer, isTrigger);

        private static BodyDesc2D Character(Vector2 position, int layer = 0) =>
            new BodyDesc2D(BodyKind.Kinematic, new[] { Capsule(layer, false) }, position, 0f, 1f);
    }
}
