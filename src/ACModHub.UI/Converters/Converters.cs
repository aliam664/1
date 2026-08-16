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

public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ModStatus.Enabled or ModStatus.Installed => new SolidColorBrush(Color.FromRgb(57, 210, 160)),
        ModStatus.Disabled => new SolidColorBrush(Color.FromRgb(126, 137, 158)),
        ModStatus.Damaged or ModStatus.Failed => new SolidColorBrush(Color.FromRgb(255, 81, 96)),
        _ => new SolidColorBrush(Color.FromRgb(255, 184, 77))
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class BoolVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
