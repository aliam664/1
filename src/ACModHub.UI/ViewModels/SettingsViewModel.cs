using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.UI.Services;

namespace ACModHub.UI.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IGameDetector _detector;
    private readonly IDiagnosticsService _diagnostics;
    private readonly IModScanner _scanner;
    private readonly IFilePickerService _picker;
    private readonly ILocalizationService _localization;
    private readonly IUiErrorHandler _errors;
    private AppSettings _model = new();
    private string _gamePath = string.Empty;
    private string _language = "fa-IR";
    private bool _launchThroughSteam = true;
    private int _concurrentDownloads = 3;
    private GameInstallation? _selectedInstallation;
    private bool _isBusy;
    private string? _message;

    public SettingsViewModel(ISettingsService settings, IGameDetector detector, IDiagnosticsService diagnostics, IModScanner scanner, IFilePickerService picker, ILocalizationService localization, IUiErrorHandler errors)
    {
        _settings = settings; _detector = detector; _diagnostics = diagnostics; _scanner = scanner; _picker = picker; _localization = localization; _errors = errors;
        SaveCommand = new AsyncRelayCommand(SaveAsync, onError: SetError);
        DetectCommand = new AsyncRelayCommand(DetectAsync, onError: SetError);
        BrowseCommand = new AsyncRelayCommand(BrowseAsync, onError: SetError);
        DiagnoseCommand = new AsyncRelayCommand(DiagnoseAsync, onError: SetError);
        ScanCommand = new AsyncRelayCommand(ScanAsync, onError: SetError);
    }

    public ObservableCollection<DiagnosticResult> Diagnostics { get; } = [];
    public ObservableCollection<GameInstallation> DetectedInstallations { get; } = [];
    public IReadOnlyList<string> Languages { get; } = ["fa-IR", "en-US"];
    public string GamePath { get => _gamePath; set => SetProperty(ref _gamePath, value); }
    public string Language { get => _language; set => SetProperty(ref _language, value); }
    public bool LaunchThroughSteam { get => _launchThroughSteam; set => SetProperty(ref _launchThroughSteam, value); }
    public int ConcurrentDownloads { get => _concurrentDownloads; set => SetProperty(ref _concurrentDownloads, Math.Clamp(value, 1, 8)); }
    public GameInstallation? SelectedInstallation
    {
        get => _selectedInstallation;
        set { if (SetProperty(ref _selectedInstallation, value) && value?.IsValid == true) GamePath = value.RootPath; }
    }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand DetectCommand { get; }
    public AsyncRelayCommand BrowseCommand { get; }
    public AsyncRelayCommand DiagnoseCommand { get; }
    public AsyncRelayCommand ScanCommand { get; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _model = await _settings.LoadAsync(cancellationToken);
        DetectedInstallations.Clear();
        foreach (var knownPath in _model.KnownGamePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var known = await _detector.ValidateManualPathAsync(knownPath, cancellationToken);
            if (known.IsValid) DetectedInstallations.Add(known);
        }
        GamePath = _model.GamePath ?? string.Empty; Language = _model.Language; LaunchThroughSteam = _model.LaunchThroughSteam; ConcurrentDownloads = _model.ConcurrentDownloads;
        SelectedInstallation = DetectedInstallations.FirstOrDefault(x => x.RootPath.Equals(GamePath, StringComparison.OrdinalIgnoreCase));
    }

    private async Task SaveAsync(object? _, CancellationToken token)
    {
        _model.GamePath = string.IsNullOrWhiteSpace(GamePath) ? null : Path.GetFullPath(GamePath);
        _model.KnownGamePaths = DetectedInstallations.Where(x => x.IsValid).Select(x => x.RootPath).Append(_model.GamePath ?? string.Empty).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _model.Language = Language; _model.LaunchThroughSteam = LaunchThroughSteam; _model.ConcurrentDownloads = ConcurrentDownloads;
        await _settings.SaveAsync(_model, token);
        _localization.SetLanguage(Language);
        Message = "Settings saved.";
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
            Message = found.Length == 0 ? "Assetto Corsa was not detected." : $"Detected {found.Length} valid game installation(s).";
        }
        finally { IsBusy = false; }
    }
    private async Task BrowseAsync(object? _, CancellationToken token)
    {
        var path = _picker.PickGameFolder(); if (path is null) return;
        var result = await _detector.ValidateManualPathAsync(path, token); if (!result.IsValid) throw new InvalidOperationException(result.ValidationMessage);
        var existing = DetectedInstallations.FirstOrDefault(x => x.RootPath.Equals(result.RootPath, StringComparison.OrdinalIgnoreCase));
        if (existing is null) { DetectedInstallations.Add(result); existing = result; }
        SelectedInstallation = existing;
        GamePath = result.RootPath;
    }
    private async Task DiagnoseAsync(object? _, CancellationToken token)
    {
        IsBusy = true;
        try { Diagnostics.Clear(); foreach (var item in await _diagnostics.RunAsync(GamePath, token)) Diagnostics.Add(item); }
        finally { IsBusy = false; }
    }
    private async Task ScanAsync(object? _, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(GamePath)) throw new InvalidOperationException("Select the game path first.");
        IsBusy = true;
        try
        {
            var progress = new Progress<double>(value => Message = $"Scanning… {value:P0}");
            var found = await _scanner.ScanAsync(GamePath, true, progress, token);
            await _scanner.ImportAsync(found, token);
            Message = $"Imported {found.Count} existing content item(s).";
        }
        finally { IsBusy = false; }
    }
    private void SetError(Exception exception) { Message = _errors.Handle(exception, "Settings or diagnostics"); IsBusy = false; }
}
