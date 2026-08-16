using System.Collections.ObjectModel;
using System.Windows;
using ACModHub.Core;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ACModHub.UI.ViewModels;

public sealed class NavigationItem : ObservableObject
{
    public required string Key { get; init; }
    public required string IconKey { get; init; }
    public required string LabelKey { get; init; }
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

public sealed class NavigationGroup
{
    public required string LabelKey { get; init; }
    public required IReadOnlyList<NavigationItem> Items { get; init; }
}

public sealed class MainViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private readonly IFilePickerService _picker;
    private readonly ILocalizationService _localization;
    private readonly ISettingsService _settings;
    private readonly IUpdateChecker _updateChecker;
    private readonly IUiErrorHandler _errors;
    private readonly ILogger<MainViewModel> _logger;

    private object? _currentPage;
    private string _selectedPage = "Home";
    private string? _notification;
    private FlowDirection _flowDirection;
    private bool _downloadsHooked;
    private UpdateCheckResult? _availableUpdate;

    public MainViewModel(
        IServiceProvider services,
        IFilePickerService picker,
        ILocalizationService localization,
        ISettingsService settings,
        IUpdateChecker updateChecker,
        IUiErrorHandler errors,
        ILogger<MainViewModel> logger)
    {
        _services = services;
        _picker = picker;
        _localization = localization;
        _settings = settings;
        _updateChecker = updateChecker;
        _errors = errors;
        _logger = logger;
        _flowDirection = localization.FlowDirection;

        NavigateCommand = new AsyncRelayCommand(NavigateAsync, onError: SetError);
        ImportCommand = new AsyncRelayCommand(ImportAsync, onError: SetError);
        ToggleLanguageCommand = new AsyncRelayCommand(ToggleLanguageAsync, onError: SetError);
        OpenUpdatesCommand = new AsyncRelayCommand((_, token) => NavigateAsync("Updates", token), onError: SetError);
        DismissUpdateCommand = new AsyncRelayCommand(DismissUpdateAsync, onError: SetError);
        _localization.LanguageChanged += (_, _) =>
        {
            FlowDirection = _localization.FlowDirection;
            // Rebuild so converted labels re-resolve in the new language, keeping selection.
            BuildNavigation();
            SelectNavigationItem(SelectedPage);
        };
    }

    public object? CurrentPage { get => _currentPage; private set => SetProperty(ref _currentPage, value); }
    public string SelectedPage { get => _selectedPage; private set => SetProperty(ref _selectedPage, value); }
    public string? Notification { get => _notification; private set { if (SetProperty(ref _notification, value)) OnPropertyChanged(nameof(HasNotification)); } }
    public bool HasNotification => !string.IsNullOrWhiteSpace(Notification);
    public FlowDirection FlowDirection { get => _flowDirection; private set => SetProperty(ref _flowDirection, value); }
    public AsyncRelayCommand NavigateCommand { get; }
    public AsyncRelayCommand ImportCommand { get; }
    public AsyncRelayCommand ToggleLanguageCommand { get; }
    public AsyncRelayCommand OpenUpdatesCommand { get; }
    public AsyncRelayCommand DismissUpdateCommand { get; }

    public bool UpdateBannerVisible => _availableUpdate is { HasUpdate: true };
    public string UpdateBannerText => _availableUpdate?.HasUpdate == true
        ? _localization.Format("UpdateBannerTitle")
        : string.Empty;
    public string CurrentVersionText => AppInfo.Version;

    public ObservableCollection<NavigationGroup> NavigationGroups { get; } = [];

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        BuildNavigation();
        return NavigateAsync("Home", cancellationToken);
    }

    /// <summary>Starts the background update check. Fire-and-forget by design; never blocks the window.</summary>
    public void StartBackgroundUpdateCheck()
    {
        _ = CheckForUpdatesInBackgroundAsync();
    }

    public void NotifyUpdateApplied() => Notification = _localization.Get("NotifUpdateApplied");

    private void BuildNavigation()
    {
        NavigationGroups.Clear();
        NavigationGroups.Add(new NavigationGroup
        {
            LabelKey = "SecDiscover",
            Items = [Item("Home", "IconHome", "NavHome"), Item("Store", "IconStore", "NavStore")]
        });
        NavigationGroups.Add(new NavigationGroup
        {
            LabelKey = "SecLibrary",
            Items =
            [
                Item("AllMods", "IconLibrary", "NavAllMods"),
                Item("Cars", "IconCar", "NavCars"),
                Item("Tracks", "IconTrack", "NavTracks"),
                Item("Skins", "IconSkin", "NavSkins"),
                Item("Apps", "IconApp", "NavApps")
            ]
        });
        NavigationGroups.Add(new NavigationGroup
        {
            LabelKey = "SecSystem",
            Items =
            [
                Item("Downloads", "IconDownload", "NavDownloads"),
                Item("Updates", "IconUpdate", "NavUpdates"),
                Item("Backups", "IconBackup", "NavBackups"),
                Item("Settings", "IconSettings", "NavSettings")
            ]
        });
        NavigationItem Item(string key, string icon, string label) => new() { Key = key, IconKey = icon, LabelKey = label };
    }

    private async Task NavigateAsync(object? parameter, CancellationToken token)
    {
        var key = parameter?.ToString() ?? "Home";
        SelectedPage = key;
        SelectNavigationItem(key);
        Notification = null;

        switch (key)
        {
            case "Home":
                var dashboard = _services.GetRequiredService<DashboardViewModel>();
                CurrentPage = dashboard;
                await dashboard.LoadAsync(token);
                break;
            case "Store":
                var store = _services.GetRequiredService<CatalogViewModel>();
                store.PackageReady -= OnStorePackageReady;
                store.PackageReady += OnStorePackageReady;
                CurrentPage = store;
                await store.LoadAsync(token);
                break;
            case "AllMods":
            case "Cars":
            case "Tracks":
            case "Skins":
            case "Apps":
                await ShowLibraryAsync(key switch
                {
                    "Cars" => ModCategory.Car,
                    "Tracks" => ModCategory.Track,
                    "Skins" => ModCategory.Skin,
                    "Apps" => ModCategory.App,
                    _ => (ModCategory?)null
                }, token);
                break;
            case "Downloads":
                var downloads = _services.GetRequiredService<DownloadsViewModel>();
                if (!_downloadsHooked)
                {
                    downloads.PackageInstallRequested += path => ImportCommand.Execute(path);
                    _downloadsHooked = true;
                }
                CurrentPage = downloads;
                break;
            case "Updates":
                var updates = _services.GetRequiredService<UpdatesViewModel>();
                CurrentPage = updates;
                await updates.LoadAsync(token);
                break;
            case "Backups":
                var backups = _services.GetRequiredService<BackupsViewModel>();
                CurrentPage = backups;
                await backups.LoadAsync(token);
                break;
            case "Settings":
                var settings = _services.GetRequiredService<SettingsViewModel>();
                CurrentPage = settings;
                await settings.LoadAsync(token);
                break;
            default:
                await NavigateAsync("Home", token);
                break;
        }
    }

    private void SelectNavigationItem(string key)
    {
        foreach (var group in NavigationGroups)
            foreach (var item in group.Items)
                item.IsSelected = item.Key == key;
    }

    private async Task ShowLibraryAsync(ModCategory? category, CancellationToken token)
    {
        var library = _services.GetRequiredService<LibraryViewModel>();
        library.Configure(category);
        CurrentPage = library;
        await library.LoadAsync(token);
    }

    private void OnStorePackageReady(string path) => _ = ImportCommand.Execute(path);

    private async Task ToggleLanguageAsync(object? _, CancellationToken token)
    {
        var language = _localization.Language.StartsWith("fa", StringComparison.OrdinalIgnoreCase) ? "en-US" : "fa-IR";
        _localization.SetLanguage(language);
        var settings = await _settings.LoadAsync(token);
        settings.Language = language;
        await _settings.SaveAsync(settings, token);
        // Recreate the current page so every visible string re-resolves in the new language.
        await NavigateAsync(SelectedPage, token);
    }

    private async Task ImportAsync(object? parameter, CancellationToken token)
    {
        string? archive;
        IReadOnlyDictionary<string, string>? metadata;
        switch (parameter)
        {
            case StorePackage package:
                archive = package.Path;
                metadata = package.Metadata;
                break;
            case string path:
                archive = path;
                metadata = null;
                break;
            case string[] { Length: > 0 } files:
                archive = files[0];
                metadata = null;
                break;
            default:
                archive = _picker.PickArchive();
                metadata = null;
                break;
        }
        if (archive is null) return;
        var installer = _services.GetRequiredService<InstallerViewModel>();
        installer.CancelRequested -= OnInstallerCancelled;
        installer.CancelRequested += OnInstallerCancelled;
        installer.InstallationCompleted -= OnInstallationCompleted;
        installer.InstallationCompleted += OnInstallationCompleted;
        CurrentPage = installer;
        SelectedPage = "Installer";
        foreach (var group in NavigationGroups)
            foreach (var item in group.Items)
                item.IsSelected = false;
        await installer.InitializeAsync(archive, metadata, token);
    }

    private void OnInstallerCancelled() => _ = NavigateCommand.Execute("AllMods");
    private void OnInstallationCompleted(Guid _)
    {
        Notification = _localization.Get("NotifInstalled");
        _ = NavigateCommand.Execute("AllMods");
    }

    private async Task CheckForUpdatesInBackgroundAsync()
    {
        try
        {
            var settings = await _settings.LoadAsync().ConfigureAwait(false);
            if (!settings.CheckForUpdatesOnStartup) return;
            var channel = settings.UpdateChannel == "beta" ? UpdateChannel.Beta : UpdateChannel.Stable;
            var result = await _updateChecker.CheckAsync(channel, false).ConfigureAwait(false);
            if (!result.HasUpdate || result.Release is null) return;
            if (!string.IsNullOrWhiteSpace(settings.SkippedUpdateVersion) && result.Release.Version.ToString() == settings.SkippedUpdateVersion) return;
            _availableUpdate = result;
            OnPropertyChanged(nameof(UpdateBannerVisible));
            OnPropertyChanged(nameof(UpdateBannerText));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Background update check failed");
        }
    }

    private async Task DismissUpdateAsync(object? _, CancellationToken token)
    {
        if (_availableUpdate?.Release is not { } release) return;
        var settings = await _settings.LoadAsync(token);
        settings.SkippedUpdateVersion = release.Version.ToString();
        await _settings.SaveAsync(settings, token);
        _availableUpdate = null;
        OnPropertyChanged(nameof(UpdateBannerVisible));
    }

    private void SetError(Exception exception) => Notification = _errors.Handle(exception, _localization.Get("CommonError"));
}
