using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Fomoxa.Networking;
using Fomoxa.Unity;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace FomoxaChecks
{
    public sealed class RoomsMeasureRun : RoomsRun
    {
        public const int WarmupTicks = 300;
        public const int MeasuredTicks = 1800;

        private const double Megabyte = 1024.0 * 1024.0;

        private readonly ushort port;
        private readonly int instances;
        private readonly HashSet<NetworkScene> rooms = new HashSet<NetworkScene>();
        private readonly List<NetworkObject> crates = new List<NetworkObject>();
        private int loaded;
        private uint startTick;
        private NetworkTickTimer timer;

        public RoomsMeasureRun(ushort port, int instances)
        {
            this.port = port;
            this.instances = instances;
        }

        public override string Progress => $"loaded {loaded} of {instances}, samples {timer?.Samples.Count ?? 0}";

        private ServerManager Server => Manager.ServerManager;

        public override string Poll() => timer != null && timer.Samples.Count >= MeasuredTicks ? Report() : null;

        public override void Stop()
        {
            Manager.TimeManager.OnTick -= OnTick;
            timer?.Dispose();
            Server.StopConnection();
        }

        protected override void Begin()
        {
            Server.Scenes.OnLoaded += OnLoaded;
            Manager.TimeManager.OnTick += OnTick;
            Server.StartConnection(port);
            uint roomId = Manager.Scenes.IdOf(RoomsCheck.RoomPath);
            for (int index = 0; index < instances; index++)
            {
                rooms.Add(Server.Scenes.Load(roomId, Array.Empty<ulong>(), ScenePhysics.Own));
            }
        }

        private void OnLoaded(NetworkScene scene)
        {
            if (!rooms.Contains(scene))
            {
                return;
            }

            Server.Scenes.TryGetScene(scene, out Scene room);
            crates.AddRange(RoomsLayout.SpawnCrates(Manager, CratePrefab, room));
            loaded++;
            if (loaded == instances)
            {
                timer = new NetworkTickTimer(Manager.TimeManager);
                startTick = Manager.TimeManager.Tick;
            }
        }

        private void OnTick()
        {
            uint tick = Manager.TimeManager.Tick;
            RoomsLayout.Kick(crates, tick);
            if (timer != null && tick - startTick == WarmupTicks)
            {
                timer.Recording = true;
            }
        }

        private string Report()
        {
            double[] sorted = timer.Samples.Take(MeasuredTicks).OrderBy(sample => sample).ToArray();
            return string.Format(
                CultureInfo.InvariantCulture,
                "PASS measure: instances={0} bodies={1} ticks={2} mean_ms={3:F3} p50_ms={4:F3} p95_ms={5:F3} max_ms={6:F3} working_set_mb={7:F1} managed_mb={8:F1} unity_reserved_mb={9:F1}",
                instances,
                crates.Count,
                sorted.Length,
                sorted.Average(),
                sorted[sorted.Length / 2],
                sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1],
                sorted[sorted.Length - 1],
                ProcessMemory.WorkingSetBytes() / Megabyte,
                GC.GetTotalMemory(false) / Megabyte,
                Profiler.GetTotalReservedMemoryLong() / Megabyte);
        }
    }
}
