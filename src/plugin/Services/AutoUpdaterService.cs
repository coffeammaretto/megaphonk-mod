using BepInEx.Logging;
using Megaphonk.Services;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Megaphonk.Common
{
    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; }

        [JsonPropertyName("assets")]
        public GitHubAsset[] Assets { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }
    }

    public class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }
    }

    public interface IAutoUpdaterService
    {
        public void Initialize();
        Task<bool> CheckAndUpdate();
        bool IsAnUpdateAvailable();
        string GetDownloadedVersion();
        public string GetLatestVersion();
        public bool IsModManagerInstall();
        public string GetCurrentVersion();
        public Task EnsureChangelogExists(string version, string pluginDirectory);
    }

    public class AutoUpdaterService : IAutoUpdaterService
    {
        private const string UPDATE_FILE_PREFIX = ".update_download_";
        private static readonly TimeSpan UPDATE_CHECK_COOLDOWN = TimeSpan.FromMinutes(5);

        private const string GITHUB_OWNER = "coffeammaretto";
        private const string GITHUB_REPO = "megaphonk-mod";
        private string currentVersion;
        private string pluginPath;
        private ManualLogSource logger;

        private bool isUpdateAvailable = false;
        private string downloadedVersion = "";
        private string latestVersion = "";
        private DateTime lastUpdateCheck = DateTime.MinValue;

        private bool isModManagerInstall = false;

        public AutoUpdaterService(ManualLogSource logger)
        {
            this.logger = logger;
        }

        private static readonly HttpClient httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        static AutoUpdaterService()
        {
            httpClient.DefaultRequestHeaders.Add("User-Agent", "MegaphonkMod-AutoUpdater");
        }


        public void Initialize()
        {
            this.currentVersion = MyPluginInfo.PLUGIN_VERSION;
            this.pluginPath = System.Reflection.Assembly.GetExecutingAssembly().Location;

            // Installed by a mod manager (r2modman, Gale): the Thunderstore package's manifest lies next to the dll, the
            // notice points to the manager instead of GitHub
            var pluginDirectory = Path.GetDirectoryName(pluginPath);
            isModManagerInstall = File.Exists(Path.Combine(pluginDirectory, "manifest.json")) || File.Exists(Path.Combine(Path.GetDirectoryName(pluginDirectory), "manifest.json"));

            DeleteSelfUpdaterLeftovers(pluginDirectory);
        }

        /// <summary>
        /// The mod used to download its updates and apply them with a generated ApplyUpdate.bat: remove what is left of it
        /// </summary>
        private void DeleteSelfUpdaterLeftovers(string pluginDirectory)
        {
            try
            {
                foreach (var file in Directory.GetFiles(pluginDirectory, UPDATE_FILE_PREFIX + "*").Append(Path.Combine(pluginDirectory, "ApplyUpdate.bat")))
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                        logger.LogInfo($"Deleted old self-updater file {Path.GetFileName(file)}");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Could not delete old self-updater files: {ex.Message}");
            }
        }

        public bool IsAnUpdateAvailable()
        {
            return isUpdateAvailable;
        }

        public bool IsModManagerInstall()
        {
            return isModManagerInstall;
        }

        public string GetDownloadedVersion()
        {
            return downloadedVersion;
        }

        public string GetLatestVersion()
        {
            if (string.IsNullOrEmpty(latestVersion))
            {
                var task = GetLatestRelease();
                task.Wait();
                var latestRelease = task.Result;
                if (latestRelease != null)
                {
                    latestVersion = latestRelease.TagName;
                }
            }

            return latestVersion;
        }

        public string GetCurrentVersion()
        {
            return currentVersion;
        }

        public async Task<bool> CheckAndUpdate()
        {
            if (isUpdateAvailable)
            {
                return true;
            }

            var timeSinceLastCheck = DateTime.UtcNow - lastUpdateCheck;
            if (timeSinceLastCheck < UPDATE_CHECK_COOLDOWN)
            {
                var remainingTime = UPDATE_CHECK_COOLDOWN - timeSinceLastCheck;
                logger.LogInfo($"Next update check in {remainingTime.TotalMinutes:F1} minutes.");
                return false;
            }

            latestVersion = "";
            lastUpdateCheck = DateTime.UtcNow;

            try
            {
                logger.LogInfo("Checking for updates...");

                var latestRelease = await GetLatestRelease();
                if (latestRelease == null)
                {
                    logger.LogInfo("No releases found");
                    return false;
                }

                latestVersion = latestRelease.TagName;

                if (IsNewerVersion(latestRelease.TagName, currentVersion))
                {
                    logger.LogInfo($"New version available: {latestRelease.TagName} (current: {currentVersion}). " +
                        (isModManagerInstall ? "Update it in your mod manager." : "Download it from GitHub."));
                    isUpdateAvailable = true;
                    downloadedVersion = latestRelease.TagName;
                    return true;
                }
                else
                {
                    logger.LogInfo($"Already on latest version: {currentVersion}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"Error checking for updates: {ex.Message}");
                return false;
            }
        }

        private async Task<GitHubRelease> GetLatestRelease()
        {
            var url = $"https://api.github.com/repos/{GITHUB_OWNER}/{GITHUB_REPO}/releases/latest";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError($"Failed to fetch releases: {response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<GitHubRelease>(json);
        }

        private bool IsNewerVersion(string remoteVersion, string localVersion)
        {
            remoteVersion = remoteVersion.TrimStart('v');
            localVersion = localVersion.TrimStart('v');

            try
            {
                var remote = new Version(remoteVersion);
                var local = new Version(localVersion);
                return remote > local;
            }
            catch
            {
                return string.Compare(remoteVersion, localVersion, StringComparison.OrdinalIgnoreCase) > 0;
            }
        }

        public async Task EnsureChangelogExists(string version, string pluginDirectory)
        {
            var changelogPath = Path.Combine(pluginDirectory, ChangelogService.CHANGELOG_FILENAME);

            if (File.Exists(changelogPath))
            {
                logger.LogInfo("CHANGELOG.toml already exists, skipping download");
                return;
            }

            try
            {
                logger.LogInfo($"CHANGELOG.toml not found locally, fetching from GitHub for version {version}...");

                var url = $"https://raw.githubusercontent.com/{GITHUB_OWNER}/{GITHUB_REPO}/{version}/src/plugin/CHANGELOG.toml";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning($"Could not fetch CHANGELOG.toml from {version} tag, trying main branch...");

                    url = $"https://raw.githubusercontent.com/{GITHUB_OWNER}/{GITHUB_REPO}/main/src/plugin/CHANGELOG.toml";
                    using var fallbackRequest = new HttpRequestMessage(HttpMethod.Get, url);
                    using var fallbackResponse = await httpClient.SendAsync(fallbackRequest);

                    if (!fallbackResponse.IsSuccessStatusCode)
                    {
                        logger.LogWarning("Could not fetch CHANGELOG.toml from main branch either");
                        return;
                    }

                    var content = await fallbackResponse.Content.ReadAsStringAsync();
                    await File.WriteAllTextAsync(changelogPath, content);
                    logger.LogInfo("CHANGELOG.toml fetched successfully from main branch");
                    return;
                }

                var versionContent = await response.Content.ReadAsStringAsync();
                await File.WriteAllTextAsync(changelogPath, versionContent);
                logger.LogInfo("CHANGELOG.toml fetched successfully from version tag");
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Failed to fetch CHANGELOG.toml: {ex.Message}");
            }
        }
    }
}
