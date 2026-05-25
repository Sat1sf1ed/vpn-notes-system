using YamlDotNet.Serialization;

namespace VpnNotes.Core.Configuration
{
    public class WatcherConfig
    {
        [YamlMember(Alias = "metrics_interval_seconds")]
        public int MetricsIntervalSeconds { get; set; } = 60;
    }
}