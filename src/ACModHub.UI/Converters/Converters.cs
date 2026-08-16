using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ACModHub.Core.Models;

namespace ACModHub.UI.Converters;

public sealed class BytesConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var size = value switch
        {
            long number => (double)number,
            int number => number,
            double number => number,
            _ => 0d
        };
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.#} {units[unit]}";
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class BoolVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class InverseBoolVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is null ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class InverseNullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is null ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Localizes an enum value using the key convention "{parameter}{EnumName}" (e.g. "Category" + Car → CategoryCar).</summary>
public sealed class LocalizedEnumConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null) return string.Empty;
        var prefix = parameter as string ?? string.Empty;
        var key = prefix + value;
        return Application.Current?.TryFindResource(key) as string ?? value.ToString() ?? string.Empty;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class ModStatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ModStatus.Enabled or ModStatus.Installed => Brush("SafeBrush"),
        ModStatus.Disabled => Brush("SubtleTextBrush"),
        ModStatus.Damaged or ModStatus.Failed => Brush("ErrorBrush"),
        ModStatus.Installing => Brush("WarningBrush"),
        _ => Brush("SecondaryTextBrush")
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;

    private static Brush Brush(string key) => Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
}

/// <summary>Converts a string resource key into its localized value.</summary>
public sealed class LocalizedKeyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string key ? Application.Current?.TryFindResource(key) as string ?? key : string.Empty;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>True when the string value equals the converter parameter.</summary>
public sealed class StringEqualsParameterConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.Equals(value as string, parameter as string, StringComparison.OrdinalIgnoreCase);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Maps DiagnosticStatus to the token brush (Passed=Safe, Warning=Warning, Failed=Error).</summary>
public sealed class DiagnosticStatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        DiagnosticStatus.Passed => Brush("SafeBrush"),
        DiagnosticStatus.Warning => Brush("WarningBrush"),
        DiagnosticStatus.Failed => Brush("ErrorBrush"),
        _ => Brush("SecondaryTextBrush")
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;

    private static Brush Brush(string key) => Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
}

/// <summary>Converts a geometry resource key into the Geometry instance (icon lookup).</summary>
public sealed class GeometryByKeyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string key && Application.Current?.TryFindResource(key) is Geometry geometry ? geometry : Geometry.Empty;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Visible only while an update package is downloaded and verified, ready to apply.</summary>
public sealed class UpdateReadyVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is UpdateDownloadState.ReadyToApply or UpdateDownloadState.Applying ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class DownloadStateBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        DownloadState.Completed => Brush("SafeBrush"),
        DownloadState.Failed => Brush("ErrorBrush"),
        DownloadState.Downloading or DownloadState.Verifying => Brush("PrimaryBrush"),
        DownloadState.Paused => Brush("WarningBrush"),
        DownloadState.Cancelled => Brush("SubtleTextBrush"),
        _ => Brush("SecondaryTextBrush")
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;

    private static Brush Brush(string key) => Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
}
