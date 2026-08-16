using System.Collections.ObjectModel;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.UI.Services;

namespace ACModHub.UI.ViewModels;

public sealed class BackupsViewModel : ObservableObject
{
    private readonly IBackupService _backups;
    private readonly ISettingsService _settings;
    private readonly ILocalizationService _localization;
    private readonly IUiErrorHandler _errors;
    private BackupDescriptor? _selected;
    private string? _message;
    private bool _isBusy;

    public BackupsViewModel(IBackupService backups, ISettingsService settings, ILocalizationService localization, IUiErrorHandler errors)
    {
        _backups = backups;
        _settings = settings;
        _localization = localization;
        _errors = errors;
        RefreshCommand = new AsyncRelayCommand((_, token) => LoadAsync(token), onError: SetError);
        RestoreCommand = new AsyncRelayCommand(RestoreAsync, _ => Selected is not null, SetError);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, _ => Selected is not null, SetError);
    }

    public ObservableCollection<BackupDescriptor> Backups { get; } = [];
    public BackupDescriptor? Selected { get => _selected; set { if (SetProperty(ref _selected, value)) { RestoreCommand.RaiseCanExecuteChanged(); DeleteCommand.RaiseCanExecuteChanged(); } } }
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public bool IsEmpty => !IsBusy && Backups.Count == 0;
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand RestoreCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            Backups.Clear();
            foreach (var backup in await _backups.GetAllAsync(cancellationToken)) Backups.Add(backup);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private async Task RestoreAsync(object? _, CancellationToken token)
    {
        var settings = await _settings.LoadAsync(token);
        if (string.IsNullOrWhiteSpace(settings.GamePath)) throw new InvalidOperationException(_localization.Get("DashNotConfigured"));
        await _backups.RestoreAsync(Selected!, settings.GamePath, token);
        Message = _localization.Get("BackupRestored");
    }

    private async Task DeleteAsync(object? _, CancellationToken token)
    {
        await _backups.DeleteAsync(Selected!.Id, token);
        Selected = null;
        Message = _localization.Get("BackupDeleted");
        await LoadAsync(token);
    }

    private void SetError(Exception exception) => Message = _errors.Handle(exception, _localization.Get("BackupsTitle"));
}
