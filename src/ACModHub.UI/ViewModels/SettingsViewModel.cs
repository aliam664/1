using System.Collections.ObjectModel;
using ACModHub.Core;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.Infrastructure.Services;
using ACModHub.UI.Services;

namespace ACModHub.UI.ViewModels;

public sealed class DiagnosticItem
{
    public required string Title { get; init; }
    public required string StatusText { get; init; }
    public required string StatusKey { get; init; }
    public required DiagnosticStatus Status { get; init; }
    public string? Detail { get; init; }
}

public sealed class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IGameDetector _detector;
    private readonly IDiagnosticsService _diagnostics;
    private readonly IModScanner _scanner;
    private readonly IFilePickerService _picker;
    private readonly ILocalizationService _localization;
    private readonly IUiErrorHandler _errors;
    private readonly ConcurrencySettings _concurrency;

    private AppSettings _model = new();
    private string _gamePath = string.Empty;
    private string _language = "fa-IR";
    private string _catalogUrl = string.Empty;
    private bool _launchThroughSteam = true;
    private int _concurrentDownloads = 3;
    private string _updateChannel = "stable";
    private bool _checkOnStartup = true;
    private bool _reducedMotion;
    private GameInstallation? _selectedInstallation;
    private bool _isBusy;
    private string? _message;
    private bool _hasError;

    public SettingsViewModel(
        ISettingsService settings,
        IGameDetector detector,
        IDiagnosticsService diagnostics,
        IModScanner scanner,
        IFilePickerService picker,
        ILocalizationService localization,
        IUiErrorHandler errors,
        ConcurrencySettings concurrency)
    {
        _settings = settings;
        _detector = detector;
        _diagnostics = diagnostics;
        _scanner = scanner;
        _picker = picker;
        _localization = localization;
        _errors = errors;
        _concurrency = concurrency;
        SaveCommand = new AsyncRelayCommand(SaveAsync, onError: SetError);
        CancelCommand = new AsyncRelayCommand((_, token) => LoadAsync(token), onError: SetError);
        DetectCommand = new AsyncRelayCommand(DetectAsync, onError: SetError);
        BrowseCommand = new AsyncRelayCommand(BrowseAsync, onError: SetError);
        DiagnoseCommand = new AsyncRelayCommand(DiagnoseAsync, onError: SetError);
        ScanCommand = new AsyncRelayCommand(ScanAsync, onError: SetError);
        LanguageSelectFaCommand = new RelayCommand(_ => Language = "fa-IR");
        LanguageSelectEnCommand = new RelayCommand(_ => Language = "en-US");
        ChannelStableCommand = new RelayCommand(_ => UpdateChannel = "stable");
        ChannelBetaCommand = new RelayCommand(_ => UpdateChannel = "beta");
    }

    public ObservableCollection<DiagnosticItem> Diagnostics { get; } = [];
    public ObservableCollection<GameInstallation> DetectedInstallations { get; } = [];
    public IReadOnlyList<string> Languages { get; } = ["fa-IR", "en-US"];
    public string GamePath { get => _gamePath; set => SetProperty(ref _gamePath, value); }
    public string Language { get => _language; set => SetProperty(ref _language, value); }
    public string CatalogUrl { get => _catalogUrl; set => SetProperty(ref _catalogUrl, value); }
    public bool LaunchThroughSteam { get => _launchThroughSteam; set => SetProperty(ref _launchThroughSteam, value); }
    public int ConcurrentDownloads { get => _concurrentDownloads; set => SetProperty(ref _concurrentDownloads, Math.Clamp(value, 1, 8)); }
    public string UpdateChannel { get => _updateChannel; set => SetProperty(ref _updateChannel, value); }
    public bool CheckOnStartup { get => _checkOnStartup; set => SetProperty(ref _checkOnStartup, value); }
    public bool ReducedMotion { get => _reducedMotion; set => SetProperty(ref _reducedMotion, value); }
    public GameInstallation? SelectedInstallation
    {
        get => _selectedInstallation;
        set { if (SetProperty(ref _selectedInstallation, value) && value?.IsValid == true) GamePath = value.RootPath; }
    }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public bool HasError { get => _hasError; private set => SetProperty(ref _hasError, value); }
    public string OfficialEndpointsText => _localization.Get("DefaultEndpoints");
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand CancelCommand { get; }
    public AsyncRelayCommand DetectCommand { get; }
    public AsyncRelayCommand BrowseCommand { get; }
    public AsyncRelayCommand DiagnoseCommand { get; }
    public AsyncRelayCommand ScanCommand { get; }
    public RelayCommand LanguageSelectFaCommand { get; }
    public RelayCommand LanguageSelectEnCommand { get; }
    public RelayCommand ChannelStableCommand { get; }
    public RelayCommand ChannelBetaCommand { get; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _model = await _settings.LoadAsync(cancellationToken);
        DetectedInstallations.Clear();
        foreach (var knownPath in _model.KnownGamePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var known = await _detector.ValidateManualPathAsync(knownPath, cancellationToken);
            if (known.IsValid) DetectedInstallations.Add(known);
        }
        GamePath = _model.GamePath ?? string.Empty;
        Language = _model.Language;
        CatalogUrl = _model.CatalogUrl ?? string.Empty;
        LaunchThroughSteam = _model.LaunchThroughSteam;
        ConcurrentDownloads = _model.ConcurrentDownloads;
        UpdateChannel = _model.UpdateChannel;
        CheckOnStartup = _model.CheckForUpdatesOnStartup;
        ReducedMotion = _model.ReducedMotion;
        SelectedInstallation = DetectedInstallations.FirstOrDefault(x => x.RootPath.Equals(GamePath, StringComparison.OrdinalIgnoreCase));
        Message = null;
        HasError = false;
    }

    private async Task SaveAsync(object? _, CancellationToken token)
    {
        if (!string.IsNullOrWhiteSpace(CatalogUrl)
            && (!Uri.TryCreate(CatalogUrl.Trim(), UriKind.Absolute, out var overrideUri) || overrideUri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException(_localization.Get("CatalogEndpointInvalid"));

        _model.GamePath = string.IsNullOrWhiteSpace(GamePath) ? null : Path.GetFullPath(GamePath.Trim());
        _model.KnownGamePaths = DetectedInstallations.Where(x => x.IsValid).Select(x => x.RootPath)
            .Append(_model.GamePath ?? string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _model.Language = Language;
        _model.CatalogUrl = string.IsNullOrWhiteSpace(CatalogUrl) ? null : CatalogUrl.Trim();
        _model.LaunchThroughSteam = LaunchThroughSteam;
        _model.ConcurrentDownloads = ConcurrentDownloads;
        _model.UpdateChannel = UpdateChannel;
        _model.CheckForUpdatesOnStartup = CheckOnStartup;
        _model.ReducedMotion = ReducedMotion;
        await _settings.SaveAsync(_model, token);
        _concurrency.ConcurrentDownloads = ConcurrentDownloads;
        _localization.SetLanguage(Language);
        Message = _localization.Get("SettingsSaved");
    }

    private async Task DetectAsync(object? _, CancellationToken token)
    {
        IsBusy = true;
        try
        {
            var found = (await _detector.DetectAsync(token)).Where(x => x.IsValid).ToArray();
            DetectedInstallations.Clear();
            foreach (var installation in found) DetectedInstallations.Add(installation);
            SelectedInstallation = found.FirstOrDefault(x => x.RootPath.Equals(GamePath, StringComparison.OrdinalIgnoreCase)) ?? found.FirstOrDefault();
            Message = found.Length == 0 ? _localization.Get("GameNotDetected") : $"{found.Length} ✓";
        }
        finally { IsBusy = false; }
    }

    private async Task BrowseAsync(object? _, CancellationToken token)
    {
        var path = _picker.PickGameFolder();
        if (path is null) return;
        var result = await _detector.ValidateManualPathAsync(path, token);
        if (!result.IsValid) throw new InvalidOperationException(result.ValidationMessage ?? _localization.Get("GameInvalid"));
        var existing = DetectedInstallations.FirstOrDefault(x => x.RootPath.Equals(result.RootPath, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            DetectedInstallations.Add(result);
            existing = result;
        }
        SelectedInstallation = existing;
        GamePath = result.RootPath;
    }

    private async Task DiagnoseAsync(object? _, CancellationToken token)
    {
        IsBusy = true;
        try
        {
            Diagnostics.Clear();
            foreach (var item in await _diagnostics.RunAsync(GamePath, token))
            {
                Diagnostics.Add(new DiagnosticItem
                {
                    Title = _localization.Get("Diag" + Capitalize(item.Key)),
                    Status = item.Status,
                    StatusKey = "DiagStatus" + item.Status,
                    StatusText = _localization.Get("DiagStatus" + item.Status),
                    Detail = string.IsNullOrWhiteSpace(item.Detail) ? item.Message : item.Detail
                });
            }
        }
        finally { IsBusy = false; }
    }

    private static string Capitalize(string value) => string.IsNullOrWhiteSpace(value) ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private async Task ScanAsync(object? _, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(GamePath)) throw new InvalidOperationException(_localization.Get("DashNotConfigured"));
        IsBusy = true;
        try
        {
            var progress = new Progress<double>(value => Message = _localization.Format("ScanProgress", value));
            var found = await _scanner.ScanAsync(GamePath, true, progress, token);
            await _scanner.ImportAsync(found, token);
            Message = _localization.Get("ScanDone");
        }
        finally { IsBusy = false; }
    }

    private void SetError(Exception exception)
    {
        HasError = true;
        Message = _errors.Handle(exception, _localization.Get("SettingsTitle"));
        IsBusy = false;
    }
}
