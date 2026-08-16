using System.IO;
using System.Text.Json;
using ACModHub.Core;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.UI.Services;

namespace ACModHub.UI.ViewModels;

public sealed class CatalogViewModel : ObservableObject
{
    private readonly IModCatalogService _catalogService;
    private readonly IDownloadManager _downloads;
    private readonly IHtmlCatalogPageBuilder _pageBuilder;
    private readonly ILocalizationService _localization;
    private readonly IUiErrorHandler _errors;
    private CatalogLoadResult? _catalog;
    private string _html = string.Empty;
    private string? _progressJson;
    private bool _isBusy;
    private string? _message;

    public CatalogViewModel(
        IModCatalogService catalogService,
        IDownloadManager downloads,
        IHtmlCatalogPageBuilder pageBuilder,
        ILocalizationService localization,
        IUiErrorHandler errors)
    {
        _catalogService = catalogService;
        _downloads = downloads;
        _pageBuilder = pageBuilder;
        _localization = localization;
        _errors = errors;
        InstallCommand = new AsyncRelayCommand(InstallAsync, parameter => parameter is string && !IsBusy, SetError);
        RefreshCommand = new AsyncRelayCommand((_, token) => LoadAsync(true, token), _ => !IsBusy, SetError);
    }

    public event Action<string>? HtmlChanged;
    public event Action<string>? ProgressChanged;
    public event Action<string>? PackageReady;
    public string Html { get => _html; private set { if (SetProperty(ref _html, value)) HtmlChanged?.Invoke(value); } }
    public string? ProgressJson { get => _progressJson; private set { if (SetProperty(ref _progressJson, value) && value is not null) ProgressChanged?.Invoke(value); } }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) { InstallCommand.RaiseCanExecuteChanged(); RefreshCommand.RaiseCanExecuteChanged(); } } }
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public AsyncRelayCommand InstallCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }

    public Task LoadAsync(CancellationToken cancellationToken = default) => LoadAsync(false, cancellationToken);

    private async Task LoadAsync(bool forceRemote, CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            _catalog = await _catalogService.LoadAsync(forceRemote, cancellationToken);
            Html = _pageBuilder.Build(_catalog, _localization.Language);
            Message = _catalog.Warning;
        }
        finally { IsBusy = false; }
    }

    private async Task InstallAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (parameter is not string modId || _catalog is null) return;
        var mod = _catalog.Catalog.Mods.FirstOrDefault(x => x.Id.Equals(modId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ModHubException("The selected catalog item no longer exists.");
        if (mod.DownloadUrl is null) throw new ModHubException("This catalog item does not have a download link yet.");
        IsBusy = true;
        EventHandler<DownloadProgress>? handler = null;
        try
        {
            var fileName = ResolveFileName(mod);
            var request = new DownloadRequest
            {
                Source = mod.DownloadUrl,
                FileName = fileName,
                ExpectedSha256 = mod.Sha256,
                ExpectedSize = mod.ExpectedSize,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["catalogId"] = mod.Id,
                    ["name"] = mod.Name,
                    ["version"] = mod.Version,
                    ["author"] = mod.Author
                }
            };
            var completed = new TaskCompletionSource<DownloadJob>(TaskCreationOptions.RunContinuationsAsynchronously);
            handler = (_, progress) =>
            {
                if (progress.JobId != request.Id) return;
                ProgressJson = JsonSerializer.Serialize(new
                {
                    title = progress.State is DownloadState.Verifying ? "Verifying SHA-256" : $"Downloading {mod.Name}",
                    message = BuildProgressText(progress),
                    done = progress.State == DownloadState.Completed
                });
                var job = _downloads.Jobs.FirstOrDefault(x => x.Request.Id == request.Id);
                if (job is null) return;
                if (progress.State == DownloadState.Completed) completed.TrySetResult(job);
                else if (progress.State == DownloadState.Failed) completed.TrySetException(new ModHubException(job.Error ?? "Catalog download failed."));
                else if (progress.State == DownloadState.Cancelled) completed.TrySetCanceled(cancellationToken);
            };
            _downloads.ProgressChanged += handler;
            using var registration = cancellationToken.Register(() => completed.TrySetCanceled(cancellationToken));
            await _downloads.EnqueueAsync(request, cancellationToken);
            var result = await completed.Task.ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(result.DestinationPath) || !File.Exists(result.DestinationPath)) throw new ModHubException("Downloaded package is missing after completion.");
            Message = $"{mod.Name} downloaded and verified.";
            PackageReady?.Invoke(result.DestinationPath);
        }
        finally
        {
            if (handler is not null) _downloads.ProgressChanged -= handler;
            IsBusy = false;
        }
    }

    private static string ResolveFileName(CatalogMod mod)
    {
        if (!string.IsNullOrWhiteSpace(mod.FileName)) return mod.FileName;
        var candidate = Path.GetFileName(mod.DownloadUrl!.LocalPath);
        return string.IsNullOrWhiteSpace(candidate) ? mod.Id + ".zip" : candidate;
    }

    private static string BuildProgressText(DownloadProgress progress)
    {
        var received = FormatBytes(progress.BytesReceived);
        var total = progress.TotalBytes.HasValue ? " / " + FormatBytes(progress.TotalBytes.Value) : string.Empty;
        var speed = progress.SpeedBytesPerSecond.HasValue ? " · " + FormatBytes((long)progress.SpeedBytesPerSecond.Value) + "/s" : string.Empty;
        var eta = progress.EstimatedTimeRemaining.HasValue && progress.EstimatedTimeRemaining.Value > TimeSpan.Zero ? " · ETA " + progress.EstimatedTimeRemaining.Value.ToString("mm\\:ss") : string.Empty;
        return received + total + speed + eta;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1) { value /= 1024; index++; }
        return $"{value:0.#} {units[index]}";
    }

    private void SetError(Exception exception)
    {
        Message = _errors.Handle(exception, "Catalog operation");
        ProgressJson = JsonSerializer.Serialize(new { title = "Operation failed", message = Message, done = true });
        IsBusy = false;
    }
}
