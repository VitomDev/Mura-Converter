using System;
using System.IO;
using System.Media;
using System.Threading.Tasks;
using System.Windows;

namespace Mura_Converter
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (e.Args.Length == 2)
            {
                var formatArg = e.Args[0];
                var url = e.Args[1];

                if (!TryParseFormat(formatArg, out var format))
                {
                    MessageBox.Show("Formato no válido. Usa 'mp3' o 'mp4'.");
                    Current.Shutdown();
                    return;
                }

                var success = await DownloadFromArgsAsync(format, url);
                if (success)
                    MessageBox.Show("Descarga completada.");
                Current.Shutdown();
                return;
            }

            var mainWindow = new MainWindow();
            mainWindow.Show();
        }

        private static bool TryParseFormat(string value, out DownloadFormat format)
        {
            if (string.Equals(value, "mp3", StringComparison.OrdinalIgnoreCase))
            {
                format = DownloadFormat.Mp3;
                return true;
            }

            if (string.Equals(value, "mp4", StringComparison.OrdinalIgnoreCase))
            {
                format = DownloadFormat.Mp4;
                return true;
            }

            format = default;
            return false;
        }

        private static async Task<bool> DownloadFromArgsAsync(DownloadFormat format, string url)
        {
            try
            {
                if (!YouTubeDownloadService.IsValidYouTubeUrl(url))
                    throw new ArgumentException("La URL de YouTube no es válida.");

                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "MuraDownloads");
                await YouTubeDownloadService.DownloadAsync(url, format, folder);
                SystemSounds.Asterisk.Play();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al convertir: " + ex.Message);
                SystemSounds.Hand.Play();
                return false;
            }
        }
    }
}
