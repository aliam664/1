using System.IO;
using System.Windows;
using System.Windows.Input;

namespace ACModHub.UI.Behaviors;

public static class FileDropBehavior
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(FileDropBehavior), new PropertyMetadata(null, OnCommandChanged));

    public static void SetCommand(DependencyObject element, ICommand? value) => element.SetValue(CommandProperty, value);
    public static ICommand? GetCommand(DependencyObject element) => (ICommand?)element.GetValue(CommandProperty);

    private static void OnCommandChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not UIElement element) return;
        element.AllowDrop = args.NewValue is not null;
        element.DragOver -= OnDragOver;
        element.Drop -= OnDrop;
        if (args.NewValue is not null) { element.DragOver += OnDragOver; element.Drop += OnDrop; }
    }

    private static void OnDragOver(object sender, DragEventArgs args)
    {
        args.Effects = args.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        args.Handled = true;
    }

    private static void OnDrop(object sender, DragEventArgs args)
    {
        if (sender is not DependencyObject target || GetCommand(target) is not { } command) return;
        var files = args.Data.GetData(DataFormats.FileDrop) as string[] ?? [];
        var archives = files.Where(x => new[] { ".zip", ".7z", ".rar" }.Contains(Path.GetExtension(x), StringComparer.OrdinalIgnoreCase)).ToArray();
        if (archives.Length > 0 && command.CanExecute(archives)) command.Execute(archives);
    }
}
