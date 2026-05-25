namespace VpnNotes.Core.Models
{
    public class MetricsSnapshot
    {
        public int MachineId { get; set; }
        public DateTime Timestamp { get; set; }
        public float CpuPercent { get; set; }
        public int RamUsedMb { get; set; }
        public int RamTotalMb { get; set; }
        public float DiskUsedGb { get; set; }
        public float DiskTotalGb { get; set; }
    }
}