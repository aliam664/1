using System.Collections.ObjectModel;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;

namespace ACModHub.UI.ViewModels;

public sealed class LibraryViewModel : ObservableObject
{
    private readonly IModRepository _repository;
    private readonly IModInstaller _installer;
    private readonly IManifestService _manifests;
    private readonly ISettingsService _settings;
    private IReadOnlyList<ModManifest> _all = [];
    private string _search = string.Empty;
    private ModCategory? _category;
    private ModStatus? _status;
    private SortMode _sort = SortMode.Name;
    private ModManifest? _selectedMod;
    private string? _message;
    private bool _isBusy;

    public LibraryViewModel(IModRepository repository, IModInstaller installer, IManifestService manifests, ISettingsService settings)
    {
        _repository = repository; _installer = installer; _manifests = manifests; _settings = settings;
        RefreshCommand = new AsyncRelayCommand((_, token) => LoadAsync(token), onError: SetError);
        EnableCommand = new AsyncRelayCommand(EnableAsync, _ => SelectedMod is not null, SetError);
        DisableCommand = new AsyncRelayCommand(DisableAsync, _ => SelectedMod is not null, SetError);
        RepairCommand = new AsyncRelayCommand(RepairAsync, _ => SelectedMod is not null, SetError);
        ReinstallCommand = new AsyncRelayCommand(ReinstallAsync, _ => SelectedMod is not null, SetError);
        UninstallCommand = new AsyncRelayCommand(UninstallAsync, _ => SelectedMod is not null, SetError);
        VerifyCommand = new AsyncRelayCommand(VerifyAsync, _ => SelectedMod is not null, SetError);
    }

    public ObservableCollection<ModManifest> Mods { get; } = [];
    public IReadOnlyList<ModCategory> Categories { get; } = Enum.GetValues<ModCategory>();
    public IReadOnlyList<ModStatus> Statuses { get; } = Enum.GetValues<ModStatus>();
    public IReadOnlyList<SortMode> SortModes { get; } = Enum.GetValues<SortMode>();
    public string Search { get => _search; set { if (SetProperty(ref _search, value)) Apply(); } }
    public ModCategory? Category { get => _category; set { if (SetProperty(ref _category, value)) Apply(); } }
    public ModStatus? Status { get => _status; set { if (SetProperty(ref _status, value)) Apply(); } }
    public SortMode Sort { get => _sort; set { if (SetProperty(ref _sort, value)) Apply(); } }
    public ModManifest? SelectedMod { get => _selectedMod; set { if (SetProperty(ref _selectedMod, value)) RaiseActions(); } }
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand EnableCommand { get; }
    public AsyncRelayCommand DisableCommand { get; }
    public AsyncRelayCommand RepairCommand { get; }
    public AsyncRelayCommand ReinstallCommand { get; }
    public AsyncRelayCommand UninstallCommand { get; }
    public AsyncRelayCommand VerifyCommand { get; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try { _all = await _repository.GetAllAsync(cancellationToken); Apply(); }
        finally { IsBusy = false; }
    }

    public void Configure(ModCategory? category) { Category = category; }

    private void Apply()
    {
        IEnumerable<ModManifest> query = _all;
        if (!string.IsNullOrWhiteSpace(Search)) query = query.Where(x => x.Name.Contains(Search, StringComparison.CurrentCultureIgnoreCase) || x.Author.Contains(Search, StringComparison.CurrentCultureIgnoreCase));
        if (Category.HasValue) query = query.Where(x => x.Category == Category);
        if (Status.HasValue) query = query.Where(x => x.Status == Status);
        query = Sort switch
        {
            SortMode.Author => query.OrderBy(x => x.Author), SortMode.Version => query.OrderBy(x => x.Version),
            SortMode.Size => query.OrderByDescending(x => x.Size), SortMode.InstalledDate => query.OrderByDescending(x => x.InstalledAt),
            _ => query.OrderBy(x => x.Name)
        };
        Mods.Clear(); foreach (var item in query) Mods.Add(item);
    }

    private async Task EnableAsync(object? _, CancellationToken token) { await _installer.EnableAsync(SelectedMod!.Id, token); await LoadAsync(token); }
    private async Task DisableAsync(object? _, CancellationToken token) { await _installer.DisableAsync(SelectedMod!.Id, token); await LoadAsync(token); }
    private async Task RepairAsync(object? _, CancellationToken token) { var result = await _installer.RepairAsync(SelectedMod!.Id, cancellationToken: token); Message = result.IsHealthy ? "Mod repaired and verified." : $"{result.Issues.Count} issue(s) remain."; await LoadAsync(token); }
    private async Task ReinstallAsync(object? _, CancellationToken token) { var result = await _installer.ReinstallAsync(SelectedMod!.Id, cancellationToken: token); Message = result.Success ? "Mod reinstalled." : result.Error; await LoadAsync(token); }
    private async Task UninstallAsync(object? _, CancellationToken token) { await _installer.UninstallAsync(SelectedMod!.Id, token); SelectedMod = null; Message = "Mod uninstalled safely."; await LoadAsync(token); }
    private async Task VerifyAsync(object? _, CancellationToken token)
    {
        var settings = await _settings.LoadAsync(token);
        if (string.IsNullOrWhiteSpace(settings.GamePath)) throw new InvalidOperationException("Game path is not configured.");
        var result = await _manifests.VerifyAsync(SelectedMod!, settings.GamePath, token);
        Message = result.IsHealthy ? "SHA-256 verification passed." : $"Verification found {result.Issues.Count} issue(s).";
    }
    private void RaiseActions() { EnableCommand.RaiseCanExecuteChanged(); DisableCommand.RaiseCanExecuteChanged(); RepairCommand.RaiseCanExecuteChanged(); ReinstallCommand.RaiseCanExecuteChanged(); UninstallCommand.RaiseCanExecuteChanged(); VerifyCommand.RaiseCanExecuteChanged(); }
    private void SetError(Exception ex) => Message = ex.Message;
}
