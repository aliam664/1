using System.Collections.ObjectModel;
using System.Windows;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.UI.Services;

namespace ACModHub.UI.ViewModels;

public sealed class DownloadsViewModel : ObservableObject
{
    private readonly IDownloadManager _downloads;
    private readonly IUiErrorHandler _errors;
    private string _url = string.Empty;
    private string _fileName = string.Empty;
    private string _sha256 = string.Empty;
    private DownloadJob? _selected;
    private string? _message;

    public DownloadsViewModel(IDownloadManager downloads, IUiErrorHandler errors)
    {
        _downloads = downloads; _errors = errors;
        _downloads.ProgressChanged += OnProgress;
        AddCommand = new AsyncRelayCommand(AddAsync, _ => Uri.TryCreate(Url, UriKind.Absolute, out _) && !string.IsNullOrWhiteSpace(FileName), SetError);
        PauseCommand = new AsyncRelayCommand((_, token) => _downloads.PauseAsync(Selected!.Request.Id, token), _ => Selected is not null, SetError);
        ResumeCommand = new AsyncRelayCommand((_, token) => _downloads.ResumeAsync(Selected!.Request.Id, token), _ => Selected is not null, SetError);
        CancelCommand = new AsyncRelayCommand((_, token) => _downloads.CancelAsync(Selected!.Request.Id, token), _ => Selected is not null, SetError);
        RetryCommand = new AsyncRelayCommand((_, token) => _downloads.RetryAsync(Selected!.Request.Id, token), _ => Selected is not null, SetError);
        InstallCommand = new RelayCommand(_ => PackageInstallRequested?.Invoke(Selected!.DestinationPath!), _ => Selected?.State == DownloadState.Completed && File.Exists(Selected.DestinationPath));
        Refresh();
    }

    public event Action<string>? PackageInstallRequested;
    public ObservableCollection<DownloadJob> Jobs { get; } = [];
    public string Url { get => _url; set { if (SetProperty(ref _url, value)) AddCommand.RaiseCanExecuteChanged(); } }
    public string FileName { get => _fileName; set { if (SetProperty(ref _fileName, value)) AddCommand.RaiseCanExecuteChanged(); } }
    public string Sha256 { get => _sha256; set => SetProperty(ref _sha256, value); }
    public DownloadJob? Selected { get => _selected; set { if (SetProperty(ref _selected, value)) RaiseActions(); } }
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public AsyncRelayCommand AddCommand { get; }
    public AsyncRelayCommand PauseCommand { get; }
    public AsyncRelayCommand ResumeCommand { get; }
    public AsyncRelayCommand CancelCommand { get; }
    public AsyncRelayCommand RetryCommand { get; }
    public RelayCommand InstallCommand { get; }

    private async Task AddAsync(object? _, CancellationToken cancellationToken)
    {
        var expected = string.IsNullOrWhiteSpace(Sha256) ? null : Sha256.Trim();
        await _downloads.EnqueueAsync(new DownloadRequest { Source = new Uri(Url), FileName = FileName.Trim(), ExpectedSha256 = expected }, cancellationToken);
        Url = string.Empty; FileName = string.Empty; Sha256 = string.Empty; Refresh();
    }
    private void OnProgress(object? sender, DownloadProgress progress)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) Refresh(); else dispatcher.InvokeAsync(Refresh);
    }
    private void Refresh() { var selectedId = Selected?.Request.Id; Jobs.Clear(); foreach (var job in _downloads.Jobs) Jobs.Add(job); Selected = Jobs.FirstOrDefault(x => x.Request.Id == selectedId); }
    private void RaiseActions() { PauseCommand.RaiseCanExecuteChanged(); ResumeCommand.RaiseCanExecuteChanged(); CancelCommand.RaiseCanExecuteChanged(); RetryCommand.RaiseCanExecuteChanged(); InstallCommand.RaiseCanExecuteChanged(); }
    private void SetError(Exception exception) => Message = _errors.Handle(exception, "Download");
}
