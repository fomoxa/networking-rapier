using Fomoxa.Networking.Rapier;
using Fomoxa.Networking.Simulation;
using NUnit.Framework;
using UnityEngine;

namespace Fomoxa.Unity.Rapier.Tests
{
    public sealed class NetworkRapierTest
    {
        private const float Tick = 1f / 60f;

        [Test]
        public void TheWorldMovesACharacterFromUnityTypesLikeTheCoreCall()
        {
            using (var world = new RapierWorld(new System.Numerics.Vector3(0f, -9.81f, 0f)))
            {
                world.AddStatic(new[] { new ColliderDesc(BodyShape.Box(new System.Numerics.Vector3(20f, 0.5f, 20f)), new System.Numerics.Vector3(0f, -0.5f, 0f), System.Numerics.Quaternion.Identity, ColliderMaterial.Default, 0, false) });
                BodyHandle character = world.CreateBody(new BodyDesc(BodyKind.Kinematic, new[] { new ColliderDesc(BodyShape.Capsule(0.3f, 0.5f), System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, ColliderMaterial.Default, 0, false) }, new System.Numerics.Vector3(0f, 0.81f, 0f), System.Numerics.Quaternion.Identity, 1f));
                world.Step(Tick);

                CharacterMove core = world.MoveCharacter(character, new System.Numerics.Vector3(0.5f, -0.02f, 0f), Tick, CharacterSettings.Default);
                NetworkCharacterMove move = world.MoveCharacter(character, new Vector3(0.5f, -0.02f, 0f), Tick, CharacterSettings.Default);
                NetworkCharacterMove filtered = world.MoveCharacter(character, new Vector3(0.5f, -0.02f, 0f), Tick, CharacterSettings.Default, QueryFilter.All);

                Assert.AreEqual(0.5f, move.Moved.x, 1e-3f);
                Assert.AreEqual(core.Moved.X, move.Moved.x, 1e-6f);
                Assert.AreEqual(core.Moved.Y, move.Moved.y, 1e-6f);
                Assert.AreEqual(core.Grounded, move.Grounded);
                Assert.IsTrue(move.Grounded);
                Assert.AreEqual(core.Sliding, move.Sliding);
                Assert.AreEqual(core.Moved.X, filtered.Moved.x, 1e-6f);
            }
        }

        [Test]
        public void TheWorld2DMovesACharacterFromUnityTypesLikeTheCoreCall()
        {
            using (var world = new RapierWorld2D(new System.Numerics.Vector2(0f, -9.81f)))
            {
                world.AddStatic(new[] { new ColliderDesc2D(BodyShape2D.Box(new System.Numerics.Vector2(20f, 0.5f)), new System.Numerics.Vector2(0f, -0.5f), 0f, ColliderMaterial.Default, 0, false) });
                BodyHandle character = world.CreateBody(new BodyDesc2D(BodyKind.Kinematic, new[] { new ColliderDesc2D(BodyShape2D.Capsule(0.3f, 0.5f), System.Numerics.Vector2.Zero, 0f, ColliderMaterial.Default, 0, false) }, new System.Numerics.Vector2(0f, 0.81f), 0f, 1f));
                world.Step(Tick);

                CharacterMove2D core = world.MoveCharacter(character, new System.Numerics.Vector2(0.5f, -0.02f), Tick, CharacterSettings2D.Default);
                NetworkCharacterMove2D move = world.MoveCharacter(character, new Vector2(0.5f, -0.02f), Tick, CharacterSettings2D.Default);
                NetworkCharacterMove2D filtered = world.MoveCharacter(character, new Vector2(0.5f, -0.02f), Tick, CharacterSettings2D.Default, QueryFilter.All);

                Assert.AreEqual(0.5f, move.Moved.x, 1e-3f);
                Assert.AreEqual(core.Moved.X, move.Moved.x, 1e-6f);
                Assert.AreEqual(core.Grounded, move.Grounded);
                Assert.IsTrue(move.Grounded);
                Assert.AreEqual(core.Moved.X, filtered.Moved.x, 1e-6f);
            }
        }

        [Test]
        public void JointDescriptionsFromUnityTypesMatchTheCoreConstructors()
        {
            Quaternion frame = Quaternion.AngleAxis(90f, Vector3.forward);
            JointDesc hinge = NetworkJoints.Create(JointKind.Revolute, new Vector3(-0.5f, 0f, 0f), frame, new Vector3(1f, 2f, 3f), Quaternion.identity);
            JointDesc rope = NetworkJoints.Rope(new Vector3(0.5f, 0f, 0f), new Vector3(-0.5f, 0f, 0f), 1.5f);
            JointDesc spring = NetworkJoints.Spring(new Vector3(0.5f, 0f, 0f), new Vector3(-0.5f, 0f, 0f), 1f, 30f, 2f);
            JointDesc2D hinge2D = NetworkJoints.Create2D(JointKind.Revolute, new Vector2(-0.5f, 0f), 0.25f, new Vector2(0f, 10f), 0f);
            JointDesc2D rope2D = NetworkJoints.Rope2D(new Vector2(0.5f, 0f), new Vector2(-0.5f, 0f), 1.5f);
            JointDesc2D spring2D = NetworkJoints.Spring2D(new Vector2(0.5f, 0f), new Vector2(-0.5f, 0f), 1f, 30f, 2f);

            Assert.AreEqual(JointKind.Revolute, hinge.Kind);
            Assert.AreEqual(new System.Numerics.Vector3(-0.5f, 0f, 0f), hinge.AnchorA);
            Assert.AreEqual(new System.Numerics.Quaternion(frame.x, frame.y, frame.z, frame.w), hinge.FrameA);
            Assert.AreEqual(new System.Numerics.Vector3(1f, 2f, 3f), hinge.AnchorB);
            Assert.AreEqual(System.Numerics.Quaternion.Identity, hinge.FrameB);
            AssertSame(JointDesc.Rope(new System.Numerics.Vector3(0.5f, 0f, 0f), new System.Numerics.Vector3(-0.5f, 0f, 0f), 1.5f), rope);
            AssertSame(JointDesc.Spring(new System.Numerics.Vector3(0.5f, 0f, 0f), new System.Numerics.Vector3(-0.5f, 0f, 0f), 1f, 30f, 2f), spring);
            Assert.AreEqual(new System.Numerics.Vector2(-0.5f, 0f), hinge2D.AnchorA);
            Assert.AreEqual(0.25f, hinge2D.FrameA);
            Assert.AreEqual(new System.Numerics.Vector2(0f, 10f), hinge2D.AnchorB);
            AssertSame(JointDesc2D.Rope(new System.Numerics.Vector2(0.5f, 0f), new System.Numerics.Vector2(-0.5f, 0f), 1.5f), rope2D);
            AssertSame(JointDesc2D.Spring(new System.Numerics.Vector2(0.5f, 0f), new System.Numerics.Vector2(-0.5f, 0f), 1f, 30f, 2f), spring2D);
        }

        [Test]
        public void SettingsAndEffectorFramesTakeUnityVectors()
        {
            Assert.AreEqual(System.Numerics.Vector3.UnitZ, CharacterSettings.Default.WithUp(Vector3.forward).Up);
            Assert.AreEqual(System.Numerics.Vector2.UnitX, CharacterSettings2D.Default.WithUp(Vector2.right).Up);
            EffectorDesc2D surface = EffectorDesc2D.Surface(2f, 0.5f, false, true, true).WithFrame(new Vector2(-6f, 1f), 0.5f);
            Assert.AreEqual(new System.Numerics.Vector2(-6f, 1f), surface.Origin);
            Assert.AreEqual(0.5f, surface.Rotation);
        }

        private static void AssertSame(in JointDesc expected, in JointDesc actual)
        {
            Assert.AreEqual(expected.Kind, actual.Kind);
            Assert.AreEqual(expected.AnchorA, actual.AnchorA);
            Assert.AreEqual(expected.AnchorB, actual.AnchorB);
            Assert.AreEqual(expected.Length, actual.Length);
            Assert.AreEqual(expected.Stiffness, actual.Stiffness);
            Assert.AreEqual(expected.Damping, actual.Damping);
        }

        private static void AssertSame(in JointDesc2D expected, in JointDesc2D actual)
        {
            Assert.AreEqual(expected.Kind, actual.Kind);
            Assert.AreEqual(expected.AnchorA, actual.AnchorA);
            Assert.AreEqual(expected.AnchorB, actual.AnchorB);
            Assert.AreEqual(expected.Length, actual.Length);
            Assert.AreEqual(expected.Stiffness, actual.Stiffness);
            Assert.AreEqual(expected.Damping, actual.Damping);
        }
    }
}
