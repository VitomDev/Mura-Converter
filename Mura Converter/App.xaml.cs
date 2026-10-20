using System.Windows;

namespace Mura_Converter;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Optional: "MuraConverter.exe mp3 <url>" downloads straight away; "MuraConverter.exe <url>" just pre-fills it.
        string? url = null;
        DownloadFormat? format = null;
        foreach (var arg in e.Args)
        {
            if (arg.Equals("mp3", StringComparison.OrdinalIgnoreCase)) format = DownloadFormat.Mp3;
            else if (arg.Equals("mp4", StringComparison.OrdinalIgnoreCase)) format = DownloadFormat.Mp4;
            else if (YtDlpService.LooksLikeUrl(arg)) url = arg;
        }

        new MainWindow(url, format).Show();
    }
}
