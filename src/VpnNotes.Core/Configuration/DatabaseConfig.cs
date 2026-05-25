using YamlDotNet.Serialization;

namespace VpnNotes.Core.Configuration
{
    public class DatabaseConfig
    {
        [YamlMember(Alias = "host")]
        public string Host { get; set; } = "localhost";

        [YamlMember(Alias = "port")]
        public int Port { get; set; } = 5432;

        [YamlMember(Alias = "database")]
        public string Database { get; set; } = "vpnnotes";
    }
}