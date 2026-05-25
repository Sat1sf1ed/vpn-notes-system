using System.IO.Compression;
using System.Net.Http.Json;

namespace VpnNotes.Updater.Lib
{
    public class GitHubUpdater
    {
        private readonly HttpClient _httpClient;
        private readonly string _owner;
        private readonly string _repo;

        public GitHubUpdater(string owner, string repo)
        {
            _owner = owner;
            _repo = repo;

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("VpnNotes-Updater/1.0");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<UpdateInfo?> CheckAsync(Version currentVersion)
        {
            string url = $"https://api.github.com/repos/{_owner}/{_repo}/releases/latest";

            try
            {
                GitHubRelease? release = await _httpClient.GetFromJsonAsync<GitHubRelease>(url);
                if (release == null)
                {
                    return null;
                }

                string tagWithoutV = release.TagName.TrimStart('v', 'V');
                if (!Version.TryParse(tagWithoutV, out Version? remoteVersion))
                {
                    return null;
                }

                if (remoteVersion <= currentVersion)
                {
                    return null;
                }

                // Ищем zip-архив среди ассетов релиза
                GitHubAsset? zipAsset = null;
                foreach (GitHubAsset asset in release.Assets)
                {
                    if (asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        zipAsset = asset;
                        break;
                    }
                }

                if (zipAsset == null)
                {
                    return null;
                }

                return new UpdateInfo(remoteVersion, zipAsset.BrowserDownloadUrl, zipAsset.Size);
            }
            catch (HttpRequestException)
            {
                // Нет интернета или GitHub недоступен — это нормально, не падаем
                return null;
            }
            catch (TaskCanceledException)
            {
                // Таймаут
                return null;
            }
        }

        public async Task<string> DownloadAsync(UpdateInfo info)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "VpnNotesUpdate");
            Directory.CreateDirectory(tempDir);

            string zipPath = Path.Combine(tempDir, $"vpn-notes-{info.Version}.zip");

            using HttpResponseMessage response = await _httpClient.GetAsync(
                info.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using Stream contentStream = await response.Content.ReadAsStreamAsync();
            await using FileStream fileStream = File.Create(zipPath);
            await contentStream.CopyToAsync(fileStream);

            return zipPath;
        }

        public string ExtractArchive(string zipPath)
        {
            string extractDir = Path.Combine(Path.GetTempPath(), "VpnNotesUpdate", "extracted");

            if (Directory.Exists(extractDir))
            {
                Directory.Delete(extractDir, recursive: true);
            }

            ZipFile.ExtractToDirectory(zipPath, extractDir);

            try
            {
                File.Delete(zipPath);
            }
            catch
            {
                // Не критично, если не удалили — TEMP очистится сам
            }

            return extractDir;
        }
    }
}
