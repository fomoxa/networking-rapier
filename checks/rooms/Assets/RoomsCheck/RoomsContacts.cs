using Fomoxa.Unity;
using UnityEngine;

namespace FomoxaChecks
{
    [RequireComponent(typeof(NetworkCollision))]
    public sealed class RoomsContacts : MonoBehaviour
    {
        private NetworkCollision collision;

        public static int Contacts { get; private set; }

        public static int Crossings { get; private set; }

        public static string FirstCrossing { get; private set; }

        private void OnEnable()
        {
            collision = GetComponent<NetworkCollision>();
            collision.OnEnter += Record;
        }

        private void OnDisable()
        {
            collision.OnEnter -= Record;
        }

        private void Record(Collider other)
        {
            Contacts++;
            if (other.gameObject.scene == gameObject.scene)
            {
                return;
            }

            Crossings++;
            FirstCrossing ??= $"{name} in scene {gameObject.scene.handle} touched {other.name} in scene {other.gameObject.scene.handle}";
        }
    }
}
