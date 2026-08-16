using System.Collections.ObjectModel;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.UI.Services;

namespace ACModHub.UI.ViewModels;

public sealed class UpdatesViewModel : ObservableObject
{
    private readonly IEnumerable<IContentProvider> _providers;
    private readonly IModRepository _repository;
    private readonly IUiErrorHandler _errors;
    private string? _message;
    public UpdatesViewModel(IEnumerable<IContentProvider> providers, IModRepository repository, IUiErrorHandler errors)
    {
        _providers = providers; _repository = repository; _errors = errors;
        CheckCommand = new AsyncRelayCommand(CheckAsync, onError: ex => Message = _errors.Handle(ex, "Update check"));
    }
    public ObservableCollection<ContentRelease> Releases { get; } = [];
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public AsyncRelayCommand CheckCommand { get; }
    private async Task CheckAsync(object? _, CancellationToken token)
    {
        Releases.Clear();
        var installed = await _repository.GetAllAsync(token);
        foreach (var provider in _providers)
            foreach (var release in await provider.CheckUpdatesAsync(installed, token)) Releases.Add(release);
        Message = Releases.Count == 0 ? "No provider-reported updates. Direct HTTP sources can be added in Downloads." : $"{Releases.Count} update(s) found.";
    }
}
