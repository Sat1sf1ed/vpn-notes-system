using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace VpnNotes.Core.Configuration
{
    public class AppConfig
    {
        [YamlMember(Alias = "database")]
        public DatabaseConfig Database { get; set; } = new DatabaseConfig();

        [YamlMember(Alias = "watcher")]
        public WatcherConfig Watcher { get; set; } = new WatcherConfig();

        [YamlMember(Alias = "update")]
        public UpdateConfig Update { get; set; } = new UpdateConfig();

        public static AppConfig Load(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Configuration file not found: {path}");
            }

            string yamlText = File.ReadAllText(path);

            IDeserializer deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            AppConfig? config = deserializer.Deserialize<AppConfig>(yamlText);
            if (config == null)
            {
                throw new InvalidOperationException("Failed to deserialize configuration");
            }

            return config;
        }
    }
}