using System;
using System.Collections.Generic;
using System.Diagnostics;
using Fomoxa.Networking;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace FomoxaChecks
{
    public sealed class NetworkTickTimer : IDisposable
    {
        private readonly TimeManager time;
        private readonly Stopwatch stopwatch = new Stopwatch();
        private readonly List<double> samples = new List<double>();
        private double pendingMilliseconds;
        private int ticksThisFrame;

        public NetworkTickTimer(TimeManager time)
        {
            this.time = time;
            time.OnPreTick += CountTick;
            Install();
        }

        public bool Recording { get; set; }

        public IReadOnlyList<double> Samples => samples;

        public void Dispose()
        {
            time.OnPreTick -= CountTick;
            PlayerLoopSystem root = PlayerLoop.GetCurrentPlayerLoop();
            PlayerLoopSystem[] phases = root.subSystemList;
            for (int index = 0; index < phases.Length; index++)
            {
                if (phases[index].subSystemList != null)
                {
                    phases[index].subSystemList = Array.FindAll(phases[index].subSystemList, system => system.type != typeof(NetworkTickTimer));
                }
            }

            PlayerLoop.SetPlayerLoop(root);
        }

        private void Install()
        {
            PlayerLoopSystem root = PlayerLoop.GetCurrentPlayerLoop();
            PlayerLoopSystem[] phases = root.subSystemList;
            for (int index = 0; index < phases.Length; index++)
            {
                if (phases[index].type == typeof(EarlyUpdate))
                {
                    var systems = new List<PlayerLoopSystem>(phases[index].subSystemList);
                    systems.Insert(1, new PlayerLoopSystem { type = typeof(NetworkTickTimer), updateDelegate = End });
                    systems.Insert(0, new PlayerLoopSystem { type = typeof(NetworkTickTimer), updateDelegate = Begin });
                    phases[index].subSystemList = systems.ToArray();
                }
                else if (phases[index].type == typeof(PostLateUpdate))
                {
                    var systems = new List<PlayerLoopSystem>(phases[index].subSystemList);
                    systems.Add(new PlayerLoopSystem { type = typeof(NetworkTickTimer), updateDelegate = Publish });
                    phases[index].subSystemList = systems.ToArray();
                }
            }

            PlayerLoop.SetPlayerLoop(root);
        }

        private void CountTick() => ticksThisFrame++;

        private void Begin() => stopwatch.Restart();

        private void End()
        {
            stopwatch.Stop();
            pendingMilliseconds += stopwatch.Elapsed.TotalMilliseconds;
        }

        private void Publish()
        {
            if (ticksThisFrame == 0)
            {
                return;
            }

            if (Recording)
            {
                for (int tick = 0; tick < ticksThisFrame; tick++)
                {
                    samples.Add(pendingMilliseconds / ticksThisFrame);
                }
            }

            pendingMilliseconds = 0;
            ticksThisFrame = 0;
        }
    }
}
