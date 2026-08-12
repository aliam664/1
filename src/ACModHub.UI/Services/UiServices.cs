using System.Windows;
using Microsoft.Win32;

namespace ACModHub.UI.Services;

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
}
