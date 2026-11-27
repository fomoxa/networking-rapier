using System.Collections.Generic;
using System.Linq;
using Fomoxa.Networking;
using Fomoxa.Networking.Objects;
using Fomoxa.Networking.Sessions;
using Fomoxa.Unity;
using UnityEngine.SceneManagement;

namespace FomoxaChecks
{
    public sealed class RoomsClientRun : RoomsRun
    {
        private const int JudgeTicks = 240;
        private const string Address = "127.0.0.1";

        private readonly ushort port;
        private RoomsPlayer player;
        private int ticksInRoom;
        private string verdict;
        private string mismatch;

        public RoomsClientRun(ushort port)
        {
            this.port = port;
        }

        public override string Progress =>
            $"state {Client.State}, objects {Client.Spawned.Count}, own player {(player != null ? player.NetworkObject.ObjectId : 0)}, ticks in the room {ticksInRoom}";

        private ClientManager Client => Manager.ClientManager;

        public override string Poll()
        {
            if (mismatch != null)
            {
                return "FAIL client: " + mismatch;
            }

            return verdict != null && Client.State != ConnectionState.Started ? verdict : null;
        }

        public override void Stop()
        {
            Manager.TimeManager.OnTick -= OnTick;
            Client.StopConnection();
        }

        protected override void Begin()
        {
            Client.OnObjectMismatch += OnMismatch;
            Manager.TimeManager.OnTick += OnTick;
            Client.StartConnection(Address, port);
        }

        private void OnMismatch(ObjectMismatchArgs args)
        {
            mismatch ??= $"object mismatch {args.Kind} for object {args.ObjectId}";
        }

        private void OnTick()
        {
            if (player == null)
            {
                player = Client.Spawned.Values.OfType<NetworkObject>().Where(networkObject => networkObject.IsOwner)
                    .Select(networkObject => networkObject.GetComponent<RoomsPlayer>()).FirstOrDefault();
                return;
            }

            if (player.SceneChanges == 0 || verdict != null)
            {
                return;
            }

            ticksInRoom++;
            if (ticksInRoom >= JudgeTicks)
            {
                verdict = Judge();
            }
        }

        private string Judge()
        {
            var problems = new List<string>();
            if (player.SceneChanges != 1)
            {
                problems.Add($"the own player changed scene {player.SceneChanges} times");
            }

            if (!Client.Scenes.TryGetScene(player.Scene, out Scene room))
            {
                problems.Add($"room {player.Scene.Id} is not loaded");
            }

            List<NetworkObject> spawned = Client.Spawned.Values.OfType<NetworkObject>().ToList();
            int outside = spawned.Count(networkObject => networkObject.gameObject.scene != room);
            if (outside != 0)
            {
                problems.Add($"{outside} objects are outside room {player.Scene.Id}");
            }

            if (spawned.Count != 2 + RoomsLayout.CratesPerRoom)
            {
                problems.Add($"the client sees {spawned.Count} objects");
            }

            int players = spawned.Count(networkObject => networkObject.GetComponent<RoomsPlayer>() != null);
            if (players != 2)
            {
                problems.Add($"the client sees {players} players");
            }

            if (!player.IsPredicting)
            {
                problems.Add("the owner does not predict its player after the move");
            }

            if (RoomsContacts.Crossings != 0)
            {
                problems.Add($"{RoomsContacts.Crossings} contacts crossed rooms, first: {RoomsContacts.FirstCrossing}");
            }

            return problems.Count == 0
                ? $"PASS client: room {player.Scene.Id}, {spawned.Count} objects, predicting, {RoomsContacts.Contacts} contacts, none across rooms"
                : "FAIL client: " + string.Join("; ", problems);
        }
    }
}
