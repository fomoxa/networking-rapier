using System.Collections.Generic;
using Fomoxa.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FomoxaChecks
{
    public static class RoomsLayout
    {
        public const int CratesPerRoom = 16;
        public const float HalfSize = 10f;
        private const int KickInterval = 90;

        public static readonly Vector3[] LobbyStarts =
        {
            new Vector3(-6f, 1f, 0f),
            new Vector3(-2f, 1f, 0f),
            new Vector3(2f, 1f, 0f),
            new Vector3(6f, 1f, 0f),
        };

        public static readonly Vector3[] RoomStarts =
        {
            new Vector3(-3f, 1f, -5f),
            new Vector3(3f, 1f, -5f),
        };

        public static Vector3 CrateStart(int index) => new Vector3(-6f + 4f * (index % 4), 1f, 0.5f + 2.5f * (index / 4));

        public static List<NetworkObject> SpawnCrates(NetworkManager manager, NetworkObject prefab, Scene scene)
        {
            var crates = new List<NetworkObject>();
            for (int index = 0; index < CratesPerRoom; index++)
            {
                NetworkObject crate = Object.Instantiate(prefab, CrateStart(index), Quaternion.identity);
                SceneManager.MoveGameObjectToScene(crate.gameObject, scene);
                manager.ServerManager.Spawn(crate);
                crates.Add(crate);
            }

            return crates;
        }

        public static void Kick(IReadOnlyList<NetworkObject> crates, uint tick)
        {
            for (int index = 0; index < crates.Count; index++)
            {
                int slot = index % CratesPerRoom;
                if ((tick + (uint)slot * 7) % KickInterval != 0)
                {
                    continue;
                }

                float angle = (slot * 37 + tick / KickInterval * 53) % 360 * Mathf.Deg2Rad;
                crates[index].Body.AddImpulse(new Vector3(Mathf.Cos(angle) * 4f, 2f, Mathf.Sin(angle) * 4f));
            }
        }
    }
}
