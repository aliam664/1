using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.UI.Services;

namespace ACModHub.UI.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IGameDetector _detector;
    private readonly IModRepository _repository;
    private readonly ILaunchService _launcher;
    private readonly IFilePickerService _picker;
    private AppSettings _currentSettings = new();
    private GameInstallation? _installation;
    private string _gamePath = "—";
    private string _gameStatus = "Not detected";
    private int _installedMods;
    private int _cars;
    private int _tracks;
    private int _updates;
    private bool _isBusy;
    private string? _error;

    public DashboardViewModel(ISettingsService settings, IGameDetector detector, IModRepository repository, ILaunchService launcher, IFilePickerService picker)
    {
        _settings = settings; _detector = detector; _repository = repository; _launcher = launcher; _picker = picker;
        RefreshCommand = new AsyncRelayCommand((_, token) => LoadAsync(token), onError: SetError);
        LaunchCommand = new AsyncRelayCommand(LaunchAsync, _ => _installation?.IsValid == true, SetError);
        BrowseCommand = new AsyncRelayCommand(BrowseAsync, onError: SetError);
    }

    public string GamePath { get => _gamePath; private set => SetProperty(ref _gamePath, value); }
    public string GameStatus { get => _gameStatus; private set => SetProperty(ref _gameStatus, value); }
    public int InstalledMods { get => _installedMods; private set => SetProperty(ref _installedMods, value); }
    public int Cars { get => _cars; private set => SetProperty(ref _cars, value); }
    public int Tracks { get => _tracks; private set => SetProperty(ref _tracks, value); }
    public int Updates { get => _updates; private set => SetProperty(ref _updates, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public string? Error { get => _error; private set => SetProperty(ref _error, value); }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand LaunchCommand { get; }
    public AsyncRelayCommand BrowseCommand { get; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true; Error = null;
        try
        {
            _currentSettings = await _settings.LoadAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(_currentSettings.GamePath))
                _installation = await _detector.ValidateManualPathAsync(_currentSettings.GamePath, cancellationToken);
            if (_installation?.IsValid != true)
                _installation = (await _detector.DetectAsync(cancellationToken)).FirstOrDefault(x => x.IsValid);
            if (_installation is not null)
            {
                GamePath = _installation.RootPath;
                GameStatus = _installation.IsValid ? "Ready" : _installation.ValidationMessage ?? "Invalid";
                if (_installation.IsValid && _currentSettings.GamePath != _installation.RootPath)
                {
                    _currentSettings.GamePath = _installation.RootPath;
                    await _settings.SaveAsync(_currentSettings, cancellationToken);
                }
            }
            var mods = await _repository.GetAllAsync(cancellationToken);
            InstalledMods = mods.Count;
            Cars = mods.Count(x => x.Category == ModCategory.Car);
            Tracks = mods.Count(x => x.Category == ModCategory.Track);
            Updates = 0;
            LaunchCommand.RaiseCanExecuteChanged();
        }
        finally { IsBusy = false; }
    }

    private async Task BrowseAsync(object? _, CancellationToken cancellationToken)
    {
        var selected = _picker.PickGameFolder();
        if (selected is null) return;
        var installation = await _detector.ValidateManualPathAsync(selected, cancellationToken);
        if (!installation.IsValid) throw new InvalidOperationException(installation.ValidationMessage);
        _currentSettings.GamePath = installation.RootPath;
        await _settings.SaveAsync(_currentSettings, cancellationToken);
        await LoadAsync(cancellationToken);
    }

    private Task LaunchAsync(object? _, CancellationToken cancellationToken) => _launcher.LaunchAsync(_installation!, _currentSettings.LaunchThroughSteam, cancellationToken);
    private void SetError(Exception exception) => Error = exception.Message;
}
