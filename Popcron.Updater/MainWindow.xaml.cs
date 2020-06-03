using Octokit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;

namespace Popcron.Updater
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Path to the folder that contains the game files.
        /// </summary>
        private string Destination
        {
            get
            {
                string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(folderPath, Settings.RepositoryOwner, Settings.GameName);
            }
        }

        /// <summary>
        /// Path to the executable.
        /// </summary>
        private string Executable
        {
            get
            {
                string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(folderPath, Settings.RepositoryOwner, Settings.GameName, Settings.ExecName);
            }
        }

        /// <summary>
        /// Path to the local version file.
        /// </summary>
        private string VersionFile
        {
            get
            {
                string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(folderPath, Settings.RepositoryOwner, Settings.GameName, Settings.VersionFile);
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            Start();
        }

        private Info GetLocalVersion()
        {
            if (File.Exists(VersionFile))
            {
                string[] lines = File.ReadAllLines(VersionFile);
                if (lines.Length == 2)
                {
                    if (DateTime.TryParse(lines[0], out DateTime downloadedAt))
                    {
                        if (DateTime.TryParse(lines[1], out DateTime versionPublishedAt))
                        {
                            return new Info()
                            {
                                downloadedAt = downloadedAt,
                                versionPublishedAt = versionPublishedAt
                            };
                        }
                    }
                }
            }

            return null;
        }

        private void UpdateLocalInfo(Release release)
        {
            DateTime downloadedAt = DateTime.Now.ToUniversalTime();
            DateTime versionPublishedAt = release.PublishedAt.GetValueOrDefault().UtcDateTime;

            string contents = downloadedAt.ToString() + "\n" + versionPublishedAt.ToString();
            string directoryName = Path.GetDirectoryName(VersionFile);
            if (!Directory.Exists(directoryName))
            {
                Directory.CreateDirectory(directoryName);
            }

            File.WriteAllText(VersionFile, contents);
        }

        private async Task<Release> GetLatestRelease()
        {
            try
            {
                DateTime now = DateTime.Now.ToUniversalTime();
                string gameName = Settings.GameName.Replace(" ", "");
                ProductHeaderValue productHeader = new ProductHeaderValue($"{gameName}Updater");
                GitHubClient github = new GitHubClient(productHeader);
                Repository repo = await github.Repository.Get(Settings.RepositoryOwner, Settings.RepositoryName);
                IReadOnlyList<Release> releases = await github.Repository.Release.GetAll(repo.Id);
                double milliseconds = double.MaxValue;
                Release latest = null;
                foreach (Release release in releases)
                {
                    if (release.TagName.StartsWith(Settings.TagPrefix) && release.Assets.Count != 0)
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
                text.Content = "getting latest release";
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

            text.Content = "live version: " + release.Name;
            await Task.Delay(Settings.Delay);
            text.Content = "downloading 0%";
            bool success = false;
            WebClient client = new WebClient();
            client.DownloadProgressChanged += ProgressChanged;
            foreach (ReleaseAsset asset in release.Assets)
            {
                //delete zip if already exists
                string pathToZip = Destination + "/" + asset.Name;
                if (File.Exists(pathToZip))
                {
                    File.Delete(pathToZip);
                }

                //download
                await client.DownloadFileTaskAsync(asset.BrowserDownloadUrl, pathToZip);
                success = true;

                //extract to temporary location
                string tempDestination = Destination + "_Temp";
                if (Directory.Exists(tempDestination))
                {
                    Directory.Delete(tempDestination, true);
                }

                Directory.CreateDirectory(tempDestination);
                ZipFile.ExtractToDirectory(pathToZip, tempDestination);

                //delete the zip
                File.Delete(pathToZip);

                //move stuff from temp to destination
                Move();
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

        private void ClearUnityFolder(string folderName)
        {
            string folderDirectory = Path.Combine(Directory.GetParent(Executable).FullName, folderName);
            if (Directory.Exists(folderDirectory))
            {
                Directory.Delete(folderDirectory, true);
            }
        }

        private void Move()
        {
            string tempDestination = Destination + "_Temp";

            //clear the _Data directory
            string dataFolderName = Path.GetFileNameWithoutExtension(Settings.ExecName) + "_Data";
            ClearUnityFolder(dataFolderName);
            ClearUnityFolder("MonoBleedingEdge");

            //move the files from the temp folder, to the correct folder
            string[] files = Directory.GetFiles(tempDestination, "*.*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string properPath = files[i].Replace("_Temp", "");

                //make sure dir exists
                string directory = Path.GetDirectoryName(properPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                //delete the old file if it exists
                if (File.Exists(properPath))
                {
                    File.Delete(properPath);
                }

                //then move the new one into the proper path
                File.Move(files[i], properPath);
            }

            //delete temp dir
            Directory.Delete(tempDestination, true);
        }

        private void ProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            float percentageFloat = e.ProgressPercentage / 100f;
            int percentage = (int)(percentageFloat * 100f);
            text.Content = $"downloading {percentage}%";
        }
    }
}
