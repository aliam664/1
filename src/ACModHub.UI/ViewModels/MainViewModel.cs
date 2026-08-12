using System.Windows;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ACModHub.UI.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private readonly IFilePickerService _picker;
    private readonly ILocalizationService _localization;
    private readonly ISettingsService _settings;
    private object? _currentPage;
    private string _selectedPage = "Dashboard";
    private string? _notification;
    private FlowDirection _flowDirection;

    public MainViewModel(IServiceProvider services, IFilePickerService picker, ILocalizationService localization, ISettingsService settings)
    {
        _services = services; _picker = picker; _localization = localization; _settings = settings; _flowDirection = localization.FlowDirection;
        NavigateCommand = new AsyncRelayCommand(NavigateAsync, onError: SetError);
        ImportCommand = new AsyncRelayCommand(ImportAsync, onError: SetError);
        ToggleLanguageCommand = new AsyncRelayCommand(ToggleLanguageAsync, onError: SetError);
        _localization.LanguageChanged += (_, _) => FlowDirection = _localization.FlowDirection;
    }

    public object? CurrentPage { get => _currentPage; private set => SetProperty(ref _currentPage, value); }
    public string SelectedPage { get => _selectedPage; private set => SetProperty(ref _selectedPage, value); }
    public string? Notification { get => _notification; private set => SetProperty(ref _notification, value); }
    public FlowDirection FlowDirection { get => _flowDirection; private set => SetProperty(ref _flowDirection, value); }
    public AsyncRelayCommand NavigateCommand { get; }
    public AsyncRelayCommand ImportCommand { get; }
    public AsyncRelayCommand ToggleLanguageCommand { get; }

    public Task InitializeAsync(CancellationToken cancellationToken = default) => ShowDashboardAsync(cancellationToken);

    private async Task NavigateAsync(object? parameter, CancellationToken token)
    {
        var key = parameter?.ToString() ?? "Dashboard";
        SelectedPage = key; Notification = null;
        switch (key)
        {
            case "Dashboard": await ShowDashboardAsync(token); break;
            case "Mods": await ShowLibraryAsync(null, token); break;
            case "Cars": await ShowLibraryAsync(ModCategory.Car, token); break;
            case "Tracks": await ShowLibraryAsync(ModCategory.Track, token); break;
            case "Skins": await ShowLibraryAsync(ModCategory.Skin, token); break;
            case "Apps": await ShowLibraryAsync(ModCategory.App, token); break;
            case "Downloads": CurrentPage = _services.GetRequiredService<DownloadsViewModel>(); break;
            case "Updates": CurrentPage = _services.GetRequiredService<UpdatesViewModel>(); break;
            case "Backups": var backups = _services.GetRequiredService<BackupsViewModel>(); CurrentPage = backups; await backups.LoadAsync(token); break;
            case "Settings": var settings = _services.GetRequiredService<SettingsViewModel>(); CurrentPage = settings; await settings.LoadAsync(token); break;
            default: await ShowDashboardAsync(token); break;
        }
    }

    private async Task ShowDashboardAsync(CancellationToken token)
    {
        var dashboard = _services.GetRequiredService<DashboardViewModel>(); CurrentPage = dashboard; await dashboard.LoadAsync(token);
    }
    private async Task ShowLibraryAsync(ModCategory? category, CancellationToken token)
    {
        var library = _services.GetRequiredService<LibraryViewModel>(); library.Configure(category); CurrentPage = library; await library.LoadAsync(token);
    }
    private async Task ToggleLanguageAsync(object? _, CancellationToken token)
    {
        var language = _localization.Language.StartsWith("fa", StringComparison.OrdinalIgnoreCase) ? "en-US" : "fa-IR";
        _localization.SetLanguage(language);
        var settings = await _settings.LoadAsync(token);
        settings.Language = language;
        await _settings.SaveAsync(settings, token);
    }

    private async Task ImportAsync(object? parameter, CancellationToken token)
    {
        var archive = parameter is string[] files && files.Length > 0 ? files[0] : _picker.PickArchive();
        if (archive is null) return;
        var installer = _services.GetRequiredService<InstallerViewModel>();
        installer.CancelRequested += () => NavigateCommand.Execute("Mods");
        installer.InstallationCompleted += _ => { Notification = "Mod installed and verified."; NavigateCommand.Execute("Mods"); };
        CurrentPage = installer; SelectedPage = "Installer";
        await installer.InitializeAsync(archive, token);
    }
    private void SetError(Exception exception) => Notification = exception.Message;
}
