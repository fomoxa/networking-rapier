using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Fomoxa.Networking.Rapier.Bench
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            var ragdolls = new List<int> { 10, 100, 500 };
            int fallingTicks = 30;
            int settleTicks = 600;
            int restingTicks = 60;
            for (int index = 0; index < args.Length; index += 2)
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException($"{args[index]} needs a value");
                }

                string value = args[index + 1];
                switch (args[index])
                {
                    case "--ragdolls":
                        ragdolls.Clear();
                        foreach (string part in value.Split(','))
                        {
                            ragdolls.Add(int.Parse(part, CultureInfo.InvariantCulture));
                        }

                        break;
                    case "--falling-ticks":
                        fallingTicks = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "--settle-ticks":
                        settleTicks = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "--resting-ticks":
                        restingTicks = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    default:
                        throw new ArgumentException($"unknown option {args[index]}");
                }
            }

            Console.WriteLine($"{RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}, {Environment.ProcessorCount} logical processors, {RuntimeInformation.FrameworkDescription}");
            Console.WriteLine($"{Ragdoll.BodyCount} bodies per ragdoll, no joints; falling: the first {fallingTicks} ticks after the spawn; resting: {restingTicks} ticks after {settleTicks} more");
            Console.WriteLine();
            Console.WriteLine("| Ragdolls | Bodies | Phase | Mean ms | p50 ms | p95 ms | Max ms | Slots read per tick |");
            Console.WriteLine("|---:|---:|---|---:|---:|---:|---:|---:|");
            foreach (int count in ragdolls)
            {
                (PhaseResult falling, PhaseResult resting) = new RagdollBench(count, fallingTicks, settleTicks, restingTicks).Run();
                Print(count, "falling", falling);
                Print(count, "resting", resting);
            }

            return 0;
        }

        private static void Print(int ragdolls, string phase, PhaseResult result) =>
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "| {0} | {1} | {2} | {3:F3} | {4:F3} | {5:F3} | {6:F3} | {7:F1} |",
                ragdolls,
                ragdolls * Ragdoll.BodyCount,
                phase,
                result.MeanMs,
                result.MedianMs,
                result.P95Ms,
                result.MaxMs,
                result.SlotsPerTick));
    }
}
