using System.Collections.ObjectModel;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.UI.Services;

namespace ACModHub.UI.ViewModels;

public sealed class LibraryViewModel : ObservableObject
{
    private readonly IModRepository _repository;
    private readonly IModInstaller _installer;
    private readonly IManifestService _manifests;
    private readonly ISettingsService _settings;
    private readonly IFilePickerService _picker;
    private readonly ILocalizationService _localization;
    private readonly IUiErrorHandler _errors;

    private IReadOnlyList<ModManifest> _all = [];
    private string _search = string.Empty;
    private ModCategory? _category;
    private ModStatus? _status;
    private SortMode _sort = SortMode.Name;
    private ModManifest? _selectedMod;
    private string? _message;
    private bool _isBusy;
    private UninstallAnalysis? _uninstallAnalysis;
    private VerificationResult? _verification;

    public LibraryViewModel(
        IModRepository repository,
        IModInstaller installer,
        IManifestService manifests,
        ISettingsService settings,
        IFilePickerService picker,
        ILocalizationService localization,
        IUiErrorHandler errors)
    {
        _repository = repository;
        _installer = installer;
        _manifests = manifests;
        _settings = settings;
        _picker = picker;
        _localization = localization;
        _errors = errors;
        RefreshCommand = new AsyncRelayCommand((_, token) => LoadAsync(token), onError: SetError);
        EnableCommand = new AsyncRelayCommand(EnableAsync, _ => SelectedMod is not null, SetError);
        DisableCommand = new AsyncRelayCommand(DisableAsync, _ => SelectedMod is not null, SetError);
        RepairCommand = new AsyncRelayCommand(RepairAsync, _ => SelectedMod is not null, SetError);
        ReinstallCommand = new AsyncRelayCommand(ReinstallAsync, _ => SelectedMod is not null, SetError);
        UpdateCommand = new AsyncRelayCommand(UpdateAsync, _ => SelectedMod is not null, SetError);
        UninstallCommand = new AsyncRelayCommand(AnalyzeUninstallAsync, _ => SelectedMod is not null, SetError);
        ConfirmUninstallCommand = new AsyncRelayCommand(UninstallAsync, _ => _uninstallAnalysis?.CanUninstall == true && !RequiresUninstallDecision, SetError);
        PreserveAndUninstallCommand = new AsyncRelayCommand(PreserveAndUninstallAsync, _ => _uninstallAnalysis?.CanUninstall == true, SetError);
        RestoreAndUninstallCommand = new AsyncRelayCommand(RestoreAndUninstallAsync, _ => _uninstallAnalysis?.CanUninstall == true, SetError);
        AbortUninstallCommand = new AsyncRelayCommand((_, _) => { _uninstallAnalysis = null; OnPropertyChanged(nameof(ShowUninstallDecision)); return Task.CompletedTask; });
        VerifyCommand = new AsyncRelayCommand(VerifyAsync, _ => SelectedMod is not null, SetError);
    }

    public ObservableCollection<ModManifest> Mods { get; } = [];
    public ObservableCollection<string> SelectedModFiles { get; } = [];
    public ObservableCollection<VerificationIssue> VerificationIssues { get; } = [];
    public IReadOnlyList<ModCategory> Categories { get; } = Enum.GetValues<ModCategory>();
    public IReadOnlyList<ModStatus> Statuses { get; } = Enum.GetValues<ModStatus>();
    public IReadOnlyList<SortMode> SortModes { get; } = Enum.GetValues<SortMode>();

    public string Search { get => _search; set { if (SetProperty(ref _search, value)) Apply(); } }
    public ModCategory? Category { get => _category; set { if (SetProperty(ref _category, value)) Apply(); } }
    public ModStatus? Status { get => _status; set { if (SetProperty(ref _status, value)) Apply(); } }
    public SortMode Sort { get => _sort; set { if (SetProperty(ref _sort, value)) Apply(); } }
    public ModManifest? SelectedMod
    {
        get => _selectedMod;
        set
        {
            if (!SetProperty(ref _selectedMod, value)) return;
            _uninstallAnalysis = null;
            _verification = null;
            VerificationIssues.Clear();
            SelectedModFiles.Clear();
            if (value is not null)
            {
                foreach (var file in value.Files.Take(200)) SelectedModFiles.Add(file.RelativePath);
            }
            RaiseActions();
            OnPropertyChanged(nameof(ShowUninstallDecision));
            OnPropertyChanged(nameof(VerificationText));
        }
    }
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public bool IsEmpty => !IsBusy && Mods.Count == 0;

    public bool ShowUninstallDecision => _uninstallAnalysis is not null;
    public bool RequiresUninstallDecision => _uninstallAnalysis is { RequiresUserDecision: true };
    public bool UninstallBlocked => _uninstallAnalysis is { CanUninstall: false };
    public string UninstallDecisionText
    {
        get
        {
            if (_uninstallAnalysis is null) return string.Empty;
            if (!_uninstallAnalysis.CanUninstall)
                return _localization.Get("UninstallBlockedHint") + string.Join(", ", _uninstallAnalysis.BlockingNewerModIds);
            return _uninstallAnalysis.RequiresUserDecision
                ? _localization.Get("UninstallModifiedHint")
                : _localization.Get("ConfirmDestructive");
        }
    }
    public string VerificationText => _verification is null
        ? string.Empty
        : _verification.IsHealthy
            ? _localization.Get("LibraryHealthy")
            : _localization.Format("LibraryDamaged", _verification.Issues.Count);

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand EnableCommand { get; }
    public AsyncRelayCommand DisableCommand { get; }
    public AsyncRelayCommand RepairCommand { get; }
    public AsyncRelayCommand ReinstallCommand { get; }
    public AsyncRelayCommand UpdateCommand { get; }
    public AsyncRelayCommand UninstallCommand { get; }
    public AsyncRelayCommand ConfirmUninstallCommand { get; }
    public AsyncRelayCommand PreserveAndUninstallCommand { get; }
    public AsyncRelayCommand RestoreAndUninstallCommand { get; }
    public AsyncRelayCommand AbortUninstallCommand { get; }
    public AsyncRelayCommand VerifyCommand { get; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            _all = await _repository.GetAllAsync(cancellationToken);
            Apply();
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public void Configure(ModCategory? category) => Category = category;

    private void Apply()
    {
        IEnumerable<ModManifest> query = _all;
        if (!string.IsNullOrWhiteSpace(Search))
            query = query.Where(x => x.Name.Contains(Search, StringComparison.CurrentCultureIgnoreCase) || x.Author.Contains(Search, StringComparison.CurrentCultureIgnoreCase));
        if (Category.HasValue) query = query.Where(x => x.Category == Category);
        if (Status.HasValue) query = query.Where(x => x.Status == Status);
        query = Sort switch
        {
            SortMode.Author => query.OrderBy(x => x.Author),
            SortMode.Version => query.OrderBy(x => x.Version),
            SortMode.Size => query.OrderByDescending(x => x.Size),
            SortMode.InstalledDate => query.OrderByDescending(x => x.InstalledAt),
            _ => query.OrderBy(x => x.Name)
        };
        Mods.Clear();
        foreach (var item in query) Mods.Add(item);
        OnPropertyChanged(nameof(IsEmpty));
    }

    private async Task EnableAsync(object? _, CancellationToken token) { await _installer.EnableAsync(SelectedMod!.Id, token); await LoadAsync(token); }
    private async Task DisableAsync(object? _, CancellationToken token) { await _installer.DisableAsync(SelectedMod!.Id, token); await LoadAsync(token); }
    private async Task RepairAsync(object? _, CancellationToken token)
    {
        var result = await _installer.RepairAsync(SelectedMod!.Id, cancellationToken: token);
        Message = result.IsHealthy ? _localization.Get("LibraryHealthy") : _localization.Format("LibraryDamaged", result.Issues.Count);
        await LoadAsync(token);
    }
    private async Task ReinstallAsync(object? _, CancellationToken token)
    {
        var result = await _installer.ReinstallAsync(SelectedMod!.Id, cancellationToken: token);
        Message = result.Success ? _localization.Get("LibraryHealthy") : result.Error ?? _localization.Get("CommonError");
        await LoadAsync(token);
    }
    private async Task UpdateAsync(object? _, CancellationToken token)
    {
        var archive = _picker.PickArchive();
        if (archive is null) return;
        var result = await _installer.UpdateAsync(SelectedMod!.Id, archive, new InstallOptions { AllowOverwriteConflicts = true }, cancellationToken: token);
        Message = result.Success ? _localization.Get("LibraryHealthy") : result.Error ?? _localization.Get("CommonError");
        await LoadAsync(token);
    }

    private async Task AnalyzeUninstallAsync(object? _, CancellationToken token)
    {
        _uninstallAnalysis = await _installer.AnalyzeUninstallAsync(SelectedMod!.Id, token);
        OnPropertyChanged(nameof(ShowUninstallDecision));
        OnPropertyChanged(nameof(RequiresUninstallDecision));
        OnPropertyChanged(nameof(UninstallBlocked));
        OnPropertyChanged(nameof(UninstallDecisionText));
        RaiseActions();
    }

    private async Task UninstallAsync(object? _, CancellationToken token)
    {
        await _installer.UninstallAsync(SelectedMod!.Id, token);
        _uninstallAnalysis = null;
        SelectedMod = null;
        Message = _localization.Get("UninstallDone");
        await LoadAsync(token);
    }

    private async Task PreserveAndUninstallAsync(object? _, CancellationToken token)
    {
        await _installer.UninstallAsync(SelectedMod!.Id, new UninstallOptions { ModifiedFileAction = ModifiedFileAction.Preserve }, token);
        _uninstallAnalysis = null;
        SelectedMod = null;
        Message = _localization.Get("ModifiedPreservedDone");
        await LoadAsync(token);
    }

    private async Task RestoreAndUninstallAsync(object? _, CancellationToken token)
    {
        await _installer.UninstallAsync(SelectedMod!.Id, new UninstallOptions { ModifiedFileAction = ModifiedFileAction.RestoreOrDelete }, token);
        _uninstallAnalysis = null;
        SelectedMod = null;
        Message = _localization.Get("ModifiedRestoredDone");
        await LoadAsync(token);
    }

    private async Task VerifyAsync(object? _, CancellationToken token)
    {
        var settings = await _settings.LoadAsync(token);
        if (string.IsNullOrWhiteSpace(settings.GamePath)) throw new InvalidOperationException(_localization.Get("DashNotConfigured"));
        _verification = await _manifests.VerifyAsync(SelectedMod!, settings.GamePath, token);
        VerificationIssues.Clear();
        foreach (var issue in _verification.Issues) VerificationIssues.Add(issue);
        OnPropertyChanged(nameof(VerificationText));
    }

    private void RaiseActions()
    {
        EnableCommand.RaiseCanExecuteChanged();
        DisableCommand.RaiseCanExecuteChanged();
        RepairCommand.RaiseCanExecuteChanged();
        ReinstallCommand.RaiseCanExecuteChanged();
        UpdateCommand.RaiseCanExecuteChanged();
        UninstallCommand.RaiseCanExecuteChanged();
        ConfirmUninstallCommand.RaiseCanExecuteChanged();
        PreserveAndUninstallCommand.RaiseCanExecuteChanged();
        RestoreAndUninstallCommand.RaiseCanExecuteChanged();
        VerifyCommand.RaiseCanExecuteChanged();
    }

    private void SetError(Exception ex) => Message = _errors.Handle(ex, _localization.Get("LibraryTitle"));
}
