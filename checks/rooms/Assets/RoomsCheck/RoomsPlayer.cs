using Fomoxa.Networking;
using Fomoxa.Unity;
using UnityEngine;

namespace FomoxaChecks
{
    public sealed partial class RoomsPlayer : NetworkBehaviour
    {
        public const float Speed = 3f;

        [NetworkInput]
        private float x;

        [NetworkReconcile(Tolerance = 0.05f)]
        private float positionX;

        [NetworkReconcile(Tolerance = 0.05f)]
        private float positionZ;

        [NetworkReconcile(Tolerance = 0.1f)]
        private float velocityX;

        [NetworkReconcile(Tolerance = 0.1f)]
        private float velocityZ;

        public NetworkScene Scene { get; private set; }

        public int SceneChanges { get; private set; }

        public override void OnSceneChangedClient(NetworkScene previous, NetworkScene current)
        {
            Scene = current;
            SceneChanges++;
        }

        protected override void OnGatherInput()
        {
            x = Mathf.Sin(Time.realtimeSinceStartup * 1.5f);
        }

        protected override void OnApplyInput(InputContext context)
        {
            NetworkBody body = NetworkObject.Body;
            Vector3 velocity = body.Velocity;
            body.Velocity = new Vector3(x * Speed, velocity.y, 0f);
        }

        protected override void OnCaptureState()
        {
            NetworkBodyState body = NetworkObject.Body.State;
            positionX = body.Position.x;
            positionZ = body.Position.z;
            velocityX = body.Velocity.x;
            velocityZ = body.Velocity.z;
        }

        protected override void OnRestoreState()
        {
            NetworkBody body = NetworkObject.Body;
            NetworkBodyState current = body.State;
            current.Position = new Vector3(positionX, current.Position.y, positionZ);
            current.Velocity = new Vector3(velocityX, current.Velocity.y, velocityZ);
            body.State = current;
        }
    }
}
