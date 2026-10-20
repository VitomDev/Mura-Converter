using System.Diagnostics;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace Mura_Converter;

internal sealed record QualityOption(int Value, string Label)
{
    public override string ToString() => Label;
}

public partial class MainWindow : Window
{
    private static readonly QualityOption[] VideoQualities =
    {
        new(0, "Máxima disponible"),
        new(2160, "4K (2160p)"),
        new(1440, "1440p"),
        new(1080, "1080p"),
        new(720, "720p"),
        new(480, "480p"),
        new(360, "360p"),
    };

    private static readonly QualityOption[] AudioQualities =
    {
        new(320, "320 kbps"),
        new(256, "256 kbps"),
        new(192, "192 kbps"),
        new(128, "128 kbps"),
    };

    private readonly AppSettings settings = AppSettings.Load();
    private CancellationTokenSource? downloadCts;
    private Task? updateTask;
    private string? lastClipboardUrl;
    private string? lastFilePath;

    internal MainWindow(string? initialUrl = null, DownloadFormat? initialFormat = null)
    {
        InitializeComponent();

        if (initialFormat.HasValue)
            settings.Format = initialFormat.Value;

        Mp4Option.IsChecked = settings.Format == DownloadFormat.Mp4;
        Mp3Option.IsChecked = settings.Format == DownloadFormat.Mp3;
        RefreshQualityOptions();
        RefreshFolderLabel();

        if (initialUrl != null)
        {
            UrlBox.Text = initialUrl;
            lastClipboardUrl = initialUrl;
        }

        Loaded += (_, _) =>
        {
            UrlBox.Focus();
            if (initialUrl != null && initialFormat.HasValue)
                _ = StartDownloadAsync();
        };

        // yt-dlp must track YouTube's changes; refresh it at most once a day, silently in the background.
        if (DateTime.Now - settings.LastUpdateCheck > TimeSpan.FromDays(1))
        {
            updateTask = Task.Run(async () =>
            {
                if (await YtDlpService.TryUpdateAsync())
                    Dispatcher.Invoke(() => { settings.LastUpdateCheck = DateTime.Now; settings.Save(); });
            });
        }
    }

    private DownloadFormat SelectedFormat => Mp3Option.IsChecked == true ? DownloadFormat.Mp3 : DownloadFormat.Mp4;

    private void Window_Activated(object? sender, EventArgs e) => TryPasteFromClipboard(force: false);

    private void TryPasteFromClipboard(bool force)
    {
        if (downloadCts != null)
            return;

        string text;
        try
        {
            text = Clipboard.GetText().Trim();
        }
        catch
        {
            return;
        }

        // Auto-paste only fresh YouTube links, so we never overwrite something the user typed.
        if (!force && (!YtDlpService.IsYouTubeUrl(text) || text == lastClipboardUrl))
            return;
        if (force && !YtDlpService.LooksLikeUrl(text))
        {
            SetStatus("El portapapeles no contiene un enlace.", null);
            return;
        }

        lastClipboardUrl = text;
        UrlBox.Text = text;
        UrlBox.CaretIndex = text.Length;
        SetStatus("Enlace pegado. Pulsa Descargar o Enter.", null);
    }

    private void Paste_Click(object sender, RoutedEventArgs e) => TryPasteFromClipboard(force: true);

    private void Format_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || QualitySelector == null)
            return;
        settings.Format = SelectedFormat;
        RefreshQualityOptions();
        settings.Save();
    }

    private void RefreshQualityOptions()
    {
        var isMp3 = SelectedFormat == DownloadFormat.Mp3;
        var options = isMp3 ? AudioQualities : VideoQualities;
        var current = isMp3 ? settings.AudioBitrate : settings.VideoHeight;

        QualitySelector.ItemsSource = options;
        QualitySelector.SelectedItem = options.FirstOrDefault(o => o.Value == current) ?? options[0];
        QualityLabel.Text = isMp3 ? "Calidad de audio" : "Calidad de vídeo (máx.)";
    }

    private void Quality_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (QualitySelector.SelectedItem is not QualityOption option)
            return;

        if (SelectedFormat == DownloadFormat.Mp3)
            settings.AudioBitrate = option.Value;
        else
            settings.VideoHeight = option.Value;
        settings.Save();
    }

    private void UrlBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            _ = StartDownloadAsync();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.Text) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.Text) is string text && YtDlpService.LooksLikeUrl(text))
            UrlBox.Text = text.Trim();
    }

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        if (downloadCts != null)
            downloadCts.Cancel();
        else
            _ = StartDownloadAsync();
    }

    private async Task StartDownloadAsync()
    {
        if (downloadCts != null)
            return;

        var url = UrlBox.Text.Trim();
        if (!YtDlpService.LooksLikeUrl(url))
        {
            SetStatus("Pega un enlace válido.", null);
            SystemSounds.Hand.Play();
            return;
        }

        var cts = new CancellationTokenSource();
        downloadCts = cts;
        SetBusy(true);
        SetStatus("Preparando…", null);
        Progress.IsIndeterminate = true;
        lastFilePath = null;
        ShowFileButton.Visibility = Visibility.Collapsed;

        var progress = new Progress<DownloadProgress>(p =>
        {
            if (cts.IsCancellationRequested)
                return;
            Progress.IsIndeterminate = p.Percent == null;
            if (p.Percent.HasValue)
                Progress.Value = p.Percent.Value;
            var head = p.Percent.HasValue ? $"{p.Stage}  {p.Percent.Value:0}%" : p.Stage + "…";
            SetStatus(head, p.Detail);
        });

        try
        {
            // Don't race the self-update replacing yt-dlp.exe.
            if (updateTask is { IsCompleted: false })
            {
                SetStatus("Actualizando motor de descarga…", null);
                await updateTask;
            }

            var quality = (QualitySelector.SelectedItem as QualityOption)?.Value ?? 0;
            var format = SelectedFormat;
            var path = await YtDlpService.DownloadAsync(
                url,
                format,
                videoHeight: format == DownloadFormat.Mp4 ? quality : 0,
                audioBitrate: format == DownloadFormat.Mp3 ? quality : 320,
                settings.OutputFolder,
                progress,
                cts.Token);

            lastFilePath = path;
            Progress.IsIndeterminate = false;
            Progress.Value = 100;
            SetStatus("✓ Descarga completada", Path.GetFileName(path));
            ShowFileButton.Visibility = Visibility.Visible;
            SystemSounds.Asterisk.Play();
        }
        catch (OperationCanceledException)
        {
            Progress.IsIndeterminate = false;
            Progress.Value = 0;
            SetStatus("Descarga cancelada.", null);
        }
        catch (Exception ex)
        {
            Progress.IsIndeterminate = false;
            Progress.Value = 0;
            SetStatus("Error", ex.Message);
            SystemSounds.Hand.Play();
        }
        finally
        {
            downloadCts = null;
            cts.Dispose();
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        DownloadButton.Content = busy ? "Cancelar" : "Descargar";
        UrlBox.IsEnabled = !busy;
        PasteButton.IsEnabled = !busy;
        Mp3Option.IsEnabled = !busy;
        Mp4Option.IsEnabled = !busy;
        QualitySelector.IsEnabled = !busy;
        ChangeFolderButton.IsEnabled = !busy;
    }

    private void SetStatus(string head, string? detail)
    {
        StatusText.Text = head;
        DetailText.Text = detail ?? "";
        DetailText.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RefreshFolderLabel()
    {
        FolderText.Text = settings.OutputFolder;
        FolderText.ToolTip = settings.OutputFolder;
    }

    private void ChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Carpeta de descargas",
            InitialDirectory = Directory.Exists(settings.OutputFolder) ? settings.OutputFolder : null,
        };

        if (dialog.ShowDialog(this) == true)
        {
            settings.OutputFolder = dialog.FolderName;
            settings.Save();
            RefreshFolderLabel();
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(settings.OutputFolder);
        Process.Start("explorer.exe", $"\"{settings.OutputFolder}\"");
    }

    private void ShowFile_Click(object sender, RoutedEventArgs e)
    {
        if (lastFilePath != null && File.Exists(lastFilePath))
            Process.Start("explorer.exe", $"/select,\"{lastFilePath}\"");
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
    {
        downloadCts?.Cancel();
        Close();
    }
}
