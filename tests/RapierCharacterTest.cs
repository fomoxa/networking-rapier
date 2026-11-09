using System;
using System.Numerics;
using Fomoxa.Networking.Simulation;
using NUnit.Framework;

namespace Fomoxa.Networking.Rapier.Tests
{
    public sealed class RapierCharacterTest
    {
        private const float Tick = 1f / 60f;
        private static readonly Vector3 Gravity = new Vector3(0f, -9.81f, 0f);
        private static readonly Vector3 Start = new Vector3(0f, 0.81f, 0f);

        [Test]
        public void TheMoveArrivesAtTheNextStepAndASecondCallReplacesTheFirst()
        {
            using (var world = new RapierWorld(Gravity))
            {
                world.AddStatic(new[] { Ground() });
                BodyHandle character = world.CreateBody(Character(Start));
                world.Step(Tick);

                CharacterMove move = world.MoveCharacter(character, new Vector3(5f, 0f, 0f), Tick, CharacterSettings.Default);
                Assert.AreEqual(5f, move.Moved.X, 1e-3f);
                move = world.MoveCharacter(character, new Vector3(0.5f, -0.02f, 0f), Tick, CharacterSettings.Default);
                Assert.AreEqual(0.5f, move.Moved.X, 1e-3f);
                Assert.IsTrue(move.Grounded);
                Assert.IsFalse(move.Sliding);
                Assert.AreEqual(0f, world.GetBody(character).Position.X, 1e-6f);

                world.Step(Tick);

                BodyState state = world.GetBody(character);
                Assert.AreEqual(0.5f, state.Position.X, 1e-3f);
                Assert.AreEqual(0.5f / Tick, state.Velocity.X, 0.05f);
                Assert.AreEqual(Quaternion.Identity, state.Rotation);
            }
        }

        [Test]
        public void AWallStopsTheCharacterAndTheMoveSlidesAlongIt()
        {
            using (var world = new RapierWorld(Gravity))
            {
                world.AddStatic(new[] { Ground(), Box(new Vector3(0.5f, 2f, 5f), new Vector3(2f, 2f, 0f), 0, false) });
                BodyHandle character = world.CreateBody(Character(Start));
                world.Step(Tick);

                CharacterMove move = world.MoveCharacter(character, new Vector3(3f, -0.02f, 1f), Tick, CharacterSettings.Default);

                Assert.AreEqual(1.2f, move.Moved.X, 0.05f);
                Assert.AreEqual(1f, move.Moved.Z, 0.05f);
                Assert.IsTrue(move.Grounded);
            }
        }

        [Test]
        public void TriggersNeverBlockAndTheLayerMatrixAndTheLayerMaskBothApply()
        {
            using (var world = new RapierWorld(Gravity))
            {
                world.AddStatic(new[] { Ground(), Box(new Vector3(0.3f, 1f, 1f), new Vector3(2f, 1f, 0f), 0, true) });
                BodyHandle character = world.CreateBody(Character(Start));
                world.Step(Tick);

                Assert.AreEqual(4f, world.MoveCharacter(character, new Vector3(4f, -0.02f, 0f), Tick, CharacterSettings.Default).Moved.X, 1e-3f);
                Assert.AreEqual(4f, world.MoveCharacter(character, new Vector3(4f, -0.02f, 0f), Tick, CharacterSettings.Default, new QueryFilter(~0, default, true)).Moved.X, 1e-3f);
            }

            using (var world = new RapierWorld(Gravity))
            {
                uint[] masks = new uint[32];
                for (int layer = 0; layer < masks.Length; layer++)
                {
                    masks[layer] = uint.MaxValue;
                }

                masks[1] = ~(1u << 2);
                masks[2] = ~(1u << 1);
                world.SetLayerCollisions(masks);
                world.AddStatic(new[] { Ground(), Box(new Vector3(0.1f, 1f, 10f), new Vector3(2f, 1f, 0f), 2, false), Box(new Vector3(0.1f, 1f, 10f), new Vector3(3f, 1f, 0f), 0, false) });
                BodyHandle onLayer1 = world.CreateBody(Character(Start, 1));
                BodyHandle onLayer0 = world.CreateBody(Character(new Vector3(0f, 0.81f, 5f), 0));
                world.Step(Tick);

                Assert.AreEqual(2.6f, world.MoveCharacter(onLayer1, new Vector3(4f, -0.02f, 0f), Tick, CharacterSettings.Default).Moved.X, 0.05f);
                Assert.AreEqual(4f, world.MoveCharacter(onLayer1, new Vector3(4f, -0.02f, 0f), Tick, CharacterSettings.Default, new QueryFilter(~1, default, false)).Moved.X, 1e-3f);
                Assert.AreEqual(1.6f, world.MoveCharacter(onLayer0, new Vector3(4f, -0.02f, 0f), Tick, CharacterSettings.Default).Moved.X, 0.05f);
            }
        }

        [Test]
        public void ABodyPlacedOrCreatedInTheTickBlocksTheCharacterBeforeTheNextStep()
        {
            using (var world = new RapierWorld(Gravity))
            {
                world.AddStatic(new[] { Ground() });
                BodyHandle character = world.CreateBody(Character(Start));
                BodyHandle gate = world.CreateBody(new BodyDesc(BodyKind.Kinematic, new[] { Box(new Vector3(0.1f, 1f, 1f), Vector3.Zero, 0, false) }, new Vector3(0f, 1f, 20f), Quaternion.Identity, 1f));
                world.Step(Tick);

                world.SetBody(gate, new BodyState { Position = new Vector3(1f, 1f, 0f), Rotation = Quaternion.Identity });
                Assert.AreEqual(0.6f, world.MoveCharacter(character, new Vector3(2f, -0.02f, 0f), Tick, CharacterSettings.Default).Moved.X, 0.05f);
                Assert.AreEqual(2f, world.MoveCharacter(character, new Vector3(2f, -0.02f, 0f), Tick, CharacterSettings.Default, new QueryFilter(~0, gate, false)).Moved.X, 1e-3f);
                world.Step(Tick);

                world.SetBody(gate, new BodyState { Position = new Vector3(0f, 1f, 20f), Rotation = Quaternion.Identity });
                float x = world.GetBody(character).Position.X;
                Assert.AreEqual(2f, x, 1e-3f);
                Assert.AreEqual(2f, world.MoveCharacter(character, new Vector3(2f, -0.02f, 0f), Tick, CharacterSettings.Default).Moved.X, 1e-3f);
                world.AddStatic(new[] { Box(new Vector3(0.1f, 1f, 1f), new Vector3(x + 1f, 1f, 0f), 0, false) });
                Assert.AreEqual(0.6f, world.MoveCharacter(character, new Vector3(2f, -0.02f, 0f), Tick, CharacterSettings.Default).Moved.X, 0.05f);
                world.CreateBody(new BodyDesc(BodyKind.Dynamic, new[] { Box(new Vector3(0.1f, 1f, 1f), Vector3.Zero, 0, false) }, new Vector3(x - 1f, 1f, 0f), Quaternion.Identity, 1f));
                Assert.AreEqual(-0.6f, world.MoveCharacter(character, new Vector3(-2f, -0.02f, 0f), Tick, CharacterSettings.Default).Moved.X, 0.05f);
            }
        }

        [Test]
        public void AutostepClimbsALowStepAndPushMassPushesADynamicBody()
        {
            using (var world = new RapierWorld(Gravity))
            {
                world.AddStatic(new[] { Ground(), Box(new Vector3(1f, 0.15f, 2f), new Vector3(2f, 0.15f, 0f), 0, false) });
                BodyHandle climber = world.CreateBody(Character(Start));
                BodyHandle blocked = world.CreateBody(Character(new Vector3(0f, 0.81f, 1f)));
                world.Step(Tick);

                CharacterSettings stepping = CharacterSettings.Default.WithAutostep(CharacterAutostep.Default);
                for (int tick = 0; tick < 30; tick++)
                {
                    world.MoveCharacter(climber, new Vector3(0.1f, -0.02f, 0f), Tick, stepping);
                    world.MoveCharacter(blocked, new Vector3(0.1f, -0.02f, 0f), Tick, CharacterSettings.Default);
                    world.Step(Tick);
                }

                Assert.Greater(world.GetBody(climber).Position.X, 2.5f);
                Assert.AreEqual(1.11f, world.GetBody(climber).Position.Y, 0.02f);
                Assert.AreEqual(0.69f, world.GetBody(blocked).Position.X, 0.05f);
            }

            using (var world = new RapierWorld(Gravity))
            {
                world.AddStatic(new[] { Ground() });
                BodyHandle pusher = world.CreateBody(Character(Start));
                BodyHandle still = world.CreateBody(Character(new Vector3(0f, 0.81f, 3f)));
                BodyHandle pushed = world.CreateBody(Crate(new Vector3(1f, 0.3f, 0f)));
                BodyHandle untouched = world.CreateBody(Crate(new Vector3(1f, 0.3f, 3f)));
                for (int tick = 0; tick < 30; tick++)
                {
                    world.Step(Tick);
                }

                world.MoveCharacter(pusher, new Vector3(1f, -0.02f, 0f), Tick, CharacterSettings.Default.WithPushMass(5f));
                world.MoveCharacter(still, new Vector3(1f, -0.02f, 0f), Tick, CharacterSettings.Default);
                Assert.Greater(world.GetBody(pushed).Velocity.X, 1f);
                Assert.AreEqual(0f, world.GetBody(untouched).Velocity.X, 1e-3f);
            }
        }

        [Test]
        public void SlidingIsSetOnASlopeAtLeastAsSteepAsMinSlopeSlideAngleOnly()
        {
            Assert.IsFalse(LandOn(0f));
            Assert.IsFalse(LandOn(30f));
            Assert.IsTrue(LandOn(60f));

            using (var world = new RapierWorld(Gravity))
            {
                world.AddStatic(new[] { Box(new Vector3(0.5f, 5f, 5f), new Vector3(0.81f, 5f, 0f), 0, false) });
                BodyHandle character = world.CreateBody(Character(new Vector3(0f, 5f, 0f)));
                world.Step(Tick);

                CharacterMove move = world.MoveCharacter(character, new Vector3(0.5f, -1f, 0f), Tick, CharacterSettings.Default);
                Assert.AreEqual(-1f, move.Moved.Y, 1e-3f);
                Assert.IsFalse(move.Sliding);
            }
        }

        [Test]
        public void AMoveSeesBodiesBeforeTheyAreIndexedAndRepeatsBitForBitAfterLoad()
        {
            using (var world = new RapierWorld(Gravity))
            {
                world.AddStatic(new[] { Ground() });
                var boxes = new BodyHandle[64];
                var floating = new BodyMotion(BodyLocks.None, false, 0f, 0f);
                for (int index = 0; index < boxes.Length; index++)
                {
                    var position = new Vector3(index % 8 * 1.5f - 6f, 0.5f + index % 3 * 0.25f, index / 8 * 1.5f - 6f);
                    BodyKind kind = index % 2 == 0 ? BodyKind.Dynamic : BodyKind.Static;
                    boxes[index] = world.CreateBody(new BodyDesc(kind, new[] { Box(new Vector3(0.4f), Vector3.Zero, 0, false) }, position, Quaternion.CreateFromAxisAngle(Vector3.UnitY, index * 0.3f), 1f, floating));
                }

                BodyHandle character = world.CreateBody(Character(new Vector3(0.75f, 0.81f, 0.75f)));
                CharacterSettings settings = CharacterSettings.Default.WithAutostep(CharacterAutostep.Default);
                CharacterMove[] beforeStep = Moves(world, character, settings);
                world.SetBody(character, world.GetBody(character));
                world.Step(Tick);
                CharacterMove[] indexed = Moves(world, character, settings);
                Assert.AreEqual(Text(beforeStep), Text(indexed));

                for (int index = 1; index < boxes.Length; index += 2)
                {
                    world.SetKind(boxes[index], BodyKind.Kinematic);
                }

                world.SetBody(character, world.GetBody(character));
                world.Step(Tick);
                PhysicsSnapshot saved = world.CreateSnapshot();
                world.Save(saved);
                string first = Replayed(world, boxes, character, settings);
                world.Load(saved);

                Assert.AreEqual(first, Replayed(world, boxes, character, settings));
            }
        }

        [Test]
        public void ACharacterNeedsOneSolidBoxSphereOrCapsuleOnAKinematicBodyAndValidSettings()
        {
            using (var world = new RapierWorld(Gravity))
            {
                BodyHandle dynamic = world.CreateBody(new BodyDesc(BodyKind.Dynamic, new[] { Capsule(0, false) }, Start, Quaternion.Identity, 1f));
                BodyHandle twoSolid = world.CreateBody(new BodyDesc(BodyKind.Kinematic, new[] { Capsule(0, false), Capsule(0, false) }, Start, Quaternion.Identity, 1f));
                BodyHandle withTrigger = world.CreateBody(new BodyDesc(BodyKind.Kinematic, new[] { Capsule(0, false), Capsule(0, true) }, new Vector3(5f, 0.81f, 0f), Quaternion.Identity, 1f));
                BodyHandle hull = world.CreateBody(new BodyDesc(BodyKind.Kinematic, new[] { new ColliderDesc(BodyShape.ConvexHull(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ }), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, 0, false) }, new Vector3(10f, 0f, 0f), Quaternion.Identity, 1f));
                BodyHandle character = world.CreateBody(Character(new Vector3(-5f, 0.81f, 0f)));

                Assert.Throws<ArgumentException>(() => world.MoveCharacter(dynamic, Vector3.UnitX, Tick, CharacterSettings.Default));
                Assert.Throws<ArgumentException>(() => world.MoveCharacter(twoSolid, Vector3.UnitX, Tick, CharacterSettings.Default));
                Assert.Throws<ArgumentException>(() => world.MoveCharacter(hull, Vector3.UnitX, Tick, CharacterSettings.Default));
                Assert.Throws<ArgumentException>(() => world.MoveCharacter(default, Vector3.UnitX, Tick, CharacterSettings.Default));
                Assert.Throws<ArgumentException>(() => world.MoveCharacter(character, Vector3.UnitX, -Tick, CharacterSettings.Default));
                Assert.Throws<ArgumentException>(() => world.MoveCharacter(character, Vector3.UnitX, float.NaN, CharacterSettings.Default));
                Assert.Throws<ArgumentException>(() => world.MoveCharacter(character, new Vector3(float.NaN, 0f, 0f), Tick, CharacterSettings.Default));
                Assert.AreEqual(1f, world.MoveCharacter(withTrigger, Vector3.UnitX, Tick, CharacterSettings.Default).Moved.X, 1e-3f);
                Assert.AreEqual(0f, world.MoveCharacter(character, Vector3.Zero, 0f, CharacterSettings.Default).Moved.X, 1e-3f);
            }

            Assert.Throws<ArgumentException>(() => CharacterLength.Absolute(-1f));
            Assert.Throws<ArgumentException>(() => CharacterLength.Relative(float.NaN));
            Assert.Throws<ArgumentException>(() => CharacterSettings.Default.WithOffset(CharacterLength.Absolute(0f)));
            Assert.Throws<ArgumentException>(() => CharacterSettings.Default.WithUp(Vector3.Zero));
            Assert.Throws<ArgumentException>(() => CharacterSettings.Default.WithSlopeAngles(-0.1f, 0.5f));
            Assert.Throws<ArgumentException>(() => CharacterSettings.Default.WithPushMass(-1f));

            CharacterSettings defaults = default;
            Assert.AreEqual(Vector3.UnitY, defaults.Up);
            Assert.AreEqual(0.01f, defaults.Offset.Value);
            Assert.IsTrue(defaults.Offset.IsRelative);
            Assert.IsTrue(defaults.Slide);
            Assert.IsNull(defaults.Autostep);
            Assert.AreEqual(MathF.PI / 4f, defaults.MaxSlopeClimbAngle, 1e-6f);
            Assert.AreEqual(MathF.PI / 4f, defaults.MinSlopeSlideAngle, 1e-6f);
            Assert.AreEqual(0.2f, defaults.SnapToGround.Value.Value);
            Assert.AreEqual(1e-4f, defaults.NormalNudgeFactor);
            Assert.AreEqual(0f, defaults.PushMass);
            CharacterSettings changed = defaults.WithPushMass(3f);
            Assert.AreEqual(3f, changed.PushMass);
            Assert.IsTrue(changed.Slide);
            Assert.AreEqual(0.2f, changed.SnapToGround.Value.Value);
            Assert.IsFalse(changed.WithSlide(false).Slide);
            Assert.IsNull(changed.WithSnapToGround(null).SnapToGround);
        }

        private static bool LandOn(float degrees)
        {
            using (var world = new RapierWorld(Gravity))
            {
                Quaternion slope = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, degrees * MathF.PI / 180f);
                world.AddStatic(new[] { new ColliderDesc(BodyShape.Box(new Vector3(5f, 0.5f, 5f)), Vector3.Zero, slope, ColliderMaterial.Default, 0, false) });
                BodyHandle character = world.CreateBody(Character(new Vector3(0f, 4f, 0f)));
                world.Step(Tick);

                return world.MoveCharacter(character, new Vector3(0f, -3f, 0f), Tick, CharacterSettings.Default).Sliding;
            }
        }

        private static CharacterMove[] Moves(RapierWorld world, BodyHandle character, in CharacterSettings settings)
        {
            var moves = new CharacterMove[16];
            for (int index = 0; index < moves.Length; index++)
            {
                float angle = index * MathF.PI / 8f;
                moves[index] = world.MoveCharacter(character, new Vector3(MathF.Cos(angle) * 2f, -0.05f, MathF.Sin(angle) * 2f), Tick, settings);
            }

            return moves;
        }

        private static string Text(CharacterMove[] moves)
        {
            var text = new System.Text.StringBuilder();
            foreach (CharacterMove move in moves)
            {
                text.Append(BitConverter.SingleToInt32Bits(move.Moved.X)).Append(',')
                    .Append(BitConverter.SingleToInt32Bits(move.Moved.Y)).Append(',')
                    .Append(BitConverter.SingleToInt32Bits(move.Moved.Z)).Append(',')
                    .Append(move.Grounded).Append(',').Append(move.Sliding).Append(';');
            }

            return text.ToString();
        }

        private static string Replayed(RapierWorld world, BodyHandle[] boxes, BodyHandle character, in CharacterSettings settings)
        {
            var text = new System.Text.StringBuilder();
            for (int tick = 0; tick < 20; tick++)
            {
                BodyHandle box = boxes[(tick * 2 + 1) % boxes.Length];
                BodyState moved = world.GetBody(box);
                moved.Position += new Vector3(0.05f, 0f, 0.05f);
                world.SetBody(box, moved);
                CharacterMove move = world.MoveCharacter(character, new Vector3(0.2f, -0.05f, 0.1f), Tick, settings);
                world.Step(Tick);
                text.Append(BitConverter.SingleToInt32Bits(move.Moved.X)).Append(',')
                    .Append(BitConverter.SingleToInt32Bits(move.Moved.Y)).Append(',')
                    .Append(BitConverter.SingleToInt32Bits(move.Moved.Z)).Append(',')
                    .Append(move.Grounded).Append(',').Append(move.Sliding).Append(';');
            }

            return text.Append(world.StateHash).ToString();
        }

        private static ColliderDesc Ground() => Box(new Vector3(20f, 0.5f, 20f), new Vector3(0f, -0.5f, 0f), 0, false);

        private static ColliderDesc Box(Vector3 halfExtents, Vector3 position, int layer, bool isTrigger) =>
            new ColliderDesc(BodyShape.Box(halfExtents), position, Quaternion.Identity, ColliderMaterial.Default, layer, isTrigger);

        private static ColliderDesc Capsule(int layer, bool isTrigger) =>
            new ColliderDesc(BodyShape.Capsule(0.3f, 0.5f), Vector3.Zero, Quaternion.Identity, ColliderMaterial.Default, layer, isTrigger);

        private static BodyDesc Character(Vector3 position, int layer = 0) =>
            new BodyDesc(BodyKind.Kinematic, new[] { Capsule(layer, false) }, position, Quaternion.Identity, 1f);

        private static BodyDesc Crate(Vector3 position) =>
            new BodyDesc(BodyKind.Dynamic, new[] { Box(new Vector3(0.3f), Vector3.Zero, 0, false) }, position, Quaternion.Identity, 1f);
    }
}
