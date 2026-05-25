using System.Runtime.InteropServices;

namespace VpnNotes.Watcher.Collectors
{
    public static class MemoryCollector
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        public static (int usedMb, int totalMb) Read()
        {
            MEMORYSTATUSEX status = new MEMORYSTATUSEX();
            bool success = GlobalMemoryStatusEx(status);
            if (!success)
            {
                throw new InvalidOperationException("GlobalMemoryStatusEx failed");
            }

            int totalMb = (int)(status.ullTotalPhys / 1024 / 1024);
            int availMb = (int)(status.ullAvailPhys / 1024 / 1024);
            int usedMb = totalMb - availMb;

            return (usedMb, totalMb);
        }
    }
}