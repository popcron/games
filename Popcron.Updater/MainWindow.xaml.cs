using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;

using Newtonsoft.Json;
using Octokit;

namespace Popcron.Updater
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Settings settings;

        private string Destination
        {
            get
            {
                string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(folderPath, settings.repositoryOwner, settings.gameName);
            }
        }

        private string Executable
        {
            get
            {
                string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(folderPath, settings.repositoryOwner, settings.gameName, settings.execName);
            }
        }

        private string SettingsFile
        {
            get
            {
                string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(folderPath, settings.repositoryOwner, settings.gameName, Settings.VersionFile);
            }
        }

        public MainWindow()
        {
            InitializeComponent();

            Load();
            Start();
        }

        private void Load()
        {
            string json = Properties.Resources.Settings;
            settings = JsonConvert.DeserializeObject<Settings>(json);
        }

        private Info GetLocalVersion()
        {
            if (!File.Exists(SettingsFile))
            {
                return null;
            }

            using (StreamReader streamReader = new StreamReader(SettingsFile))
            {
                string text = streamReader.ReadToEnd();
                return JsonConvert.DeserializeObject<Info>(text);
            }
        }

        private void UpdateLocalInfo(Release release)
        {
            DateTime utcDateTime = release.PublishedAt.GetValueOrDefault().UtcDateTime;
            Info info = new Info
            {
                downloadedAt = DateTime.Now.ToUniversalTime(),
                versionPublishedAt = utcDateTime
            };

            string contents = JsonConvert.SerializeObject(info, Formatting.None);
            string directoryName = Path.GetDirectoryName(SettingsFile);
            if (!Directory.Exists(directoryName))
            {
                Directory.CreateDirectory(directoryName);
            }
            File.WriteAllText(SettingsFile, contents);
        }

        private async Task<Release> GetLatestRelease()
        {
            try
            {
                DateTime now = DateTime.Now.ToUniversalTime();
                string gameName = settings.gameName.Replace(" ", "");
                ProductHeaderValue productHeader = new ProductHeaderValue(gameName + "Updater");
                GitHubClient github = new GitHubClient(productHeader);
                Repository repo = await github.Repository.Get(settings.repositoryOwner, settings.repositoryName);
                IReadOnlyList<Release> releases = await github.Repository.Release.GetAll(repo.Id);
                double milliseconds = double.MaxValue;
                Release latest = null;
                foreach (Release release in releases)
                {
                    if (release.TagName.StartsWith(settings.tagPrefix) && release.Assets.Count != 0)
                    {
                        DateTimeOffset value = now;
                        DateTimeOffset? publishedAt = release.PublishedAt;
                        TimeSpan? difference = value - publishedAt;
                        if (difference.HasValue && milliseconds > difference.Value.TotalMilliseconds)
                        {
                            milliseconds = difference.Value.TotalMilliseconds;
                            latest = release;
                        }
                    }
                }
                return latest;
            }
            catch (Exception exception)
            {
                text.Content = "error: " + exception.Message.ToLower();
                await Task.Delay(Settings.ErrorDelay);
                Close();
                return null;
            }
        }

        private bool IsOutOfDate(Info current, Release live)
        {
            return (live.PublishedAt - current.versionPublishedAt).Value.TotalMilliseconds > 0.0;
        }

        private async void Start()
        {
            text.Content = "checking local game";
            await Task.Delay(Settings.Delay);
            Info info = GetLocalVersion();
            if (info != null)
            {
                Release liveVersion = await GetLatestRelease();
                if (liveVersion == null)
                {
                    text.Content = "error: live version not found";
                    await Task.Delay(Settings.ErrorDelay);
                }
                else if (IsOutOfDate(info, liveVersion))
                {
                    text.Content = "out of to date";
                    await Task.Delay(Settings.Delay);
                    if (!await Download())
                    {
                        return;
                    }
                }
                else
                {
                    text.Content = "up to date";
                    await Task.Delay(Settings.Delay);
                }
            }
            else if (!await Download())
            {
                Close();
                return;
            }
            Launch();
        }

        private void ClearDirectory()
        {
            string fileName = Path.GetFileName(Assembly.GetEntryAssembly().Location);
            DirectoryInfo directoryInfo = new DirectoryInfo(Destination);
            FileInfo[] files = directoryInfo.GetFiles();
            foreach (FileInfo fileInfo in files)
            {
                string fileName2 = Path.GetFileName(fileInfo.FullName);
                if (!(fileName2 == fileName))
                {
                    try
                    {
                        fileInfo.Delete();
                    }
                    catch
                    {
                    }
                }
            }
            DirectoryInfo[] directories = directoryInfo.GetDirectories();
            foreach (DirectoryInfo directoryInfo2 in directories)
            {
                try
                {
                    directoryInfo2.Delete(recursive: true);
                }
                catch
                {
                }
            }
        }

        private async void Launch()
        {
            string path = Executable;
            if (File.Exists(path))
            {
                text.Content = "launching...";
                await Task.Delay(Settings.Delay);
                Process process = new Process
                {
                    StartInfo = new ProcessStartInfo(path)
                };
                process.Start();
                Close();
            }
            else
            {
                text.Content = "error: local game not found";
                await Task.Delay(Settings.ErrorDelay);
                Close();
            }
        }

        private async Task<bool> Download()
        {
            if (!Directory.Exists(Destination))
            {
                Directory.CreateDirectory(Destination);
            }
            text.Content = "downloading";
            Release release = await GetLatestRelease();
            if (release == null)
            {
                text.Content = "error: live version not found";
                await Task.Delay(Settings.ErrorDelay);
                return false;
            }

            ClearDirectory();
            text.Content = "live version: " + release.Name;
            await Task.Delay(Settings.Delay);
            text.Content = "downloading 0%";
            bool success = false;
            WebClient client = new WebClient();
            client.DownloadProgressChanged += ProgressChanged;
            foreach (ReleaseAsset asset in release.Assets)
            {
                string path = Destination + "/" + asset.Name;
                await client.DownloadFileTaskAsync(asset.BrowserDownloadUrl, path);
                success = true;
                ZipFile.ExtractToDirectory(path, Destination);
                File.Delete(path);
            }
            if (success)
            {
                await Task.Delay(Settings.Delay);
                text.Content = "done";
                UpdateLocalInfo(release);
                return true;
            }
            text.Content = "error: release not found";
            await Task.Delay(Settings.ErrorDelay);
            return false;
        }

        private void ProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            float num = e.ProgressPercentage / 100f;
            int num2 = (int)(num * 100f);
            text.Content = "downloading " + num2 + "%";
        }
    }
}
