using System.Diagnostics;
using System.IO;
using System.Windows;
using ACModHub.Core;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Models;
using ACModHub.UI.Services;

namespace ACModHub.UI.ViewModels;

public sealed class UpdatesViewModel : ObservableObject
{
    private readonly IUpdateChecker _checker;
    private readonly IUpdateDownloader _downloader;
    private readonly IUpdateApplier _applier;
    private readonly ISettingsService _settings;
    private readonly ILocalizationService _localization;
    private readonly IUiErrorHandler _errors;

    private UpdateChannel _channel = UpdateChannel.Stable;
    private UpdateCheckResult? _checkResult;
    private UpdateCheckState _checkState = UpdateCheckState.Idle;
    private UpdateDownloadState _downloadState = UpdateDownloadState.Idle;
    private double? _progress;
    private string _progressText = string.Empty;
    private string? _message;

    public UpdatesViewModel(
        IUpdateChecker checker,
        IUpdateDownloader downloader,
        IUpdateApplier applier,
        ISettingsService settings,
        ILocalizationService localization,
        IUiErrorHandler errors)
    {
        _checker = checker;
        _downloader = downloader;
        _applier = applier;
        _settings = settings;
        _localization = localization;
        _errors = errors;
        CheckCommand = new AsyncRelayCommand((_, token) => CheckAsync(true, token), _ => !IsBusy, SetError);
        DownloadInstallCommand = new AsyncRelayCommand(DownloadAndApplyAsync, _ => HasUpdate && DownloadState is UpdateDownloadState.Idle or UpdateDownloadState.Failed or UpdateDownloadState.Cancelled, SetError);
        CancelCommand = new AsyncRelayCommand((_, _) => { if (DownloadInstallCommand.IsRunning) DownloadInstallCommand.Cancel(); return Task.CompletedTask; }, _ => DownloadState is UpdateDownloadState.Downloading or UpdateDownloadState.Verifying, SetError);
        ViewReleaseCommand = new RelayCommand(ViewRelease, _ => _checkResult?.Release?.HtmlUrl is not null);
        LaterCommand = new AsyncRelayCommand(LaterAsync, _ => HasUpdate, SetError);
    }

    public string CurrentVersion => AppInfo.Version;
    public string AvailableVersion => _checkResult?.Release?.Version.ToString() ?? "—";
    public string ReleaseTitle => _checkResult?.Release?.Title ?? string.Empty;
    public string ReleaseNotes => string.IsNullOrWhiteSpace(_checkResult?.Release?.ReleaseNotes)
        ? _localization.Get("NoReleaseNotes")
        : _checkResult.Release!.ReleaseNotes;
    public string PublishedAtText => _checkResult?.Release is { } release ? release.PublishedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "—";
    public string DownloadSizeText => _checkResult?.Release?.InstallerAsset is { } asset ? FormatBytes(asset.Size) : "—";
    public string ChannelText => _localization.Get(_channel == UpdateChannel.Beta ? "ChannelBeta" : "ChannelStable");
    public bool HasUpdate => _checkResult?.HasUpdate == true && CheckState == UpdateCheckState.UpdateAvailable;
    public bool IsChecking => CheckState == UpdateCheckState.Checking;
    public bool IsUpToDate => CheckState == UpdateCheckState.UpToDate;
    public bool CheckFailed => CheckState == UpdateCheckState.CheckingFailed;
    public UpdateCheckState CheckState { get => _checkState; private set { if (SetProperty(ref _checkState, value)) { OnPropertyChanged(nameof(HasUpdate)); OnPropertyChanged(nameof(IsChecking)); OnPropertyChanged(nameof(IsUpToDate)); OnPropertyChanged(nameof(CheckFailed)); CheckCommand.RaiseCanExecuteChanged(); DownloadInstallCommand.RaiseCanExecuteChanged(); LaterCommand.RaiseCanExecuteChanged(); } } }
    public UpdateDownloadState DownloadState { get => _downloadState; private set { if (SetProperty(ref _downloadState, value)) { OnPropertyChanged(nameof(IsDownloading)); DownloadInstallCommand.RaiseCanExecuteChanged(); CancelCommand.RaiseCanExecuteChanged(); } } }
    public bool IsDownloading => DownloadState is UpdateDownloadState.Downloading or UpdateDownloadState.Verifying;
    public bool IsBusy => CheckState == UpdateCheckState.Checking || IsDownloading;
    public double? Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public string ProgressText { get => _progressText; private set => SetProperty(ref _progressText, value); }
    public string? Message { get => _message; private set => SetProperty(ref _message, value); }
    public AsyncRelayCommand CheckCommand { get; }
    public AsyncRelayCommand DownloadInstallCommand { get; }
    public AsyncRelayCommand CancelCommand { get; }
    public RelayCommand ViewReleaseCommand { get; }
    public AsyncRelayCommand LaterCommand { get; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settings.LoadAsync(cancellationToken);
        _channel = settings.UpdateChannel == "beta" ? UpdateChannel.Beta : UpdateChannel.Stable;
        OnPropertyChanged(nameof(ChannelText));
        await CheckAsync(false, cancellationToken);
    }

    private async Task CheckAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        CheckState = UpdateCheckState.Checking;
        Message = null;
        Progress = null;
        try
        {
            _checkResult = await _checker.CheckAsync(_channel, forceRefresh, cancellationToken);
            CheckState = _checkResult.HasUpdate ? UpdateCheckState.UpdateAvailable : UpdateCheckState.UpToDate;
            OnPropertyChanged(nameof(AvailableVersion));
            OnPropertyChanged(nameof(ReleaseTitle));
            OnPropertyChanged(nameof(ReleaseNotes));
            OnPropertyChanged(nameof(PublishedAtText));
            OnPropertyChanged(nameof(DownloadSizeText));
            ViewReleaseCommand.RaiseCanExecuteChanged();
        }
        catch (OperationCanceledException)
        {
            CheckState = UpdateCheckState.Idle;
        }
        catch (Exception ex)
        {
            CheckState = UpdateCheckState.CheckingFailed;
            Message = _errors.Handle(ex, _localization.Get("UpdatesTitle"));
        }
    }

    private async Task DownloadAndApplyAsync(object? _, CancellationToken cancellationToken)
    {
        if (_checkResult?.Release is not { } release) return;
        Message = null;
        try
        {
            var progress = new Progress<UpdateDownloadProgress>(value =>
            {
                DownloadState = value.State;
                Progress = value.Percentage;
                ProgressText = value.State switch
                {
                    UpdateDownloadState.Downloading => _localization.Get("UpdateDownloading") + " — " + FormatBytes(value.BytesReceived),
                    UpdateDownloadState.Verifying => _localization.Get("UpdateVerifying"),
                    UpdateDownloadState.ReadyToApply => _localization.Get("UpdateReady"),
                    _ => string.Empty
                };
            });
            var packagePath = await _downloader.DownloadAsync(release, progress, cancellationToken);
            if (release.InstallerAsset is not null && File.Exists(packagePath))
            {
                if (_applier.TryApply(packagePath))
                {
                    DownloadState = UpdateDownloadState.Applying;
                    ProgressText = _localization.Get("UpdateApplying");
                    _downloader.Cleanup(packagePath);
                    Application.Current?.Shutdown(0);
                }
                else
                {
                    DownloadState = UpdateDownloadState.Failed;
                    Message = _localization.Get("UpdateFailed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            DownloadState = UpdateDownloadState.Cancelled;
            ProgressText = _localization.Get("UpdateCancelled");
        }
        catch (Exception ex)
        {
            DownloadState = UpdateDownloadState.Failed;
            Message = _errors.Handle(ex, _localization.Get("UpdatesTitle"));
        }
    }

    private void ViewRelease(object? _)
    {
        if (_checkResult?.Release?.HtmlUrl is not { } url) return;
        try { Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { Message = _localization.Get("UpdateFailed"); }
    }

    private async Task LaterAsync(object? _, CancellationToken token)
    {
        if (_checkResult?.Release is not { } release) return;
        var settings = await _settings.LoadAsync(token);
        settings.SkippedUpdateVersion = release.Version.ToString();
        await _settings.SaveAsync(settings, token);
        CheckState = UpdateCheckState.Idle;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1) { value /= 1024; index++; }
        return $"{value:0.#} {units[index]}";
    }

    private void SetError(Exception exception) => Message = _errors.Handle(exception, _localization.Get("UpdatesTitle"));
}
