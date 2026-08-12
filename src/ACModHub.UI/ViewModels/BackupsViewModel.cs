using System.Collections.ObjectModel;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;

namespace ACModHub.UI.ViewModels;

public sealed class BackupsViewModel : ObservableObject
{
    private readonly IBackupService _backups;
    private readonly ISettingsService _settings;
    private BackupDescriptor? _selected;
    private string? _message;
    public BackupsViewModel(IBackupService backups, ISettingsService settings)
    {
        _backups = backups; _settings = settings;
        RefreshCommand = new AsyncRelayCommand((_, token) => LoadAsync(token), onError: SetError);
        RestoreCommand = new AsyncRelayCommand(RestoreAsync, _ => Selected is not null, SetError);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, _ => Selected is not null, SetError);
    }
    public ObservableCollection<BackupDescriptor> Backups { get; } = [];
    public BackupDescriptor? Selected { get => _selected; set { if (SetProperty(ref _selected, value)) { RestoreCommand.RaiseCanExecuteChanged(); DeleteCommand.RaiseCanExecuteChanged(); } } }
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand RestoreCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public async Task LoadAsync(CancellationToken cancellationToken = default) { Backups.Clear(); foreach (var backup in await _backups.GetAllAsync(cancellationToken)) Backups.Add(backup); }
    private async Task RestoreAsync(object? _, CancellationToken token) { var settings = await _settings.LoadAsync(token); if (string.IsNullOrWhiteSpace(settings.GamePath)) throw new InvalidOperationException("Game path is not configured."); await _backups.RestoreAsync(Selected!, settings.GamePath, token); Message = "Backup restored."; }
    private async Task DeleteAsync(object? _, CancellationToken token) { await _backups.DeleteAsync(Selected!.Id, token); Selected = null; await LoadAsync(token); }
    private void SetError(Exception exception) => Message = exception.Message;
}
