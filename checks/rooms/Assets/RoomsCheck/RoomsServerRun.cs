using System.Collections.Generic;
using System.Linq;
using Fomoxa.Networking;
using Fomoxa.Networking.Sessions;
using Fomoxa.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FomoxaChecks
{
    public sealed class RoomsServerRun : RoomsRun
    {
        public const int Clients = 4;

        private const int SettleTicks = 120;
        private const int CloseTicks = 900;

        private readonly ushort port;
        private readonly List<ulong> peers = new List<ulong>();
        private readonly HashSet<ulong> inLobby = new HashSet<ulong>();
        private readonly HashSet<ulong> inRoom = new HashSet<ulong>();
        private readonly HashSet<ulong> stopped = new HashSet<ulong>();
        private readonly List<NetworkObject> players = new List<NetworkObject>();
        private readonly List<NetworkObject> crates = new List<NetworkObject>();
        private NetworkScene lobby;
        private NetworkScene red;
        private NetworkScene blue;
        private int roomsLoaded;
        private uint movedAt;
        private string verdict;
        private bool closing;
        private string failure;

        public RoomsServerRun(ushort port)
        {
            this.port = port;
        }

        public override string Progress =>
            $"peers {peers.Count}, in the lobby {inLobby.Count}, rooms loaded {roomsLoaded}, peers in rooms {inRoom.Count}, moved at tick {movedAt}, stopped {stopped.Count}";

        private ServerManager Server => Manager.ServerManager;

        public override string Poll()
        {
            if (failure != null)
            {
                return "FAIL server: " + failure;
            }

            return closing ? verdict : null;
        }

        public override void Stop()
        {
            Manager.TimeManager.OnTick -= OnTick;
            Server.StopConnection();
        }

        protected override void Begin()
        {
            Server.OnRemoteConnectionState += OnPeer;
            Server.Scenes.OnPeerLoaded += OnPeerLoaded;
            Server.Scenes.OnLoaded += OnLoaded;
            Manager.TimeManager.OnTick += OnTick;
            Server.StartConnection(port);
            lobby = Server.Scenes.LoadGlobal(new[] { Manager.Scenes.IdOf(RoomsCheck.LobbyPath) })[0];
        }

        private void OnPeer(ConnectionStateArgs args)
        {
            if (args.State == ConnectionState.Started)
            {
                peers.Add(args.PeerId);
            }
            else if (args.State == ConnectionState.Stopped)
            {
                stopped.Add(args.PeerId);
                if (movedAt == 0)
                {
                    failure ??= $"peer {args.PeerId} stopped before the players moved ({args.Reason})";
                }
            }
        }

        private void OnPeerLoaded(ulong peerId, NetworkScene scene)
        {
            if (scene == lobby)
            {
                inLobby.Add(peerId);
                if (inLobby.Count == Clients && players.Count == 0)
                {
                    SpawnPlayers();
                }
            }
            else if (scene == red || scene == blue)
            {
                inRoom.Add(peerId);
                TryMove();
            }
        }

        private void SpawnPlayers()
        {
            Server.Scenes.TryGetScene(lobby, out Scene lobbyScene);
            for (int index = 0; index < Clients; index++)
            {
                NetworkObject player = Object.Instantiate(PlayerPrefab, RoomsLayout.LobbyStarts[index], Quaternion.identity);
                SceneManager.MoveGameObjectToScene(player.gameObject, lobbyScene);
                Server.Spawn(player, peers[index]);
                players.Add(player);
            }

            uint roomId = Manager.Scenes.IdOf(RoomsCheck.RoomPath);
            red = Server.Scenes.Load(roomId, new[] { peers[0], peers[1] }, ScenePhysics.Own);
            blue = Server.Scenes.Load(roomId, new[] { peers[2], peers[3] }, ScenePhysics.Own);
        }

        private void OnLoaded(NetworkScene scene)
        {
            if (scene != red && scene != blue)
            {
                return;
            }

            Server.Scenes.TryGetScene(scene, out Scene room);
            crates.AddRange(RoomsLayout.SpawnCrates(Manager, CratePrefab, room));
            roomsLoaded++;
            TryMove();
        }

        private void TryMove()
        {
            if (movedAt != 0 || roomsLoaded < 2 || inRoom.Count < Clients)
            {
                return;
            }

            for (int index = 0; index < Clients; index++)
            {
                NetworkObject player = players[index];
                if (!Server.MoveToScene(player, index < 2 ? red : blue))
                {
                    failure = $"MoveToScene returned false for player {player.ObjectId}";
                    return;
                }

                NetworkBody body = player.Body;
                NetworkBodyState state = body.State;
                state.Position = RoomsLayout.RoomStarts[index % 2];
                state.Velocity = Vector3.zero;
                body.State = state;
            }

            movedAt = Manager.TimeManager.Tick;
        }

        private void OnTick()
        {
            uint tick = Manager.TimeManager.Tick;
            RoomsLayout.Kick(crates, tick);
            if (movedAt == 0)
            {
                return;
            }

            if (verdict == null && tick - movedAt >= SettleTicks)
            {
                verdict = Judge();
            }

            if (tick - movedAt >= CloseTicks)
            {
                closing = true;
            }
        }

        private string Judge()
        {
            var problems = new List<string>();
            List<NetworkObject> spawned = Server.Spawned.Values.OfType<NetworkObject>().ToList();
            Server.Scenes.TryGetScene(lobby, out Scene lobbyScene);
            int left = spawned.Count(networkObject => networkObject.gameObject.scene == lobbyScene);
            if (left != 0)
            {
                problems.Add($"{left} objects are still in the lobby");
            }

            foreach ((NetworkScene room, int first) in new[] { (red, 0), (blue, 2) })
            {
                Server.Scenes.TryGetScene(room, out Scene roomScene);
                int count = spawned.Count(networkObject => networkObject.gameObject.scene == roomScene);
                if (count != 2 + RoomsLayout.CratesPerRoom)
                {
                    problems.Add($"room {room.Id} holds {count} objects");
                }

                for (int index = first; index < first + 2; index++)
                {
                    if (players[index].gameObject.scene != roomScene)
                    {
                        problems.Add($"player {players[index].ObjectId} is not in room {room.Id}");
                    }
                }
            }

            if (RoomsContacts.Crossings != 0)
            {
                problems.Add($"{RoomsContacts.Crossings} contacts crossed rooms, first: {RoomsContacts.FirstCrossing}");
            }

            if (RoomsContacts.Contacts == 0)
            {
                problems.Add("no contact was reported at all");
            }

            return problems.Count == 0
                ? $"PASS server: rooms {red.Id} and {blue.Id}, {spawned.Count} objects, {RoomsContacts.Contacts} contacts, none across rooms"
                : "FAIL server: " + string.Join("; ", problems);
        }
    }
}
