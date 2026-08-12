using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.UI.Services;

namespace ACModHub.UI.ViewModels;

public sealed class InstallerViewModel : ObservableObject
{
    private readonly IModInstaller _installer;
    private readonly ISettingsService _settings;
    private readonly IUiErrorHandler _errors;
    private ModAnalysis? _analysis;
    private InstallStage _stage = InstallStage.Analyze;
    private double _progress;
    private string _statusText = "Analyzing package…";
    private bool _allowConflicts;
    private string _skinCarId = string.Empty;
    private bool _isBusy;
    private string? _error;

    public InstallerViewModel(IModInstaller installer, ISettingsService settings, IUiErrorHandler errors)
    {
        _installer = installer; _settings = settings; _errors = errors;
        InstallCommand = new AsyncRelayCommand(InstallAsync, _ => Analysis is not null && !IsBusy, SetError);
        CancelCommand = new RelayCommand(_ => CancelRequested?.Invoke());
    }

    public event Action<Guid>? InstallationCompleted;
    public event Action? CancelRequested;
    public ModAnalysis? Analysis { get => _analysis; private set { if (SetProperty(ref _analysis, value)) { OnPropertyChanged(nameof(Files)); OnPropertyChanged(nameof(Conflicts)); OnPropertyChanged(nameof(HasConflicts)); OnPropertyChanged(nameof(RequiresSkinTarget)); InstallCommand.RaiseCanExecuteChanged(); } } }
    public IReadOnlyList<PlannedFile> Files => Analysis?.Plan.Files ?? [];
    public IReadOnlyList<ModConflict> Conflicts => Analysis?.Conflicts ?? [];
    public bool HasConflicts => Conflicts.Count > 0;
    public bool RequiresSkinTarget => Files.Any(x => x.DestinationPath.Contains("/_select_car_/", StringComparison.OrdinalIgnoreCase));
    public string SkinCarId { get => _skinCarId; set => SetProperty(ref _skinCarId, value); }
    public InstallStage Stage { get => _stage; private set => SetProperty(ref _stage, value); }
    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public bool AllowConflicts { get => _allowConflicts; set => SetProperty(ref _allowConflicts, value); }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) InstallCommand.RaiseCanExecuteChanged(); } }
    public string? Error { get => _error; private set => SetProperty(ref _error, value); }
    public AsyncRelayCommand InstallCommand { get; }
    public RelayCommand CancelCommand { get; }

    public async Task InitializeAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        IsBusy = true; Error = null; Stage = InstallStage.Analyze; Progress = 0;
        try
        {
            var settings = await _settings.LoadAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(settings.GamePath)) throw new InvalidOperationException("Configure a valid Assetto Corsa path before importing mods.");
            Analysis = await _installer.AnalyzeAsync(archivePath, settings.GamePath, cancellationToken);
            Stage = InstallStage.Preview; Progress = 0.1; StatusText = "Review files and conflicts before installation.";
        }
        finally { IsBusy = false; }
    }

    private async Task InstallAsync(object? _, CancellationToken cancellationToken)
    {
        if (Analysis is null) return;
        IsBusy = true; Error = null;
        try
        {
            if (RequiresSkinTarget)
            {
                if (string.IsNullOrWhiteSpace(SkinCarId)) throw new InvalidOperationException("Enter the destination car folder name for this standalone skin.");
                Analysis = await _installer.ApplySkinTargetAsync(Analysis, SkinCarId.Trim(), cancellationToken);
                Stage = InstallStage.ConflictCheck;
                Progress = 0.12;
                StatusText = "Skin destination applied. Review the final paths and conflicts, then install again.";
                return;
            }
            var reporter = new Progress<InstallProgress>(value =>
            {
                Stage = value.Stage;
                Progress = value.Percentage;
                var count = value.Total > 0 ? $" ({value.Current:N0}/{value.Total:N0})" : string.Empty;
                StatusText = value.CurrentFile is null ? value.Message + count : $"{value.Message}{count} · {value.CurrentFile}";
            });
            var result = await _installer.InstallAsync(Analysis, new InstallOptions { AllowOverwriteConflicts = AllowConflicts, CreateBackup = true, VerifyAfterInstall = true }, reporter, cancellationToken);
            if (!result.Success) { Error = result.Error; Stage = InstallStage.Failed; return; }
            if (result.ModId.HasValue) InstallationCompleted?.Invoke(result.ModId.Value);
        }
        finally { IsBusy = false; }
    }
    private void SetError(Exception exception) { Error = _errors.Handle(exception, "Installation"); Stage = InstallStage.Failed; IsBusy = false; }
}
