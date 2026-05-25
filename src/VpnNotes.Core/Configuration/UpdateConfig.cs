using YamlDotNet.Serialization;

namespace VpnNotes.Core.Configuration
{
    public class UpdateConfig
    {
        [YamlMember(Alias = "current_version")]
        public string CurrentVersion { get; set; } = "1.0.0";

        [YamlMember(Alias = "github")]
        public GitHubConfig GitHub { get; set; } = new GitHubConfig();
    }

    public class GitHubConfig
    {
        [YamlMember(Alias = "owner")]
        public string Owner { get; set; } = string.Empty;

        [YamlMember(Alias = "repo")]
        public string Repo { get; set; } = string.Empty;
    }
}