using System.IO;
using System.Windows;
using System.Windows.Controls;
using ACModHub.Infrastructure.Services;

namespace ACModHub.UI.Behaviors;

/// <summary>
/// Loads catalog cover images asynchronously through the bounded CoverImageService cache
/// and displays them on an Image control. Loading is cancelled when the element unloads
/// (scroll / navigation), and failures fall back to the placeholder brush.
/// </summary>
public static class AsyncCoverBehavior
{
    public static readonly DependencyProperty CoverUriProperty = DependencyProperty.RegisterAttached(
        "CoverUri", typeof(Uri), typeof(AsyncCoverBehavior), new PropertyMetadata(null, OnCoverUriChanged));

    public static readonly DependencyProperty ServiceProperty = DependencyProperty.RegisterAttached(
        "Service", typeof(ICoverImageService), typeof(AsyncCoverBehavior), new PropertyMetadata(null));

    public static void SetCoverUri(DependencyObject element, Uri? value) => element.SetValue(CoverUriProperty, value);
    public static Uri? GetCoverUri(DependencyObject element) => (Uri?)element.GetValue(CoverUriProperty);
    public static void SetService(DependencyObject element, ICoverImageService? value) => element.SetValue(ServiceProperty, value);
    public static ICoverImageService? GetService(DependencyObject element) => (ICoverImageService?)element.GetValue(ServiceProperty);

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(CoverLoadState), typeof(AsyncCoverBehavior));

    private static void OnCoverUriChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not Image image) return;
        image.Unloaded -= OnUnloaded;
        image.Unloaded += OnUnloaded;
        if (GetState(image) is { } previous) previous.Cancel();
        image.Source = null;

        if (args.NewValue is not Uri uri) return;

        var service = GetService(image);
        if (service is null) return;

        var state = new CoverLoadState();
        SetState(image, state);
        _ = LoadAsync(image, uri, service, state);
    }

    private static void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (sender is not Image image) return;
        GetState(image)?.Cancel();
        image.Unloaded -= OnUnloaded;
    }

    private static async Task LoadAsync(Image image, Uri uri, ICoverImageService service, CoverLoadState state)
    {
        try
        {
            var path = await service.GetCachedCoverAsync(uri, state.Token).ConfigureAwait(true);
            if (state.IsCancelled || string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            image.Source = bitmap;
        }
        catch
        {
            // Decode failures fall back to the placeholder — never crash the UI.
        }
    }

    private static CoverLoadState? GetState(DependencyObject element) => (CoverLoadState?)element.GetValue(StateProperty);
    private static void SetState(DependencyObject element, CoverLoadState? value) => element.SetValue(StateProperty, value);

    private sealed class CoverLoadState
    {
        private readonly CancellationTokenSource _cts = new();
        public CancellationToken Token => _cts.Token;
        public bool IsCancelled => _cts.IsCancellationRequested;
        public void Cancel() { try { _cts.Cancel(); } catch (ObjectDisposedException) { } }
    }
}
