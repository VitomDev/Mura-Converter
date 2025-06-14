using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using YoutubeExplode;
using YoutubeExplode.Converter;

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
                string format = e.Args[0];
                string url = e.Args[1];

                await DownloadFromArgsAsync(format, url);
                Current.Shutdown();
            }
            else
            {
                var mainWindow = new MainWindow();
                mainWindow.Show();
            }
        }

        private async Task DownloadFromArgsAsync(string format, string url)
        {
            try
            {
                var youtube = new YoutubeClient();
                string fileName = $"yt_{DateTime.Now:yyyyMMddHHmmss}";

                if (format == "mp3")
                {
                    await youtube.Videos.DownloadAsync(url, $"{fileName}.mp3");
                }
                else if (format == "mp4")
                {
                    await youtube.Videos.DownloadAsync(url, $"{fileName}.mp4");
                }
                else
                {
                    Console.WriteLine("Formato no soportado: usa 'mp3' o 'mp4'");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error al convertir: " + ex.Message);
            }
        }
    }
}