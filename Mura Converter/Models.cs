using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Mura_Converter;

internal enum MediaKind
{
    Video,
    Audio
}

internal enum AudioFormat
{
    Mp3,
    M4a,
    Opus,
    Wav
}

internal sealed record QualityOption(int Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Everything needed to run one download, captured when it is queued.</summary>
internal sealed record DownloadRequest(
    string Url,
    MediaKind Kind,
    AudioFormat AudioFormat,
    int Quality,
    bool WholePlaylist,
    TimeSpan? TrimStart,
    TimeSpan? TrimEnd,
    string OutputFolder);

internal sealed record MediaInfo(
    string Title,
    string? Uploader,
    TimeSpan? Duration,
    string? ThumbnailUrl,
    bool IsPlaylist,
    int? PlaylistCount,
    string? PlaylistTitle);

internal sealed record DownloadProgress(string Stage, double? Percent, string? Detail, string? Title);

internal enum QueueState
{
    Pending,
    Running,
    Done,
    Failed,
    Canceled
}

internal sealed class QueueItem : INotifyPropertyChanged
{
    private string title;
    private string status = "En cola";
    private string? detail;
    private double percent;
    private bool isIndeterminate;
    private QueueState state = QueueState.Pending;
    private string? filePath;

    public QueueItem(DownloadRequest request, string title)
    {
        Request = request;
        this.title = title;
    }

    public DownloadRequest Request { get; }
    public CancellationTokenSource Cancellation { get; } = new();

    public string Title { get => title; set => Set(ref title, value); }
    public string Status { get => status; set => Set(ref status, value); }
    public string? Detail { get => detail; set => Set(ref detail, value); }
    public double Percent { get => percent; set => Set(ref percent, value); }
    public bool IsIndeterminate { get => isIndeterminate; set => Set(ref isIndeterminate, value); }
    public string? FilePath { get => filePath; set { Set(ref filePath, value); OnChanged(nameof(CanShowFile)); } }

    public QueueState State
    {
        get => state;
        set
        {
            Set(ref state, value);
            OnChanged(nameof(IsActive));
            OnChanged(nameof(IsFinished));
            OnChanged(nameof(CanShowFile));
        }
    }

    public bool IsActive => state is QueueState.Pending or QueueState.Running;
    public bool IsFinished => !IsActive;
    public bool CanShowFile => state == QueueState.Done && filePath != null;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        OnChanged(name);
    }

    private void OnChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
