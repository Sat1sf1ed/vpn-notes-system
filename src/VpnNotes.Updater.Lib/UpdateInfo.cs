namespace VpnNotes.Updater.Lib
{
    public class UpdateInfo
    {
        public Version Version { get; }
        public string DownloadUrl { get; }
        public long Size { get; }

        public UpdateInfo(Version version, string downloadUrl, long size)
        {
            Version = version;
            DownloadUrl = downloadUrl;
            Size = size;
        }
    }
}