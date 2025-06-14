using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using YoutubeExplode;
using YoutubeExplode.Converter;
using YoutubeExplode.Videos;
using System.Media;
using System.Windows.Threading;


namespace Mura_Converter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        private DispatcherTimer downloadAnimationTimer;
        private int dotCount = 0;
        private string downloadBaseText = "";

        public MainWindow()
        {
            InitializeComponent();

            try
            {
                string clipboardText = System.Windows.Clipboard.GetText();

                if (!string.IsNullOrWhiteSpace(clipboardText) &&
                    clipboardText.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                    clipboardText.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
                {
                    UrlBox.Text = clipboardText.Trim();
                    StatusText.Text = "📋 URL automatically pasted from clipboard.";
                }
            }
            catch
            {
                StatusText.Text = "⚠️ The clipboard could not be accessed.";
            }

            
        }

        private async void ConvertToMp3_Click(object sender, RoutedEventArgs e)
        {
            string url = UrlBox.Text.Trim();
            if (!IsAValidYouTubeUrl(url))
            {
                StatusText.Text = "❌ Please enter a valid YouTube URL.";
                return;
            }

            try
            {
                ConvertToMp3.IsEnabled = false;
                ConvertToMp4.IsEnabled = false;
                OpenFolderButton.IsEnabled = false;

                //StatusText.Text = "⏳ Downloading MP3...";
                StartDownloadAnimation("⏳ Downloading MP3");

                var youtube = new YoutubeClient();
                string folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "MuraDownloads");
                System.IO.Directory.CreateDirectory(folder);
                var video = await youtube.Videos.GetAsync(url);

                string safeTitle = string.Concat(video.Title
                    .Where(c => !Path.GetInvalidFileNameChars().Contains(c)));

                string baseName = $"{safeTitle} ({DateTime.Now:yyyy-MM-dd})";
                string filePath = GetUniqueFilePath(folder, baseName, ".mp3");


                string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ffmpeg.exe");

                await youtube.Videos.DownloadAsync(url, filePath, options => options.SetFFmpegPath(ffmpegPath));

                //StatusText.Text = $"✅ Download complete! Saved as:\n{System.IO.Path.GetFileName(filePath)}";
                StopDownloadAnimation($"✅ Download complete! Saved as:\n{Path.GetFileName(filePath)}");
                SystemSounds.Asterisk.Play();
                //ShowNotification("Mura Converter", $"{video.Title} has been downloaded as MP3.");
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Error: {ex.Message}";
                SystemSounds.Hand.Play();
                //ShowNotification("Mura Converter", "The audio could not be downloaded.");
            }
            finally
            {
                ConvertToMp3.IsEnabled = true;
                ConvertToMp4.IsEnabled = true;
                OpenFolderButton.IsEnabled = true;
            }
        }

        private async void ConvertToMp4_Click(object sender, RoutedEventArgs e)
        {
            string url = UrlBox.Text.Trim();
            if (!IsAValidYouTubeUrl(url))
            {
                StatusText.Text = "❌ Please enter a valid YouTube URL.";
                return;
            }

            try
            {
                ConvertToMp3.IsEnabled = false;
                ConvertToMp4.IsEnabled = false;
                OpenFolderButton.IsEnabled = false;

                //StatusText.Text = "⏳ Downloading MP4...";
                StartDownloadAnimation("⏳ Downloading MP4");

                var youtube = new YoutubeClient();
                string folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "MuraDownloads");
                System.IO.Directory.CreateDirectory(folder);
                var video = await youtube.Videos.GetAsync(url);

                string safeTitle = string.Concat(video.Title
                    .Where(c => !Path.GetInvalidFileNameChars().Contains(c)));

                string baseName = $"{safeTitle} ({DateTime.Now:yyyy-MM-dd})";
                string filePath = GetUniqueFilePath(folder, baseName, ".mp4");


                string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ffmpeg.exe");

                await youtube.Videos.DownloadAsync(url, filePath, options => options.SetFFmpegPath(ffmpegPath));

                StatusText.Text = $"✅ Download complete! Saved as:\n{System.IO.Path.GetFileName(filePath)}";
                StopDownloadAnimation($"✅ Download complete! Saved as:\n{Path.GetFileName(filePath)}");
                SystemSounds.Asterisk.Play();
                //ShowNotification("Mura Converter", $"{video.Title} has been downloaded as MP4.");
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Error: {ex.Message}";
                SystemSounds.Hand.Play();
                //ShowNotification("Mura Converter", "The video could not be downloaded.");
            }
            finally
            {
                ConvertToMp3.IsEnabled = true;
                ConvertToMp4.IsEnabled = true;
                OpenFolderButton.IsEnabled = true;
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            string folder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                "MuraDownloads"
            );

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            Process.Start("explorer.exe", folder);
        }

        private bool IsAValidYouTubeUrl(string url)
        {
            if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
                return false;

            return url.Contains("youtube.com") || url.Contains("youtu.be");
        }

        private void StartDownloadAnimation(string baseText)
        {
            downloadBaseText = baseText;
            dotCount = 0;

            downloadAnimationTimer = new DispatcherTimer();
            downloadAnimationTimer.Interval = TimeSpan.FromMilliseconds(300);
            downloadAnimationTimer.Tick += (s, e) =>
            {
                dotCount = (dotCount + 1) % 4; // 0,1,2,3
                StatusText.Text = downloadBaseText + new string('.', dotCount);
            };
            downloadAnimationTimer.Start();
        }

        private void StopDownloadAnimation(string finalMessage)
        {
            if (downloadAnimationTimer != null)
            {
                downloadAnimationTimer.Stop();
                downloadAnimationTimer = null;
            }

            StatusText.Text = finalMessage;
        }

        private string GetUniqueFilePath(string folder, string baseName, string extension)
        {
            string filePath = Path.Combine(folder, $"{baseName}{extension}");
            int counter = 2;

            while (File.Exists(filePath))
            {
                filePath = Path.Combine(folder, $"{baseName} ({counter}){extension}");
                counter++;
            }

            return filePath;
        }


    }
}