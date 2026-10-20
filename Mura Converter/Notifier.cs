using System.Drawing;
using Forms = System.Windows.Forms;

namespace Mura_Converter;

/// <summary>Windows notifications (shown as toasts on Windows 10/11) via a tray icon that only appears while needed.</summary>
internal sealed class Notifier : IDisposable
{
    private readonly Forms.NotifyIcon icon;

    public Notifier(Action onClick)
    {
        icon = new Forms.NotifyIcon
        {
            Text = "Mura Converter",
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application,
        };
        icon.BalloonTipClicked += (_, _) => onClick();
        icon.Click += (_, _) => onClick();
    }

    public void Show(string title, string text, bool isError = false)
    {
        icon.Visible = true;
        icon.ShowBalloonTip(5000, title, string.IsNullOrWhiteSpace(text) ? " " : text,
            isError ? Forms.ToolTipIcon.Error : Forms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
    }
}
