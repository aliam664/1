using System.IO;
using System.Net.Http;
using System.Windows;
using ACModHub.Core;
using ACModHub.Core.Interfaces;
using ACModHub.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace ACModHub.UI.Services;

public interface IUiErrorHandler
{
    string Handle(Exception exception, string operation);
}

/// <summary>Maps exceptions to localized user-facing messages. Technical details stay in the log.</summary>
public sealed class UiErrorHandler : IUiErrorHandler
{
    private readonly ILocalizationService _localization;
    private readonly ILogger<UiErrorHandler> _logger;

    public UiErrorHandler(ILocalizationService localization, ILogger<UiErrorHandler> logger)
    {
        _localization = localization;
        _logger = logger;
    }

    public string Handle(Exception exception, string operation)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var root = Unwrap(exception);
        _logger.LogError(root, "UI operation {Operation} failed", operation);
        var localizedOperation = _localization.Get("CommonError");
        return root switch
        {
            UnauthorizedAccessException => _localization.Format("ErrNotWritable", localizedOperation),
            FileNotFoundException file => _localization.Format("ErrFileNotFound", localizedOperation, Path.GetFileName(file.FileName)),
            DirectoryNotFoundException => _localization.Format("ErrDirectoryMissing", localizedOperation),
            UnsafeArchiveException unsafeArchive => _localization.Format("ErrUnsafeArchive", localizedOperation, unsafeArchive.Message),
            InvalidDataException => _localization.Get("ErrCorruptPackage"),
            HttpRequestException => _localization.Get("ErrHttp"),
            OperationCanceledException => _localization.Format("ErrCancelled", localizedOperation),
            IOException io when IsSharingViolation(io) => _localization.Format("ErrLocked", localizedOperation),
            IOException => _localization.Format("ErrIo", localizedOperation),
            ModHubException => _localization.Format("ErrUnknown", localizedOperation) + " — " + root.Message,
            _ => _localization.Format("ErrUnknown", localizedOperation)
        };
    }

    private static Exception Unwrap(Exception exception)
    {
        while (exception is AggregateException { InnerExceptions.Count: 1 } aggregate) exception = aggregate.InnerExceptions[0];
        return exception.InnerException is not null && exception is not ModHubException ? exception.InnerException : exception;
    }

    private static bool IsSharingViolation(IOException exception)
    {
        var code = exception.HResult & 0xFFFF;
        return code is 32 or 33;
    }
}

public interface IFilePickerService
{
    string? PickArchive();
    string? PickGameFolder();
}

public sealed class FilePickerService : IFilePickerService
{
    public string? PickArchive()
    {
        var dialog = new OpenFileDialog { Title = "Import mod archive", Filter = "Mod archives (*.zip;*.7z;*.rar)|*.zip;*.7z;*.rar", Multiselect = false, CheckFileExists = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickGameFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Select the Assetto Corsa folder", Multiselect = false };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}

public interface ILocalizationService
{
    string Language { get; }
    FlowDirection FlowDirection { get; }
    event EventHandler? LanguageChanged;
    void SetLanguage(string language);
    string Get(string key);
    string Format(string key, params object[] args);
}

public sealed class LocalizationService : ILocalizationService
{
    public string Language { get; private set; } = "fa-IR";
    public FlowDirection FlowDirection => Language.StartsWith("fa", StringComparison.OrdinalIgnoreCase) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    public event EventHandler? LanguageChanged;

    public void SetLanguage(string language)
    {
        var normalized = language.StartsWith("fa", StringComparison.OrdinalIgnoreCase) ? "fa-IR" : "en-US";
        var application = Application.Current;
        if (application is not null)
        {
            var old = application.Resources.MergedDictionaries.FirstOrDefault(x => x.Source?.OriginalString.Contains("Strings.", StringComparison.OrdinalIgnoreCase) == true);
            if (old is not null) application.Resources.MergedDictionaries.Remove(old);
            application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/ACModHub.UI;component/Resources/Strings.{normalized}.xaml", UriKind.Relative) });
        }
        Language = normalized;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Get(string key)
    {
        var value = Application.Current?.TryFindResource(key);
        return value as string ?? key;
    }

    public string Format(string key, params object[] args)
    {
        var template = Get(key);
        try { return string.Format(template, args); }
        catch (FormatException) { return template; }
    }
}

/// <summary>
/// Detects whether the WebView2 Evergreen Runtime is installed, without ever
/// instantiating the control (which would crash on machines without the runtime).
/// </summary>
public sealed class WebView2RuntimeDetector
{
    public bool IsAvailable()
    {
        try
        {
            var version = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
            return !string.IsNullOrWhiteSpace(version);
        }
        catch (Exception ex) when (ex is Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException or DllNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }
}
