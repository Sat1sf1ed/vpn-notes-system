using System.Diagnostics;

namespace VpnNotes.Watcher.Collectors
{
    public static class CpuCollector
    {
        private static readonly PerformanceCounter _counter =
            new PerformanceCounter("Processor", "% Processor Time", "_Total");

        private static bool _warmedUp = false;

        public static float Read()
        {
            if (!_warmedUp)
            {
                // PerformanceCounter всегда возвращает 0 при первом вызове.
                // Делаем "прогрев": читаем, ждём, читаем снова — тогда получаем настоящее значение.
                _counter.NextValue();
                Thread.Sleep(500);
                _warmedUp = true;
            }

            return _counter.NextValue();
        }
    }
}
