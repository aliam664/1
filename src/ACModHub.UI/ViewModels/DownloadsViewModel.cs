using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.Core.Services;
using ACModHub.UI.Services;

namespace ACModHub.UI.ViewModels;

/// <summary>Display wrapper for a download job with localized state and formatted figures.</summary>
public sealed class DownloadJobItem
{
    public required DownloadJob Job { get; init; }
    public required string StateText { get; init; }
    public required string SpeedText { get; init; }
    public required string EtaText { get; init; }
    public double? Percentage { get; init; }
    public bool CanPause { get; init; }
    public bool CanResume { get; init; }
    public bool CanCancel { get; init; }
    public bool CanRetry { get; init; }
    public bool CanInstall { get; init; }
}

public sealed class DownloadsViewModel : ObservableObject
{
    private readonly IDownloadManager _downloads;
    private readonly ILocalizationService _localization;
    private readonly IUiErrorHandler _errors;
    private string _url = string.Empty;
    private string _fileName = string.Empty;
    private string _sha256 = string.Empty;
    private DownloadJob? _selected;
    private string? _message;

    public DownloadsViewModel(IDownloadManager downloads, ILocalizationService localization, IUiErrorHandler errors)
    {
        _downloads = downloads;
        _localization = localization;
        _errors = errors;
        _downloads.ProgressChanged += OnProgress;
        AddCommand = new AsyncRelayCommand(AddAsync, parameter => Uri.TryCreate(Url, UriKind.Absolute, out var parsed) && parsed.Scheme is "http" or "https" && !string.IsNullOrWhiteSpace(FileName), SetError);
        PauseCommand = new AsyncRelayCommand((_, token) => _downloads.PauseAsync(Selected!.Request.Id, token), _ => Selected?.State is DownloadState.Downloading or DownloadState.Queued, SetError);
        ResumeCommand = new AsyncRelayCommand((_, token) => _downloads.ResumeAsync(Selected!.Request.Id, token), _ => Selected?.State is DownloadState.Paused or DownloadState.Failed, SetError);
        CancelCommand = new AsyncRelayCommand((_, token) => _downloads.CancelAsync(Selected!.Request.Id, token), _ => Selected?.State is DownloadState.Queued or DownloadState.Downloading or DownloadState.Paused, SetError);
        RetryCommand = new AsyncRelayCommand((_, token) => _downloads.RetryAsync(Selected!.Request.Id, token), _ => Selected?.State is DownloadState.Failed or DownloadState.Cancelled, SetError);
        InstallCommand = new RelayCommand(_ => PackageInstallRequested?.Invoke(Selected!.DestinationPath!), _ => Selected?.State == DownloadState.Completed && File.Exists(Selected.DestinationPath));
        ClearCompletedCommand = new RelayCommand(ClearCompleted, _ => _downloads.Jobs.Any(x => x.State == DownloadState.Completed));
        Refresh();
    }

    public event Action<string>? PackageInstallRequested;
    public ObservableCollection<DownloadJobItem> Jobs { get; } = [];
    public string Url { get => _url; set { if (SetProperty(ref _url, value)) AddCommand.RaiseCanExecuteChanged(); } }
    public string FileName { get => _fileName; set { if (SetProperty(ref _fileName, value)) AddCommand.RaiseCanExecuteChanged(); } }
    public string Sha256 { get => _sha256; set => SetProperty(ref _sha256, value); }
    public DownloadJob? Selected { get => _selected; set { if (SetProperty(ref _selected, value)) RaiseActions(); } }
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public bool IsEmpty => Jobs.Count == 0;
    public AsyncRelayCommand AddCommand { get; }
    public AsyncRelayCommand PauseCommand { get; }
    public AsyncRelayCommand ResumeCommand { get; }
    public AsyncRelayCommand CancelCommand { get; }
    public AsyncRelayCommand RetryCommand { get; }
    public RelayCommand InstallCommand { get; }
    public RelayCommand ClearCompletedCommand { get; }

    private async Task AddAsync(object? _, CancellationToken cancellationToken)
    {
        var expected = string.IsNullOrWhiteSpace(Sha256) ? null : Sha256.Trim();
        if (!SafePath.IsSafeFileName(FileName.Trim()))
            throw new ArgumentException(_localization.Get("ErrInvalidFileName"));
        await _downloads.EnqueueAsync(new DownloadRequest { Source = new Uri(Url.Trim()), FileName = FileName.Trim(), ExpectedSha256 = expected }, cancellationToken);
        Url = string.Empty;
        FileName = string.Empty;
        Sha256 = string.Empty;
        Refresh();
    }

    private void OnProgress(object? sender, DownloadProgress progress)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) Refresh();
        else dispatcher.InvokeAsync(Refresh);
    }

    private void Refresh()
    {
        var selectedId = Selected?.Request.Id;
        Jobs.Clear();
        foreach (var job in _downloads.Jobs)
        {
            Jobs.Add(new DownloadJobItem
            {
                Job = job,
                StateText = _localization.Get("State" + job.State),
                SpeedText = job.SpeedBytesPerSecond is > 0 ? FormatBytes((long)job.SpeedBytesPerSecond.Value) + "/s" : "—",
                EtaText = job.EstimatedTimeRemaining is { } eta and > TimeSpan.Zero ? eta.ToString(@"mm\:ss") : "—",
                Percentage = job.TotalBytes is > 0 ? job.BytesReceived * 100d / job.TotalBytes.Value : null,
                CanPause = job.State is DownloadState.Downloading or DownloadState.Queued,
                CanResume = job.State is DownloadState.Paused or DownloadState.Failed,
                CanCancel = job.State is DownloadState.Queued or DownloadState.Downloading or DownloadState.Paused,
                CanRetry = job.State is DownloadState.Failed or DownloadState.Cancelled,
                CanInstall = job.State == DownloadState.Completed && File.Exists(job.DestinationPath)
            });
        }
        Selected = Jobs.FirstOrDefault(x => x.Job.Request.Id == selectedId)?.Job;
        OnPropertyChanged(nameof(IsEmpty));
        ClearCompletedCommand.RaiseCanExecuteChanged();
    }

    private void ClearCompleted(object? _)
    {
        foreach (var job in _downloads.Jobs.Where(x => x.State == DownloadState.Completed).ToArray())
        {
            try
            {
                if (job.DestinationPath is not null && File.Exists(job.DestinationPath)) File.Delete(job.DestinationPath);
            }
            catch (IOException) { }
        }
        Refresh();
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1) { value /= 1024; index++; }
        return $"{value:0.#} {units[index]}";
    }

    private void RaiseActions()
    {
        PauseCommand.RaiseCanExecuteChanged();
        ResumeCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        RetryCommand.RaiseCanExecuteChanged();
        InstallCommand.RaiseCanExecuteChanged();
    }

    private void SetError(Exception exception) => Message = _errors.Handle(exception, _localization.Get("DownloadsTitle"));
}
