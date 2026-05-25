namespace VpnNotes.Watcher.Collectors
{
    public static class DiskCollector
    {
        public static (float usedGb, float totalGb) Read(string driveLetter)
        {
            DriveInfo info = new DriveInfo(driveLetter);

            if (!info.IsReady)
            {
                throw new InvalidOperationException($"Drive {driveLetter} is not ready");
            }

            float totalGb = info.TotalSize / 1024f / 1024f / 1024f;
            float availGb = info.AvailableFreeSpace / 1024f / 1024f / 1024f;
            float usedGb = totalGb - availGb;

            return (usedGb, totalGb);
        }
    }
}