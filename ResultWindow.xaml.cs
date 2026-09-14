using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace SircleToSearch;

public partial class ResultWindow : Window
{
    private byte[] _jpegBytes;
    private bool _closing;
    private bool _busy;
    private byte[]? _pendingJpegBytes;
    private MorphingLoader? _loader;
    private bool _hasContent;
    private const string MobileUserAgent =
        "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/124.0.0.0 Mobile Safari/537.36";

    public ResultWindow(byte[] jpegBytes)
    {
        InitializeComponent();
        _jpegBytes = jpegBytes;
        Loaded += ResultWindow_Loaded;
    }

    private async void ResultWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        Width = 460;
        var screenHeight = SystemParameters.WorkArea.Height;
        Height = Math.Min(760, screenHeight * 0.82);

        Left = SystemParameters.WorkArea.Right - Width - 24;
        var targetTop = SystemParameters.WorkArea.Bottom - Height;
        Top = SystemParameters.WorkArea.Bottom;

        var slideUp = new DoubleAnimation(SystemParameters.WorkArea.Bottom, targetTop,
            TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(TopProperty, slideUp);

        _loader = new MorphingLoader(SpinnerShape, radius: 24);

        Activate();
        await RunSearchAsync(_jpegBytes);
    }

    /// <summary>Re-runs the search with a new crop in this same window (drag/resize on the overlay).</summary>
    public void UpdateSearch(byte[] jpegBytes)
    {
        _jpegBytes = jpegBytes;
        if (_busy)
        {
            // A previous search is still in flight — coalesce to the latest crop
            // rather than piling up overlapping WebView2 navigations.
            _pendingJpegBytes = jpegBytes;
            return;
        }
        _ = RunSearchAsync(jpegBytes);
    }

    private async Task RunSearchAsync(byte[] jpegBytes)
    {
        _busy = true;
        SetStatus(searching: true);
        if (!_hasContent) LoadingBackdrop.Source = null; // nothing to blur yet on the very first search
        ShowLoading();

        // Keep re-snapshotting the page for as long as this search is in flight, so the
        // blur behind the spinner reflects the actual loading progress (blank -> google.com
        // -> results painting in) instead of one frozen frame from before the request began.
        using var captureLoopCts = new CancellationTokenSource();
        var captureLoop = RunCaptureLoopAsync(captureLoopCts.Token);

        try
        {
            await NavigateToLensResultsAsync(jpegBytes);
        }
        catch (Exception ex)
        {
            AppLog.Error("Загрузка картинки в Google не удалась", ex);
        }
        finally
        {
            captureLoopCts.Cancel();
            await captureLoop;

            // No artificial minimum — the spinner shows for exactly as long as the
            // real upload+navigate takes, then fades out (see HideLoadingAsync).
            await HideLoadingAsync();
            _busy = false;
            SetStatus(searching: false);
        }

        if (_pendingJpegBytes is { } pending)
        {
            _pendingJpegBytes = null;
            await RunSearchAsync(pending);
        }
    }

    private async Task RunCaptureLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (Browser.CoreWebView2 is not null)
                await CaptureBackdropAsync();

            try
            {
                await Task.Delay(200, token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task CaptureBackdropAsync()
    {
        try
        {
            using var stream = new MemoryStream();
            await Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
            stream.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            LoadingBackdrop.Source = bitmap;
            _hasContent = true;
        }
        catch
        {
            // Purely cosmetic and runs every 200ms — a failed capture just means no
            // fresh backdrop frame this tick, not worth logging every miss.
        }
    }

    private void ShowLoading()
    {
        Browser.Visibility = Visibility.Collapsed;
        LoadingOverlay.BeginAnimation(OpacityProperty, null);
        LoadingOverlay.Opacity = 1;
        LoadingOverlay.Visibility = Visibility.Visible;
        _loader?.Start();
    }

    private Task HideLoadingAsync()
    {
        var tcs = new TaskCompletionSource();
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));
        fade.Completed += (_, _) =>
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            Browser.Visibility = Visibility.Visible;
            _loader?.Stop();
            tcs.TrySetResult();
        };
        LoadingOverlay.BeginAnimation(OpacityProperty, fade);
        return tcs.Task;
    }

    private void SetStatus(bool searching)
    {
        if (searching)
        {
            StatusText.Text = Strings.Get("ResultHeaderSearching");
            var pulse = new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(700))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
            };
            StatusDot.BeginAnimation(OpacityProperty, pulse);
        }
        else
        {
            StatusDot.BeginAnimation(OpacityProperty, null);
            StatusDot.Opacity = 1;
            StatusText.Text = Strings.Get("ResultHeaderIdle");
        }
    }

    private async Task NavigateToLensResultsAsync(byte[] jpegBytes)
    {
        // EnsureCoreWebView2Async throws if called again with a DIFFERENT
        // CoreWebView2Environment instance — which a fresh CreateAsync() call always is.
        // Only run this once; every re-search after the first reuses the existing
        // CoreWebView2 the control already has.
        if (Browser.CoreWebView2 is null)
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SircleToSearch", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await Browser.EnsureCoreWebView2Async(env);

            Browser.CoreWebView2!.Settings.UserAgent = MobileUserAgent;
            Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        }

        // Navigate to google.com first so the upload fetch below is same-origin —
        // that way the uploaded image and the results page share the exact same
        // cookie jar/session automatically, instead of us having to transplant
        // Set-Cookie headers between a separate HttpClient and the WebView2 profile.
        var navigated = new TaskCompletionSource();
        void OnNavCompleted(object? s, CoreWebView2NavigationCompletedEventArgs e) => navigated.TrySetResult();
        Browser.CoreWebView2.NavigationCompleted += OnNavCompleted;
        Browser.CoreWebView2.Navigate("https://www.google.com/");
        await navigated.Task;
        Browser.CoreWebView2.NavigationCompleted -= OnNavCompleted;

        // ExecuteScriptAsync's return value does NOT reliably await a Promise on
        // every WebView2 runtime build — it can hand back the serialized (empty)
        // Promise object instead of the resolved value. postMessage + WebMessageReceived
        // is the pattern that actually works for getting an async result back to C#.
        var uploadDone = new TaskCompletionSource<string>();
        void OnMessage(object? s, CoreWebView2WebMessageReceivedEventArgs e) =>
            uploadDone.TrySetResult(e.WebMessageAsJson);
        Browser.CoreWebView2.WebMessageReceived += OnMessage;

        var base64 = Convert.ToBase64String(jpegBytes);
        var script = $$"""
            (async () => {
                try {
                    const byteChars = atob("{{base64}}");
                    const byteNumbers = new Array(byteChars.length);
                    for (let i = 0; i < byteChars.length; i++) byteNumbers[i] = byteChars.charCodeAt(i);
                    const blob = new Blob([new Uint8Array(byteNumbers)], { type: "image/jpeg" });
                    const fd = new FormData();
                    fd.append("encoded_image", blob, "screenshot.jpg");
                    fd.append("image_url", "");
                    fd.append("sbisrc", "th");
                    const resp = await fetch("https://www.google.com/searchbyimage/upload", {
                        method: "POST",
                        body: fd,
                        credentials: "include",
                    });
                    window.chrome.webview.postMessage({ ok: true, url: resp.url });
                } catch (err) {
                    window.chrome.webview.postMessage({ ok: false, error: String(err) });
                }
            })();
            """;

        await Browser.CoreWebView2.ExecuteScriptAsync(script);
        var messageJson = await uploadDone.Task;
        Browser.CoreWebView2.WebMessageReceived -= OnMessage;

        using var doc = JsonDocument.Parse(messageJson);
        var root = doc.RootElement;
        if (!root.GetProperty("ok").GetBoolean())
            throw new InvalidOperationException($"Google отклонил загрузку: {root.GetProperty("error").GetString()}");

        var resultUrl = root.GetProperty("url").GetString();
        if (string.IsNullOrEmpty(resultUrl))
            throw new InvalidOperationException("Google не вернул URL результата поиска.");

        var resultsLoaded = new TaskCompletionSource();
        void OnResultsNavCompleted(object? s, CoreWebView2NavigationCompletedEventArgs e) => resultsLoaded.TrySetResult();
        Browser.CoreWebView2.NavigationCompleted += OnResultsNavCompleted;
        Browser.CoreWebView2.Navigate(resultUrl);
        await resultsLoaded.Task;
        Browser.CoreWebView2.NavigationCompleted -= OnResultsNavCompleted;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseOnce();

    private void CloseOnce()
    {
        // Windows fires WM_ACTIVATE (and so Deactivated) more than once while a
        // window is tearing itself down — a second Close() call mid-close throws.
        if (_closing) return;
        _closing = true;
        Close();
    }
}
