using System.Windows;

namespace Mura_Converter;

/// <summary>Command line passed to Mura (also used by the Explorer context menu).</summary>
internal sealed record LaunchArgs(string? Url, MediaKind? Kind, bool FromClipboard, string? Folder)
{
    // Accepted: [mp3|mp4] [url] [--clipboard] [--dir <folder>]
    public static LaunchArgs Parse(IReadOnlyList<string> args)
    {
        string? url = null, folder = null;
        MediaKind? kind = null;
        var clipboard = false;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i].Trim();
            if (arg.Equals("mp3", StringComparison.OrdinalIgnoreCase)) kind = MediaKind.Audio;
            else if (arg.Equals("mp4", StringComparison.OrdinalIgnoreCase)) kind = MediaKind.Video;
            else if (arg.Equals("--clipboard", StringComparison.OrdinalIgnoreCase)) clipboard = true;
            else if (arg.Equals("--dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count) folder = args[++i].Trim().TrimEnd('"');
            else if (YtDlpService.LooksLikeUrl(arg)) url = arg;
        }

        return new LaunchArgs(url, kind, clipboard, folder);
    }

    public bool IsQuickDownload => FromClipboard || (Url != null && Kind != null);
}

public partial class App : Application
{
    private SingleInstance? instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        instance = SingleInstance.TryAcquire(e.Args);
        if (instance == null)
        {
            Shutdown();
            return;
        }

        var launch = LaunchArgs.Parse(e.Args);
        var window = new MainWindow(launch);
        instance.Listen(args => Dispatcher.BeginInvoke(() => window.HandleLaunch(LaunchArgs.Parse(args), fromOtherInstance: true)));
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        base.OnExit(e);
    }
}
