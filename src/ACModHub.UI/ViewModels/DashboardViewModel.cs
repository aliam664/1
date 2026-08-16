using ACModHub.Core;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.UI.Services;

namespace ACModHub.UI.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IGameDetector _detector;
    private readonly IModRepository _repository;
    private readonly IModCatalogService _catalog;
    private readonly ILaunchService _launcher;
    private readonly IFilePickerService _picker;
    private readonly ILocalizationService _localization;
    private readonly IUiErrorHandler _errors;

    private AppSettings _currentSettings = new();
    private GameInstallation? _installation;
    private string _gamePath = "—";
    private string _gameStatus = string.Empty;
    private bool _gameValid;
    private int _installedMods;
    private int _cars;
    private int _tracks;
    private int _updates;
    private bool _isBusy;
    private bool _hasError;

    public DashboardViewModel(
        ISettingsService settings,
        IGameDetector detector,
        IModRepository repository,
        IModCatalogService catalog,
        ILaunchService launcher,
        IFilePickerService picker,
        ILocalizationService localization,
        IUiErrorHandler errors)
    {
        _settings = settings;
        _detector = detector;
        _repository = repository;
        _catalog = catalog;
        _launcher = launcher;
        _picker = picker;
        _localization = localization;
        _errors = errors;
        RefreshCommand = new AsyncRelayCommand((_, token) => LoadAsync(token), onError: SetError);
        LaunchCommand = new AsyncRelayCommand(LaunchAsync, _ => GameValid, SetError);
        BrowseCommand = new AsyncRelayCommand(BrowseAsync, onError: SetError);
    }

    public string GamePath { get => _gamePath; private set => SetProperty(ref _gamePath, value); }
    public string GameStatus { get => _gameStatus; private set => SetProperty(ref _gameStatus, value); }
    public bool GameValid { get => _gameValid; private set { if (SetProperty(ref _gameValid, value)) LaunchCommand.RaiseCanExecuteChanged(); } }
    public int InstalledMods { get => _installedMods; private set => SetProperty(ref _installedMods, value); }
    public int Cars { get => _cars; private set => SetProperty(ref _cars, value); }
    public int Tracks { get => _tracks; private set => SetProperty(ref _tracks, value); }
    public int Updates { get => _updates; private set => SetProperty(ref _updates, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public bool HasError { get => _hasError; private set => SetProperty(ref _hasError, value); }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand LaunchCommand { get; }
    public AsyncRelayCommand BrowseCommand { get; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        HasError = false;
        try
        {
            _currentSettings = await _settings.LoadAsync(cancellationToken);
            _installation = null;
            if (!string.IsNullOrWhiteSpace(_currentSettings.GamePath))
                _installation = await _detector.ValidateManualPathAsync(_currentSettings.GamePath, cancellationToken);
            if (_installation?.IsValid != true)
                _installation = (await _detector.DetectAsync(cancellationToken)).FirstOrDefault(x => x.IsValid);

            if (_installation is not null)
            {
                GamePath = _installation.RootPath;
                GameValid = _installation.IsValid;
                GameStatus = GameValid ? _localization.Get("GameReady") : _localization.Get("GameInvalid");
                if (GameValid && !string.Equals(_currentSettings.GamePath, _installation.RootPath, StringComparison.OrdinalIgnoreCase))
                {
                    _currentSettings.GamePath = _installation.RootPath;
                    await _settings.SaveAsync(_currentSettings, cancellationToken);
                }
            }
            else
            {
                GamePath = "—";
                GameValid = false;
                GameStatus = _localization.Get("GameNotDetected");
            }

            var mods = await _repository.GetAllAsync(cancellationToken);
            InstalledMods = mods.Count;
            Cars = mods.Count(x => x.Category == ModCategory.Car);
            Tracks = mods.Count(x => x.Category == ModCategory.Track);
            Updates = await CountModUpdatesAsync(mods, cancellationToken);
        }
        finally { IsBusy = false; }
    }

    private async Task<int> CountModUpdatesAsync(IReadOnlyList<ModManifest> mods, CancellationToken cancellationToken)
    {
        // Cache-only catalog read: the dashboard never waits on the network.
        var cached = await _catalog.TryLoadCachedAsync(cancellationToken);
        if (cached is null) return 0;
        var count = 0;
        foreach (var mod in mods)
        {
            if (!mod.Metadata.TryGetValue("catalogId", out var catalogId)) continue;
            if (!mod.Metadata.TryGetValue("catalogVersion", out var installedVersion)) continue;
            var catalogMod = cached.Catalog.Mods.FirstOrDefault(x => x.Id.Equals(catalogId, StringComparison.OrdinalIgnoreCase));
            if (catalogMod is null || catalogMod.Status != CatalogModStatus.Published) continue;
            if (SemanticVersion.TryParse(installedVersion, out var installed)
                && SemanticVersion.TryParse(catalogMod.Version, out var available)
                && available > installed) count++;
        }
        return count;
    }

    private async Task LaunchAsync(object? _, CancellationToken cancellationToken)
    {
        if (_installation is null || !_installation.IsValid) return;
        await _launcher.LaunchAsync(_installation, _currentSettings.LaunchThroughSteam, cancellationToken);
    }

    private async Task BrowseAsync(object? _, CancellationToken cancellationToken)
    {
        var selected = _picker.PickGameFolder();
        if (selected is null) return;
        var installation = await _detector.ValidateManualPathAsync(selected, cancellationToken);
        if (!installation.IsValid) throw new InvalidOperationException(installation.ValidationMessage ?? _localization.Get("GameInvalid"));
        _currentSettings.GamePath = installation.RootPath;
        await _settings.SaveAsync(_currentSettings, cancellationToken);
        await LoadAsync(cancellationToken);
    }

    private void SetError(Exception exception)
    {
        HasError = true;
        _ = _errors.Handle(exception, _localization.Get("CommonError"));
        IsBusy = false;
    }
}
