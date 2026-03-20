using System;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Mura_Converter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private DispatcherTimer? downloadAnimationTimer;
        private int dotCount = 0;
        private string downloadBaseText = "";

        private CancellationTokenSource? qualityLookupCts;
        private string? lastLoadedQualityUrl;

        public MainWindow()
        {
            InitializeComponent();
            ResetQualitySelector();

            try
            {
                var clipboardText = Clipboard.GetText();

                if (!string.IsNullOrWhiteSpace(clipboardText) &&
                    (clipboardText.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                     clipboardText.Contains("youtu.be", StringComparison.OrdinalIgnoreCase)))
                {
                    UrlBox.Text = clipboardText.Trim();
                    StatusText.Text = "URL pasted from clipboard.";
                }
            }
            catch
            {
                StatusText.Text = "The clipboard could not be accessed.";
            }
        }

        private async void UrlBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var url = UrlBox.Text.Trim();

            qualityLookupCts?.Cancel();
            qualityLookupCts?.Dispose();
            qualityLookupCts = null;

            if (!YouTubeDownloadService.IsValidYouTubeUrl(url))
            {
                lastLoadedQualityUrl = null;
                ResetQualitySelector();
                return;
            }

            if (string.Equals(lastLoadedQualityUrl, url, StringComparison.OrdinalIgnoreCase))
                return;

            var cts = new CancellationTokenSource();
            qualityLookupCts = cts;

            try
            {
                SetLoadingQualitySelector();
                await Task.Delay(500, cts.Token);
                await LoadQualityOptionsAsync(url, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // A newer URL check replaced this one.
            }
            catch (Exception ex)
            {
                if (cts.IsCancellationRequested)
                    return;

                ResetQualitySelector();
                StatusText.Text = $"Error checking qualities: {ex.Message}";
            }
            finally
            {
                if (ReferenceEquals(qualityLookupCts, cts))
                {
                    qualityLookupCts.Dispose();
                    qualityLookupCts = null;
                }
            }
        }

        private async Task LoadQualityOptionsAsync(string url, CancellationToken cancellationToken)
        {
            var options = await YouTubeDownloadService.GetMp4QualityOptionsAsync(url, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                return;

            lastLoadedQualityUrl = url;
            QualitySelector.ItemsSource = options;
            QualitySelector.SelectedIndex = 0;
            QualitySelector.IsEnabled = options.Count > 1;
        }

        private void ResetQualitySelector()
        {
            QualitySelector.ItemsSource = new[] { YouTubeDownloadService.AutoQualityOption };
            QualitySelector.SelectedIndex = 0;
            QualitySelector.IsEnabled = false;
        }

        private void SetLoadingQualitySelector()
        {
            QualitySelector.ItemsSource = new[] { YouTubeDownloadService.LoadingQualityOption };
            QualitySelector.SelectedIndex = 0;
            QualitySelector.IsEnabled = false;
        }

        private async void ConvertToMp3_Click(object sender, RoutedEventArgs e)
        {
            var url = UrlBox.Text.Trim();
            if (!YouTubeDownloadService.IsValidYouTubeUrl(url))
            {
                StatusText.Text = "Please enter a valid YouTube URL.";
                return;
            }

            try
            {
                SetButtonsEnabled(false);
                StartDownloadAnimation("Downloading MP3");

                var result = await YouTubeDownloadService.DownloadAsync(url, DownloadFormat.Mp3, GetDownloadsFolder());
                StopDownloadAnimation($"Download complete: {Path.GetFileName(result.FilePath)}");
                SystemSounds.Asterisk.Play();
            }
            catch (Exception ex)
            {
                StopDownloadAnimation($"Error: {ex.Message}");
                SystemSounds.Hand.Play();
            }
            finally
            {
                SetButtonsEnabled(true);
            }
        }

        private async void ConvertToMp4_Click(object sender, RoutedEventArgs e)
        {
            var url = UrlBox.Text.Trim();
            if (!YouTubeDownloadService.IsValidYouTubeUrl(url))
            {
                StatusText.Text = "Please enter a valid YouTube URL.";
                return;
            }

            var selectedQuality = QualitySelector.SelectedItem as VideoQualityOption;
            var qualityKey = selectedQuality?.Key;

            try
            {
                SetButtonsEnabled(false);
                StartDownloadAnimation("Downloading MP4");

                var result = await YouTubeDownloadService.DownloadAsync(
                    url,
                    DownloadFormat.Mp4,
                    GetDownloadsFolder(),
                    preferredMp4QualityKey: qualityKey);

                StopDownloadAnimation($"Download complete: {Path.GetFileName(result.FilePath)}");
                SystemSounds.Asterisk.Play();
            }
            catch (Exception ex)
            {
                StopDownloadAnimation($"Error: {ex.Message}");
                SystemSounds.Hand.Play();
            }
            finally
            {
                SetButtonsEnabled(true);
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var folder = GetDownloadsFolder();
            Directory.CreateDirectory(folder);
            Process.Start("explorer.exe", folder);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseWindow_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private static string GetDownloadsFolder() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "MuraDownloads");

        private void SetButtonsEnabled(bool isEnabled)
        {
            ConvertToMp3.IsEnabled = isEnabled;
            ConvertToMp4.IsEnabled = isEnabled;
            OpenFolderButton.IsEnabled = isEnabled;
            UrlBox.IsEnabled = isEnabled;
            QualitySelector.IsEnabled = isEnabled && QualitySelector.Items.Count > 1;
        }

        private void StartDownloadAnimation(string baseText)
        {
            downloadBaseText = baseText;
            dotCount = 0;

            downloadAnimationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };

            downloadAnimationTimer.Tick += (_, _) =>
            {
                dotCount = (dotCount + 1) % 4;
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
    }
}
