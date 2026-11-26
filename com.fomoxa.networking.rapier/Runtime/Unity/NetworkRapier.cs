using System.Collections.Generic;
using Fomoxa.Networking;
using Fomoxa.Networking.Rapier;
using Fomoxa.Networking.Simulation;
using UnityEngine;

namespace Fomoxa.Unity.Rapier
{
    public static class NetworkRapier
    {
        public static NetworkCharacterMove MoveCharacter(this RapierScenes scenes, INetworkEntity owner, NetworkBody body, Vector3 desired, float seconds, in CharacterSettings settings) =>
            NetworkCharacterMove.From(scenes.MoveCharacter(owner, body.Core, desired.ToNumerics(), seconds, settings));

        public static NetworkCharacterMove MoveCharacter(this RapierScenes scenes, INetworkEntity owner, NetworkBody body, Vector3 desired, float seconds, in CharacterSettings settings, in QueryFilter filter) =>
            NetworkCharacterMove.From(scenes.MoveCharacter(owner, body.Core, desired.ToNumerics(), seconds, settings, filter));

        public static NetworkCharacterMove2D MoveCharacter(this RapierScenes scenes, INetworkEntity owner, NetworkBody2D body, Vector2 desired, float seconds, in CharacterSettings2D settings) =>
            NetworkCharacterMove2D.From(scenes.MoveCharacter(owner, body.Core, desired.ToNumerics(), seconds, settings));

        public static NetworkCharacterMove2D MoveCharacter(this RapierScenes scenes, INetworkEntity owner, NetworkBody2D body, Vector2 desired, float seconds, in CharacterSettings2D settings, in QueryFilter filter) =>
            NetworkCharacterMove2D.From(scenes.MoveCharacter(owner, body.Core, desired.ToNumerics(), seconds, settings, filter));

        public static NetworkCharacterMove MoveCharacter(this RapierWorld world, BodyHandle body, Vector3 desired, float seconds, in CharacterSettings settings) =>
            NetworkCharacterMove.From(world.MoveCharacter(body, desired.ToNumerics(), seconds, settings));

        public static NetworkCharacterMove MoveCharacter(this RapierWorld world, BodyHandle body, Vector3 desired, float seconds, in CharacterSettings settings, in QueryFilter filter) =>
            NetworkCharacterMove.From(world.MoveCharacter(body, desired.ToNumerics(), seconds, settings, filter));

        public static NetworkCharacterMove2D MoveCharacter(this RapierWorld2D world, BodyHandle body, Vector2 desired, float seconds, in CharacterSettings2D settings) =>
            NetworkCharacterMove2D.From(world.MoveCharacter(body, desired.ToNumerics(), seconds, settings));

        public static NetworkCharacterMove2D MoveCharacter(this RapierWorld2D world, BodyHandle body, Vector2 desired, float seconds, in CharacterSettings2D settings, in QueryFilter filter) =>
            NetworkCharacterMove2D.From(world.MoveCharacter(body, desired.ToNumerics(), seconds, settings, filter));

        public static JointHandle CreateJoint(this RapierScenes scenes, INetworkEntity owner, NetworkBody a, NetworkBody b, in JointDesc desc) =>
            scenes.CreateJoint(owner, a.Core, b.Core, desc);

        public static JointHandle CreateJoint(this RapierScenes scenes, INetworkEntity owner, NetworkBody2D a, NetworkBody2D b, in JointDesc2D desc) =>
            scenes.CreateJoint(owner, a.Core, b.Core, desc);

        public static EffectorHandle AddEffector(this RapierScenes scenes, INetworkEntity owner, NetworkBody2D body, IReadOnlyList<int> colliders, in EffectorDesc2D desc) =>
            scenes.AddEffector(owner, body.Core, colliders, desc);

        public static EffectorHandle AddEffector(this RapierScenes scenes, NetworkBody2D body, IReadOnlyList<int> colliders, in EffectorDesc2D desc) =>
            scenes.AddEffector(body.Core, colliders, desc);

        public static CharacterSettings WithUp(this CharacterSettings settings, Vector3 value) => settings.WithUp(value.ToNumerics());

        public static CharacterSettings2D WithUp(this CharacterSettings2D settings, Vector2 value) => settings.WithUp(value.ToNumerics());

        public static EffectorDesc2D WithFrame(this EffectorDesc2D desc, Vector2 origin, float rotation) => desc.WithFrame(origin.ToNumerics(), rotation);
    }
}
