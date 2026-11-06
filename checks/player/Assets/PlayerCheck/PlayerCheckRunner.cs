using System;
using UnityEngine;

namespace Fomoxa.Rapier.PlayerCheck
{
    public sealed class PlayerCheckRunner : MonoBehaviour
    {
        private const string Tag = "[player-check] ";

        private string report = "running";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Begin()
        {
            new GameObject(nameof(PlayerCheckRunner)).AddComponent<PlayerCheckRunner>();
        }

        private void Start()
        {
            bool passed = false;
            string detail;
            try
            {
                string actual = string.Join("\n", GoldenScenario.Run());
                string expected = Resources.Load<TextAsset>("golden").text.Replace("\r", "").TrimEnd('\n');
                passed = actual == expected;
                detail = actual;
            }
            catch (Exception failure)
            {
                detail = failure.ToString();
            }

            report = $"{(passed ? "PASS" : "FAIL")} {Application.platform} {SystemInfo.processorType} {IntPtr.Size * 8}-bit\n{detail}";
            foreach (string line in report.Split('\n'))
            {
                Debug.Log(Tag + line);
            }

            if (Application.isBatchMode)
            {
                Application.Quit(passed ? 0 : 1);
            }
        }

        private void OnGUI()
        {
            GUI.skin.label.fontSize = Mathf.Max(14, Screen.height / 40);
            GUI.Label(new Rect(16f, 16f, Screen.width - 32f, Screen.height - 32f), report);
        }
    }
}
