using System;
using System.IO;
using Fomoxa.Unity;
using UnityEngine;

namespace FomoxaChecks
{
    public sealed class RoomsCheck : MonoBehaviour
    {
        public const string ServerArgument = "-fomoxa-rooms-server";
        public const string ClientArgument = "-fomoxa-rooms-client";
        public const string MeasureArgument = "-fomoxa-rooms-measure";
        public const string BuiltFolder = "Assets/RoomsCheck/Built";
        public const string LobbyPath = BuiltFolder + "/Lobby.unity";
        public const string RoomPath = BuiltFolder + "/Room.unity";

        private const float TimeoutSeconds = 240f;

        [SerializeField] private NetworkManager manager;
        [SerializeField] private NetworkObject playerPrefab;
        [SerializeField] private NetworkObject cratePrefab;

        private RoomsRun run;
        private string resultPath;
        private float deadline;
        private bool finished;

        private void Start()
        {
            Application.targetFrameRate = 120;
            string[] arguments = Environment.GetCommandLineArgs();
            if (TryRead(arguments, ServerArgument, 2, out string[] values))
            {
                run = new RoomsServerRun(ushort.Parse(values[0]));
            }
            else if (TryRead(arguments, ClientArgument, 2, out values))
            {
                run = new RoomsClientRun(ushort.Parse(values[0]));
            }
            else if (TryRead(arguments, MeasureArgument, 3, out values))
            {
                run = new RoomsMeasureRun(ushort.Parse(values[0]), int.Parse(values[1]));
            }
            else
            {
                Debug.LogError($"[rooms-check] start with {ServerArgument}, {ClientArgument} or {MeasureArgument}");
                Application.Quit(2);
                return;
            }

            resultPath = values[values.Length - 1];
            deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            run.Start(manager, playerPrefab, cratePrefab);
        }

        private void Update()
        {
            if (run == null || finished)
            {
                return;
            }

            string result = run.Poll();
            if (result == null && Time.realtimeSinceStartup > deadline)
            {
                result = "FAIL timed out: " + run.Progress;
            }

            if (result != null)
            {
                Finish(result);
            }
        }

        private void Finish(string result)
        {
            finished = true;
            Debug.Log("[rooms-check] " + result);
            File.WriteAllText(resultPath, result);
            run.Stop();
            Application.Quit(result.StartsWith("PASS", StringComparison.Ordinal) ? 0 : 1);
        }

        private static bool TryRead(string[] arguments, string name, int count, out string[] values)
        {
            int at = Array.IndexOf(arguments, name);
            if (at < 0 || at + count >= arguments.Length)
            {
                values = null;
                return false;
            }

            values = new string[count];
            Array.Copy(arguments, at + 1, values, 0, count);
            return true;
        }
    }
}
