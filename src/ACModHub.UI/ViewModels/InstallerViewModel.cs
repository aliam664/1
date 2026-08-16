using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.UI.Services;

namespace ACModHub.UI.ViewModels;

public sealed class InstallerViewModel : ObservableObject
{
    private readonly IModInstaller _installer;
    private readonly ISettingsService _settings;
    private readonly ILocalizationService _localization;
    private readonly IUiErrorHandler _errors;
    private ModAnalysis? _analysis;
    private IReadOnlyDictionary<string, string>? _catalogMetadata;
    private InstallStage _stage = InstallStage.Analyze;
    private double _progress;
    private string _statusText = string.Empty;
    private bool _allowConflicts;
    private string _skinCarId = string.Empty;
    private bool _isBusy;
    private string? _error;
    private bool _completed;

    public InstallerViewModel(IModInstaller installer, ISettingsService settings, ILocalizationService localization, IUiErrorHandler errors)
    {
        _installer = installer;
        _settings = settings;
        _localization = localization;
        _errors = errors;
        InstallCommand = new AsyncRelayCommand(InstallAsync, _ => Analysis is not null && !IsBusy && !Completed, SetError);
        CancelCommand = new RelayCommand(Cancel);
    }

    public event Action<Guid>? InstallationCompleted;
    public event Action? CancelRequested;
    public ModAnalysis? Analysis { get => _analysis; private set { if (SetProperty(ref _analysis, value)) { OnPropertyChanged(nameof(Files)); OnPropertyChanged(nameof(Conflicts)); OnPropertyChanged(nameof(Warnings)); OnPropertyChanged(nameof(HasConflicts)); OnPropertyChanged(nameof(RequiresSkinTarget)); OnPropertyChanged(nameof(SuggestedName)); OnPropertyChanged(nameof(CategoryText)); InstallCommand.RaiseCanExecuteChanged(); } } }
    public IReadOnlyList<PlannedFile> Files => Analysis?.Plan.Files ?? [];
    public IReadOnlyList<ModConflict> Conflicts => Analysis?.Conflicts ?? [];
    public IReadOnlyList<string> Warnings => Analysis?.Plan.Warnings ?? [];
    public bool HasConflicts => Conflicts.Count > 0;
    public bool RequiresSkinTarget => Files.Any(x => x.DestinationPath.Contains("/_select_car_/", StringComparison.OrdinalIgnoreCase));
    public string SuggestedName => Analysis?.Plan.SuggestedName ?? string.Empty;
    public string CategoryText => Analysis is null ? string.Empty : _localization.Get("Category" + Analysis.Plan.Category);
    public string SkinCarId { get => _skinCarId; set => SetProperty(ref _skinCarId, value); }
    public InstallStage Stage { get => _stage; private set => SetProperty(ref _stage, value); }
    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public bool AllowConflicts { get => _allowConflicts; set => SetProperty(ref _allowConflicts, value); }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) { InstallCommand.RaiseCanExecuteChanged(); CancelCommand.RaiseCanExecuteChanged(); } } }
    public bool Completed { get => _completed; private set => SetProperty(ref _completed, value); }
    public string? Error { get => _error; private set => SetProperty(ref _error, value); }
    public AsyncRelayCommand InstallCommand { get; }
    public RelayCommand CancelCommand { get; }

    public async Task InitializeAsync(string archivePath, IReadOnlyDictionary<string, string>? catalogMetadata = null, CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        Error = null;
        Completed = false;
        Stage = InstallStage.Analyze;
        Progress = 0;
        StatusText = _localization.Get("StageAnalyze");
        _catalogMetadata = catalogMetadata;
        try
        {
            var settings = await _settings.LoadAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(settings.GamePath))
                throw new InvalidOperationException(_localization.Get("DashNotConfigured"));
            Analysis = await _installer.AnalyzeAsync(archivePath, settings.GamePath, cancellationToken);
            Stage = InstallStage.Preview;
            Progress = 0.1;
            StatusText = _localization.Get("StagePreview");
        }
        finally { IsBusy = false; }
    }

    private void Cancel(object? _)
    {
        if (InstallCommand.IsRunning) InstallCommand.Cancel();
        else CancelRequested?.Invoke();
    }

    private async Task InstallAsync(object? _, CancellationToken cancellationToken)
    {
        if (Analysis is null) return;
        IsBusy = true;
        Error = null;
        try
        {
            if (RequiresSkinTarget)
            {
                if (string.IsNullOrWhiteSpace(SkinCarId))
                    throw new InvalidOperationException(_localization.Get("SkinCarTarget"));
                Analysis = await _installer.ApplySkinTargetAsync(Analysis, SkinCarId.Trim(), cancellationToken);
                Stage = InstallStage.ConflictCheck;
                Progress = 0.12;
                StatusText = _localization.Get("StageConflictCheck");
                return;
            }

            // Carry catalog provenance into the installed manifest (id/version/author/source).
            if (_catalogMetadata is { Count: > 0 })
                foreach (var pair in _catalogMetadata)
                    Analysis.Plan.Metadata[pair.Key] = pair.Value;

            var reporter = new Progress<InstallProgress>(value =>
            {
                Stage = value.Stage;
                Progress = value.Percentage;
                var count = value.Total > 0 ? $" ({value.Current:N0}/{value.Total:N0})" : string.Empty;
                StatusText = _localization.Get("Stage" + value.Stage) + count + (value.CurrentFile is null ? string.Empty : " · " + value.CurrentFile);
            });
            var result = await _installer.InstallAsync(Analysis, new InstallOptions { AllowOverwriteConflicts = AllowConflicts, CreateBackup = true, VerifyAfterInstall = true }, reporter, cancellationToken);
            if (!result.Success)
            {
                Error = result.Error ?? _localization.Get("CommonError");
                Stage = InstallStage.Failed;
                return;
            }
            Completed = true;
            StatusText = _localization.Get("InstallerCompleted");
            if (result.ModId.HasValue) InstallationCompleted?.Invoke(result.ModId.Value);
        }
        catch (OperationCanceledException)
        {
            Stage = InstallStage.RollingBack;
            StatusText = _localization.Get("InstallerCancelled");
        }
        finally { IsBusy = false; }
    }

    private void SetError(Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            Stage = InstallStage.RollingBack;
            StatusText = _localization.Get("InstallerCancelled");
            IsBusy = false;
            return;
        }
        Error = _errors.Handle(exception, _localization.Get("InstallerTitle"));
        Stage = InstallStage.Failed;
        IsBusy = false;
    }
}
