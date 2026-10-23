using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Mura_Converter;

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

    private static readonly QualityOption[] LosslessQuality = { new(0, "Sin pérdida") };

    private static readonly AudioFormat[] AudioFormats = Enum.GetValues<AudioFormat>();

    private readonly AppSettings settings = AppSettings.Load();
    private readonly ObservableCollection<QueueItem> queue = new();
    // Created on the first notification: it is the only user of WinForms,
    // which would otherwise be loaded (~17 MB) for every session.
    private Notifier? notifierInstance;
    private Notifier notifier => notifierInstance ??= new Notifier(RestoreWindow);
    private Task? updateTask;
    private bool processing;
    private bool quietMode;
    private bool loadingUi = true;
    private string? lastClipboardUrl;
    private CancellationTokenSource? previewCts;
    private (string Url, MediaInfo Info)? preview;

    internal MainWindow(LaunchArgs launch)
    {
        InitializeComponent();

        QueueList.ItemsSource = queue;
        AudioFormatSelector.ItemsSource = AudioFormats.Select(f => f.ToString().ToUpperInvariant()).ToArray();
        AudioFormatSelector.SelectedIndex = Array.IndexOf(AudioFormats, settings.AudioFormat);
        VideoOption.IsChecked = settings.Kind == MediaKind.Video;
        AudioOption.IsChecked = settings.Kind == MediaKind.Audio;
        loadingUi = false;
        RefreshOptions();
        RefreshFolderLabel();
        RefreshQueuePanel();

        // Context-menu / command-line downloads run minimized and close by themselves when done.
        if (launch.IsQuickDownload)
        {
            quietMode = true;
            ShowActivated = false;
            WindowState = WindowState.Minimized;
        }

        Loaded += (_, _) =>
        {
            if (!quietMode)
                UrlBox.Focus();
            HandleLaunch(launch, fromOtherInstance: false);
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

    private MediaKind SelectedKind => AudioOption.IsChecked == true ? MediaKind.Audio : MediaKind.Video;

    private AudioFormat SelectedAudioFormat =>
        AudioFormatSelector.SelectedIndex >= 0 ? AudioFormats[AudioFormatSelector.SelectedIndex] : AudioFormat.Mp3;

    // ---------- Launch arguments (command line, Explorer context menu, second instance) ----------

    internal void HandleLaunch(LaunchArgs launch, bool fromOtherInstance)
    {
        var url = launch.Url ?? (launch.FromClipboard ? ReadClipboardUrl(anyUrl: true) : null);

        if (launch.IsQuickDownload)
        {
            if (url == null)
            {
                notifier.Show("Mura Converter", "No hay ningún enlace copiado en el portapapeles.", isError: true);
                if (quietMode && !processing)
                    _ = CloseSoonAsync();
                return;
            }

            lastClipboardUrl = url;
            Enqueue(BuildRequest(url, launch.Folder ?? settings.OutputFolder, wholePlaylist: false, null, null, launch.Kind), url);
            if (fromOtherInstance && !quietMode)
                notifier.Show("Añadido a la cola", url);
            else if (quietMode)
                notifier.Show("Descargando con Mura…", url);
            return;
        }

        if (url != null)
        {
            lastClipboardUrl = url;
            UrlBox.Text = url;
        }

        if (fromOtherInstance)
            RestoreWindow();
    }

    private void RestoreWindow()
    {
        quietMode = false;
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Normal)
            quietMode = false;
        else if (WindowState == WindowState.Minimized && !processing)
            TrimMemory();
    }

    // ---------- Clipboard, drag & drop ----------

    private void Window_Activated(object? sender, EventArgs e)
    {
        var text = ReadClipboardUrl(anyUrl: false);
        if (text == null || text == lastClipboardUrl || text == UrlBox.Text.Trim())
            return;

        lastClipboardUrl = text;
        UrlBox.Text = text;
        UrlBox.CaretIndex = text.Length;
    }

    private static string? ReadClipboardUrl(bool anyUrl)
    {
        try
        {
            var text = Clipboard.GetText().Trim();
            return anyUrl ? (YtDlpService.LooksLikeUrl(text) ? text : null)
                          : (YtDlpService.IsKnownSiteUrl(text) ? text : null);
        }
        catch
        {
            return null;
        }
    }

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        var text = ReadClipboardUrl(anyUrl: true);
        if (text == null)
        {
            SetHint("El portapapeles no contiene un enlace.");
            return;
        }

        lastClipboardUrl = text;
        UrlBox.Text = text;
        UrlBox.CaretIndex = text.Length;
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

    private void UrlBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            AddCurrentToQueue();
    }

    // ---------- Preview ----------

    private async void UrlBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        previewCts?.Cancel();
        previewCts = null;
        SetHint(null);

        var url = UrlBox.Text.Trim();
        if (!YtDlpService.LooksLikeUrl(url))
        {
            preview = null;
            PreviewCard.Visibility = Visibility.Collapsed;
            return;
        }

        if (preview?.Url == url)
            return;

        var cts = new CancellationTokenSource();
        previewCts = cts;
        preview = null;
        PreviewCard.Visibility = Visibility.Visible;
        PreviewTitle.Text = "Cargando información…";
        PreviewMeta.Text = "";
        PreviewImage.Source = null;
        PlaylistOption.Visibility = Visibility.Collapsed;
        PlaylistOption.IsChecked = false;

        try
        {
            await Task.Delay(350, cts.Token);
            var info = await YtDlpService.GetInfoAsync(url, cts.Token);
            if (cts.IsCancellationRequested)
                return;

            preview = (url, info);
            PreviewTitle.Text = info.Title;
            PreviewMeta.Text = string.Join("  ·  ", new[]
            {
                info.Uploader,
                info.Duration.HasValue ? YtDlpService.FormatDuration(info.Duration.Value) : null,
            }.Where(s => !string.IsNullOrEmpty(s)));

            if (info.IsPlaylist)
            {
                PlaylistOption.Content = info.PlaylistCount.HasValue
                    ? $"Descargar la lista completa ({info.PlaylistCount} elementos)"
                    : "Descargar la lista completa";
                PlaylistOption.Visibility = Visibility.Visible;
                // A bare playlist link has no single video to fall back to.
                PlaylistOption.IsChecked = info.Duration == null;
            }

            if (info.ThumbnailUrl != null)
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(info.ThumbnailUrl);
                image.DecodePixelWidth = 192;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                PreviewImage.Source = image;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (cts.IsCancellationRequested)
                return;
            PreviewTitle.Text = "No se pudo leer el enlace";
            PreviewMeta.Text = FirstLine(ex.Message);
        }
    }

    // ---------- Options ----------

    private void Kind_Changed(object sender, RoutedEventArgs e)
    {
        if (loadingUi)
            return;
        settings.Kind = SelectedKind;
        settings.Save();
        RefreshOptions();
    }

    private void AudioFormat_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loadingUi)
            return;
        settings.AudioFormat = SelectedAudioFormat;
        settings.Save();
        RefreshOptions();
    }

    private void RefreshOptions()
    {
        var isAudio = SelectedKind == MediaKind.Audio;
        AudioFormatPanel.Visibility = isAudio ? Visibility.Visible : Visibility.Collapsed;

        QualityOption[] options;
        int current;
        if (!isAudio)
        {
            options = VideoQualities;
            current = settings.VideoHeight;
            QualityLabel.Text = "Calidad (máx.)";
        }
        else if (SelectedAudioFormat == AudioFormat.Wav)
        {
            options = LosslessQuality;
            current = 0;
            QualityLabel.Text = "Calidad";
        }
        else
        {
            options = AudioQualities;
            current = settings.AudioBitrate;
            QualityLabel.Text = "Calidad";
        }

        loadingUi = true;
        QualitySelector.ItemsSource = options;
        QualitySelector.SelectedItem = options.FirstOrDefault(o => o.Value == current) ?? options[0];
        QualitySelector.IsEnabled = options.Length > 1;
        loadingUi = false;
    }

    private void Quality_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loadingUi || QualitySelector.SelectedItem is not QualityOption option || option.Value == 0 && SelectedKind == MediaKind.Audio)
            return;

        if (SelectedKind == MediaKind.Audio)
            settings.AudioBitrate = option.Value;
        else
            settings.VideoHeight = option.Value;
        settings.Save();
    }

    private void Trim_Changed(object sender, RoutedEventArgs e)
    {
        TrimPanel.Visibility = TrimOption.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (TrimOption.IsChecked == true)
            TrimStartBox.Focus();
    }

    // ---------- Queue ----------

    private void Download_Click(object sender, RoutedEventArgs e) => AddCurrentToQueue();

    private void AddCurrentToQueue()
    {
        var url = UrlBox.Text.Trim();
        if (!YtDlpService.LooksLikeUrl(url))
        {
            SetHint("Pega un enlace válido.");
            SystemSounds.Hand.Play();
            return;
        }

        TimeSpan? start = null, end = null;
        if (TrimOption.IsChecked == true)
        {
            if (!YtDlpService.TryParseTime(TrimStartBox.Text, out start) || !YtDlpService.TryParseTime(TrimEndBox.Text, out end))
            {
                SetHint("Tiempo no válido. Usa por ejemplo 1:20 o 1:02:30.");
                return;
            }
            if (start.HasValue && end.HasValue && end <= start)
            {
                SetHint("El final del recorte debe ser posterior al inicio.");
                return;
            }
        }

        var info = preview?.Url == url ? preview.Value.Info : null;
        var wholePlaylist = PlaylistOption.Visibility == Visibility.Visible && PlaylistOption.IsChecked == true;
        var title = wholePlaylist ? info?.PlaylistTitle ?? info?.Title : info?.Title;

        Enqueue(BuildRequest(url, settings.OutputFolder, wholePlaylist, start, end), title ?? url);

        // Ready for the next link.
        lastClipboardUrl = url;
        UrlBox.Clear();
        TrimOption.IsChecked = false;
        TrimStartBox.Clear();
        TrimEndBox.Clear();
        UrlBox.Focus();
    }

    private DownloadRequest BuildRequest(
        string url, string folder, bool wholePlaylist, TimeSpan? start, TimeSpan? end, MediaKind? kindOverride = null)
    {
        // "mp3"/"mp4" on the command line only affect that download, not the saved choice.
        var kind = kindOverride ?? SelectedKind;
        var audioFormat = kindOverride == MediaKind.Audio ? AudioFormat.Mp3 : SelectedAudioFormat;
        var quality = kind == MediaKind.Audio ? settings.AudioBitrate : settings.VideoHeight;
        return new DownloadRequest(url, kind, audioFormat, quality, wholePlaylist, start, end, folder);
    }

    private void Enqueue(DownloadRequest request, string title)
    {
        queue.Add(new QueueItem(request, title));
        RefreshQueuePanel();
        _ = ProcessQueueAsync();
    }

    private async Task ProcessQueueAsync()
    {
        if (processing)
            return;
        processing = true;

        try
        {
            // Don't race the self-update replacing yt-dlp.exe.
            if (updateTask is { IsCompleted: false })
            {
                SetHint("Actualizando el motor de descarga…");
                await updateTask;
                SetHint(null);
            }

            while (queue.FirstOrDefault(i => i.State == QueueState.Pending) is { } next)
                await RunItemAsync(next);
        }
        finally
        {
            processing = false;
        }

        if (quietMode)
            _ = CloseSoonAsync();
    }

    private async Task RunItemAsync(QueueItem item)
    {
        item.State = QueueState.Running;
        item.Status = "Preparando…";
        item.IsIndeterminate = true;

        var progress = new Progress<DownloadProgress>(p =>
        {
            if (item.State != QueueState.Running)
                return;
            if (!string.IsNullOrEmpty(p.Title) && !item.Request.WholePlaylist)
                item.Title = p.Title;
            item.IsIndeterminate = p.Percent == null;
            if (p.Percent.HasValue)
                item.Percent = p.Percent.Value;
            item.Status = p.Percent.HasValue ? $"{p.Stage}  {p.Percent.Value:0}%" : p.Stage + "…";
            item.Detail = item.Request.WholePlaylist ? p.Title : p.Detail;
        });

        try
        {
            var path = await YtDlpService.DownloadAsync(item.Request, progress, item.Cancellation.Token);
            item.FilePath = path;
            item.State = QueueState.Done;
            item.IsIndeterminate = false;
            item.Percent = 100;
            item.Status = "✓ Completado";
            item.Detail = item.Request.WholePlaylist ? Path.GetDirectoryName(path) : Path.GetFileName(path);
            if (!IsActive)
                notifier.Show("✓ Descarga completada", item.Request.WholePlaylist ? item.Title : Path.GetFileName(path));
            SystemSounds.Asterisk.Play();
        }
        catch (OperationCanceledException)
        {
            item.State = QueueState.Canceled;
            item.IsIndeterminate = false;
            item.Percent = 0;
            item.Status = "Cancelada";
            item.Detail = null;
        }
        catch (Exception ex)
        {
            item.State = QueueState.Failed;
            item.IsIndeterminate = false;
            item.Percent = 0;
            item.Status = "Error";
            item.Detail = FirstLine(ex.Message);
            if (!IsActive)
                notifier.Show("Error en la descarga", item.Title, isError: true);
            SystemSounds.Hand.Play();
        }
    }

    private void QueueItemAction_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not QueueItem item)
            return;

        if (item.State == QueueState.Running)
        {
            item.Cancellation.Cancel();
        }
        else if (item.State == QueueState.Pending)
        {
            item.State = QueueState.Canceled;
            queue.Remove(item);
        }
        else
        {
            queue.Remove(item);
        }

        RefreshQueuePanel();
    }

    private void QueueItemShow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is QueueItem { FilePath: { } path } && File.Exists(path))
            Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    private void ClearFinished_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in queue.Where(i => i.IsFinished).ToList())
            queue.Remove(item);
        RefreshQueuePanel();
    }

    private void RefreshQueuePanel()
    {
        QueuePanel.Visibility = queue.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyHint.Visibility = queue.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task CloseSoonAsync()
    {
        // Leave time for the last notification, unless the user opened the window meanwhile.
        await Task.Delay(TimeSpan.FromSeconds(6));
        if (quietMode && !processing && WindowState == WindowState.Minimized)
            Close();
    }

    // ---------- Output folder ----------

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

    // ---------- Window chrome ----------

    private void SetHint(string? text)
    {
        HintText.Text = text ?? "";
        HintText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? text;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var active = queue.Count(i => i.IsActive);
        if (active > 0 && !quietMode &&
            MessageBox.Show(this, $"Hay {active} descarga(s) en curso o en cola. ¿Cerrar y cancelarlas?",
                "Mura Converter", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        foreach (var item in queue.Where(i => i.IsActive))
            item.Cancellation.Cancel();
        notifierInstance?.Dispose();
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimum, IntPtr maximum);

    // Minimized and idle: give memory back to Windows, which reloads pages on demand.
    private void TrimMemory()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
    }
}
