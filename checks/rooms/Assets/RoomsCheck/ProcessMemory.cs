using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FomoxaChecks
{
    public static class ProcessMemory
    {
        public static long WorkingSetBytes()
        {
            using (Process process = Process.GetCurrentProcess())
            {
                process.Refresh();
                long reported = process.WorkingSet64;
                if (reported != 0 || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return reported;
                }

                var counters = new MemoryCounters { Size = (uint)Marshal.SizeOf<MemoryCounters>() };
                return K32GetProcessMemoryInfo(process.Handle, ref counters, counters.Size) ? (long)counters.WorkingSetSize.ToUInt64() : 0;
            }
        }

        [DllImport("kernel32.dll")]
        private static extern bool K32GetProcessMemoryInfo(IntPtr process, ref MemoryCounters counters, uint size);

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryCounters
        {
            public uint Size;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize;
            public UIntPtr WorkingSetSize;
            public UIntPtr QuotaPeakPagedPoolUsage;
            public UIntPtr QuotaPagedPoolUsage;
            public UIntPtr QuotaPeakNonPagedPoolUsage;
            public UIntPtr QuotaNonPagedPoolUsage;
            public UIntPtr PagefileUsage;
            public UIntPtr PeakPagefileUsage;
        }
    }
}
