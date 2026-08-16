using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using ACModHub.Core;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.UI.Services;

namespace ACModHub.UI.ViewModels;

public sealed record StorePackage(string Path, IReadOnlyDictionary<string, string> Metadata);

public enum StoreSortMode { Default, Name, Newest, Version }

/// <summary>Display item for the native (non-WebView2) store fallback.</summary>
public sealed class CatalogModItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Author { get; init; }
    public required string Version { get; init; }
    public required string CategoryText { get; init; }
    public required string Description { get; init; }
    public string? SizeText { get; init; }
    public Uri? CoverUri { get; init; }
    public bool IsInstallable { get; init; }
    public bool IsDeprecated { get; init; }
    public bool IsRevoked { get; init; }
    public string? BlockReason { get; init; }
}

public sealed class CatalogViewModel : ObservableObject
{
    private readonly IModCatalogService _catalogService;
    private readonly IDownloadManager _downloads;
    private readonly IStorePageBuilder _pageBuilder;
    private readonly ILocalizationService _localization;
    private readonly IUiErrorHandler _errors;
    private readonly WebView2RuntimeDetector _webViewDetector;
    private readonly ICoverImageService _coverService;

    private CatalogLoadResult? _result;
    private IReadOnlyList<CatalogMod> _allMods = [];
    private string _htmlPath = string.Empty;
    private string? _progressJson;
    private string _search = string.Empty;
    private ModCategory? _category;
    private StoreSortMode _sort = StoreSortMode.Default;
    private bool _isBusy;
    private bool _isDownloading;
    private bool _hasError;
    private string _sourceText = string.Empty;
    private string _warningText = string.Empty;
    private string _downloadStatusText = string.Empty;
    private double? _downloadProgress;
    private Guid? _activeDownloadId;
    private CancellationTokenSource? _downloadCancellation;

    public CatalogViewModel(
        IModCatalogService catalogService,
        IDownloadManager downloads,
        IStorePageBuilder pageBuilder,
        ILocalizationService localization,
        IUiErrorHandler errors,
        WebView2RuntimeDetector webViewDetector,
        ICoverImageService coverService)
    {
        _catalogService = catalogService;
        _downloads = downloads;
        _pageBuilder = pageBuilder;
        _localization = localization;
        _errors = errors;
        _webViewDetector = webViewDetector;
        _coverService = coverService;
        InstallCommand = new AsyncRelayCommand(InstallAsync, parameter => parameter is string modId && !IsBusy && !IsDownloading, SetError);
        RefreshCommand = new AsyncRelayCommand((_, token) => LoadAsync(true, token), _ => !IsBusy, SetError);
        CancelDownloadCommand = new AsyncRelayCommand(CancelDownloadAsync, _ => IsDownloading, SetError);
        InstallRuntimeCommand = new RelayCommand(_ =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                _ = _errors.Handle(ex, _localization.Get("StoreTitle"));
            }
        });
    }

    public event Action<string>? HtmlPathChanged;
    public event Action<string>? WebMessage;
    public event Action<StorePackage>? PackageReady;

    public string HtmlPath { get => _htmlPath; private set { if (SetProperty(ref _htmlPath, value)) HtmlPathChanged?.Invoke(value); } }
    public string? ProgressJson { get => _progressJson; private set { if (SetProperty(ref _progressJson, value) && value is not null) WebMessage?.Invoke(value); } }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) { InstallCommand.RaiseCanExecuteChanged(); RefreshCommand.RaiseCanExecuteChanged(); } } }
    public bool IsDownloading { get => _isDownloading; private set { if (SetProperty(ref _isDownloading, value)) { InstallCommand.RaiseCanExecuteChanged(); CancelDownloadCommand.RaiseCanExecuteChanged(); } } }
    public bool HasError { get => _hasError; private set => SetProperty(ref _hasError, value); }
    public bool WebViewAvailable => _webViewDetector.IsAvailable();
    public bool ShowNativeStore => !WebViewAvailable;
    public string SourceText { get => _sourceText; private set => SetProperty(ref _sourceText, value); }
    public string WarningText { get => _warningText; private set => SetProperty(ref _warningText, value); }
    public string DownloadStatusText { get => _downloadStatusText; private set => SetProperty(ref _downloadStatusText, value); }
    public double? DownloadProgress { get => _downloadProgress; private set => SetProperty(ref _downloadProgress, value); }
    public string Search { get => _search; set { if (SetProperty(ref _search, value)) ApplyFilter(); } }
    public ModCategory? Category { get => _category; set { if (SetProperty(ref _category, value)) ApplyFilter(); } }
    public StoreSortMode Sort { get => _sort; set { if (SetProperty(ref _sort, value)) ApplyFilter(); } }
    public AsyncRelayCommand InstallCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand CancelDownloadCommand { get; }
    public RelayCommand InstallRuntimeCommand { get; }
    /// <summary>Cover loader used by the native store fallback (AsyncCoverBehavior).</summary>
    public ICoverImageService CoverService => _coverService;

    public ObservableCollection<CatalogModItem> Mods { get; } = [];
    /// <summary>First entry is null = "All categories".</summary>
    public IReadOnlyList<object?> CategoryOptions { get; } = [null, ModCategory.Car, ModCategory.Track, ModCategory.Skin, ModCategory.App, ModCategory.Weather, ModCategory.Csp, ModCategory.Miscellaneous];
    public IReadOnlyList<StoreSortMode> SortModes { get; } = [StoreSortMode.Default, StoreSortMode.Name, StoreSortMode.Newest, StoreSortMode.Version];
    public bool IsEmpty => !IsBusy && !HasError && Mods.Count == 0;
    public bool IsOffline => !IsBusy && !HasError && _result?.IsCached == true && _result.Warning is CatalogWarning.RemoteUnavailableUsingCache or CatalogWarning.RemoteUnavailableUsingEmbedded;

    public Task LoadAsync(CancellationToken cancellationToken = default) => LoadAsync(false, cancellationToken);

    private async Task LoadAsync(bool forceRemote, CancellationToken cancellationToken)
    {
        IsBusy = true;
        HasError = false;
        try
        {
            _result = await _catalogService.LoadAsync(forceRemote, cancellationToken);
            _allMods = _result.Catalog.Mods;
            SourceText = LocalizeSource(_result.Source);
            WarningText = LocalizeWarning(_result.Warning);

            var strings = BuildStringTable();
            HtmlPath = _pageBuilder.Build(_result.Catalog, _localization.Language, strings, SourceText, WarningText);
            ApplyFilter();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            HasError = true;
            WarningText = _errors.Handle(ex, _localization.Get("StoreTitle"));
            Mods.Clear();
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsOffline));
        }
    }

    private void ApplyFilter()
    {
        IEnumerable<CatalogMod> query = _allMods;
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var needle = Search.Trim();
            query = query.Where(x =>
                x.Name.Fa?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true
                || x.Name.En?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true
                || x.AuthorName.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }
        if (Category.HasValue) query = query.Where(x => x.Category == Category.Value);
        query = Sort switch
        {
            StoreSortMode.Name => query.OrderBy(x => x.Name.Get(_localization.Language), StringComparer.CurrentCultureIgnoreCase),
            StoreSortMode.Newest => query.OrderByDescending(x => x.PublishedAt),
            StoreSortMode.Version => query.OrderByDescending(x => SemanticVersion.TryParse(x.Version, out var version) ? version : default),
            _ => query
        };

        Mods.Clear();
        foreach (var mod in query) Mods.Add(ToItem(mod));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private CatalogModItem ToItem(CatalogMod mod)
    {
        var language = _localization.Language;
        var size = mod.ExpectedSize is > 0 ? FormatSize(mod.ExpectedSize.Value) : null;
        string? blockReason = mod.Status switch
        {
            CatalogModStatus.Revoked => _localization.Get("StoreRevokedHint") + mod.RevocationReason,
            _ when mod.BlockReason?.StartsWith("requires-launcher:", StringComparison.OrdinalIgnoreCase) == true => _localization.Get("StoreNeedsLauncher"),
            _ => null
        };
        return new CatalogModItem
        {
            Id = mod.Id,
            Name = mod.Name.Get(language),
            Author = mod.AuthorName,
            Version = mod.Version,
            CategoryText = _localization.Get("Category" + mod.Category),
            Description = mod.Description?.Get(language) ?? string.Empty,
            SizeText = size,
            CoverUri = mod.CoverUri,
            IsInstallable = mod.IsInstallable,
            IsDeprecated = mod.Status == CatalogModStatus.Deprecated,
            IsRevoked = mod.Status == CatalogModStatus.Revoked,
            BlockReason = blockReason
        };
    }

    public async Task InstallAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (parameter is not string modId || _result is null) return;
        var mod = _result.Catalog.Mods.FirstOrDefault(x => x.Id.Equals(modId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ModHubException("The selected catalog item no longer exists.");
        if (mod.DownloadUrl is null || !mod.IsInstallable)
            throw new ModHubException(mod.BlockReason ?? "This catalog item is not installable.");
        if (mod.FileName is null)
            throw new ModHubException("The catalog item has no package file name.");

        IsDownloading = true;
        DownloadProgress = null;
        DownloadStatusText = string.Empty;
        _downloadCancellation = new CancellationTokenSource();
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _downloadCancellation.Token);
        EventHandler<DownloadProgress>? handler = null;
        try
        {
            var request = new DownloadRequest
            {
                Source = mod.DownloadUrl,
                FileName = mod.FileName,
                ExpectedSha256 = mod.Sha256,
                ExpectedSize = mod.ExpectedSize,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["catalogId"] = mod.Id,
                    ["catalogVersion"] = mod.Version,
                    ["name"] = mod.Name.Get(_localization.Language),
                    ["author"] = mod.AuthorName,
                    ["source"] = mod.DownloadUrl.AbsoluteUri
                }
            };
            var completed = new TaskCompletionSource<DownloadJob>(TaskCreationOptions.RunContinuationsAsynchronously);
            handler = (_, progress) =>
            {
                if (progress.JobId != request.Id) return;
                if (progress.State is DownloadState.Downloading or DownloadState.Verifying or DownloadState.Completed)
                {
                    DownloadProgress = progress.Percentage;
                    DownloadStatusText = progress.State == DownloadState.Verifying
                        ? _localization.Get("StoreVerifying")
                        : _localization.Format("StoreDownloading", mod.Name.Get(_localization.Language)) + " — " + FormatBytes(progress.BytesReceived);
                    PublishProgress(progress);
                }
                var job = _downloads.Jobs.FirstOrDefault(x => x.Request.Id == request.Id);
                if (job is null) return;
                if (progress.State == DownloadState.Completed) completed.TrySetResult(job);
                else if (progress.State == DownloadState.Failed) completed.TrySetException(new ModHubException(job.Error ?? "Download failed."));
                else if (progress.State == DownloadState.Cancelled) completed.TrySetCanceled(linked.Token);
            };
            _downloads.ProgressChanged += handler;
            using var registration = linked.Token.Register(() => completed.TrySetCanceled(linked.Token));
            await _downloads.EnqueueAsync(request, linked.Token);
            _activeDownloadId = request.Id;
            var result = await completed.Task.ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(result.DestinationPath) || !File.Exists(result.DestinationPath))
                throw new ModHubException("Downloaded package is missing after completion.");
            DownloadProgress = 100;
            DownloadStatusText = _localization.Get("StoreDownloadComplete");
            PackageReady?.Invoke(new StorePackage(result.DestinationPath, request.Metadata));
        }
        catch (OperationCanceledException)
        {
            DownloadStatusText = _localization.Get("OperationCancelled");
        }
        finally
        {
            if (handler is not null) _downloads.ProgressChanged -= handler;
            linked.Dispose();
            _downloadCancellation.Dispose();
            _downloadCancellation = null;
            _activeDownloadId = null;
            IsDownloading = false;
        }
    }

    private async Task CancelDownloadAsync(object? _, CancellationToken cancellationToken)
    {
        _downloadCancellation?.Cancel();
        if (_activeDownloadId is { } id) await _downloads.CancelAsync(id, cancellationToken);
    }

    private void PublishProgress(DownloadProgress progress)
    {
        ProgressJson = JsonSerializer.Serialize(new
        {
            kind = "download",
            text = DownloadStatusText,
            percentage = progress.Percentage,
            cancelable = true
        });
    }

    private string LocalizeSource(string source) => source switch
    {
        "primary" => _localization.Get("StoreSourcePrimary"),
        "fallback" => _localization.Get("StoreSourceFallback"),
        "override" => _localization.Get("StoreSourceOverride"),
        "cache" => _localization.Get("StoreSourceCached"),
        _ => _localization.Get("StoreSourceEmbedded")
    };

    private string LocalizeWarning(CatalogWarning warning) => warning switch
    {
        CatalogWarning.RemoteUnavailableUsingCache => _localization.Get("StoreOfflineHint"),
        CatalogWarning.RemoteUnavailableUsingEmbedded => _localization.Get("StoreOfflineTitle") + " — " + _localization.Get("StoreEmptyHint"),
        CatalogWarning.CatalogRequiresNewerLauncher => _localization.Get("StoreNeedsLauncher"),
        _ => string.Empty
    };

    private Dictionary<string, string> BuildStringTable()
    {
        string[] keys =
        [
            "StoreTitle", "StoreSearchPlaceholder", "StoreRefresh", "StoreRetry", "StoreAllCategories",
            "StoreSortDefault", "StoreSortName", "StoreSortNewest", "StoreSortVersion", "StoreLoading",
            "StoreEmptyTitle", "StoreEmptyHint", "StoreOfflineTitle", "StoreOfflineHint", "StoreErrorTitle",
            "StoreErrorHint", "StoreInstall", "StoreInstalledTag", "StoreInstallBlocked", "StoreDeprecatedWarning",
            "StoreRevokedTitle", "StoreRevokedHint", "StoreNeedsLauncher", "StoreAuthor", "StoreVersion",
            "StoreSize", "StoreCategory", "StoreStatus", "StorePublished", "StoreDownloading", "StoreVerifying",
            "StoreDownloadComplete", "StoreCancelDownload", "StoreFeatured", "Cancel", "Retry", "Close",
            "CommonLoading", "CommonError"
        ];
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in keys) table[key] = _localization.Get(key);
        return table;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1) { value /= 1024; index++; }
        return $"{value:0.#} {units[index]}";
    }

    private static string FormatSize(long bytes) => FormatBytes(bytes);

    private void SetError(Exception exception)
    {
        HasError = true;
        WarningText = _errors.Handle(exception, _localization.Get("StoreTitle"));
        ProgressJson = JsonSerializer.Serialize(new { kind = "error", text = WarningText });
        IsDownloading = false;
        IsBusy = false;
    }
}
