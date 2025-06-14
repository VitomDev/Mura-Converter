using System;
using System.Windows;
using YoutubeExplode;
using YoutubeExplode.Converter;

namespace Mura_Converter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void ConvertToMp3_Click(object sender, RoutedEventArgs e)
        {
            string url = UrlBox.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                StatusText.Text = "Please enter a valid YouTube URL.";
                return;
            }

            try
            {
                StatusText.Text = "Downloading MP3...";
                var youtube = new YoutubeClient();
                string folder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                    "MuraDownloads"
                );
                System.IO.Directory.CreateDirectory(folder);

                string filePath = System.IO.Path.Combine(folder, $"yt_{DateTime.Now:yyyyMMddHHmmss}.mp3");
                await youtube.Videos.DownloadAsync(url, filePath);
                StatusText.Text = $"Done! Saved as {filePath}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
            }
        }

        private async void ConvertToMp4_Click(object sender, RoutedEventArgs e)
        {
            string url = UrlBox.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                StatusText.Text = "Please enter a valid YouTube URL.";
                return;
            }

            try
            {
                StatusText.Text = "Downloading MP4...";
                var youtube = new YoutubeClient();
                string folder = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                    "MuraDownloads"
                );
                System.IO.Directory.CreateDirectory(folder);

                string filePath = System.IO.Path.Combine(folder, $"yt_{DateTime.Now:yyyyMMddHHmmss}.mp4");
                await youtube.Videos.DownloadAsync(url, filePath);
                StatusText.Text = $"Done! Saved as {filePath}";

            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
            }
        }
    }
}