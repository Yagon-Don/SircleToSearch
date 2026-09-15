using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Web.WebView2.Core;

namespace SircleToSearch;

public partial class ResultWindow : Window
{
    private byte[]? _jpegBytes;
    private bool _closing;
    private bool _busy;
    private bool _revealed;
    private byte[]? _pendingJpegBytes;
    private MorphingLoader? _loader;
    private const string MobileUserAgent =
        "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/124.0.0.0 Mobile Safari/537.36";

    public ResultWindow()
    {
        InitializeComponent();
        Loaded += ResultWindow_Loaded;
    }

    private void ResultWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        Width = 460;
        var screenHeight = SystemParameters.WorkArea.Height;
        Height = Math.Min(760, screenHeight * 0.82);
        Left = SystemParameters.WorkArea.Right - Width - 24;
        Top = SystemParameters.WorkArea.Bottom - Height;

        // Hidden (not just transparent) until Reveal() — this window is created and shown
        // (so its WebView2 control gets a real HWND to initialize in) as soon as the
        // overlay opens, well before the user finishes dragging a selection, so
        // PreWarmAsync can eat the WebView2 startup + google.com navigation cost while
        // they're still drawing. Opacity=0 alone isn't enough here: DWM renders a
        // blurred "ghost" placeholder for a layered AllowsTransparency window that
        // hasn't fully composited a real frame yet, which showed up as a visible smudge
        // over the desktop. Visibility.Hidden gives it no screen presence at all, and as
        // a bonus a hidden window can't steal clicks either — no click-through hack needed.
        Opacity = 0;
        Visibility = Visibility.Hidden;

        _loader = new MorphingLoader(SpinnerShape, radius: 24);
    }

    /// <summary>
    /// Fire-and-forget from the moment the overlay opens: gets WebView2 initialized and
    /// sitting on google.com before the user has even finished selecting anything, so that
    /// cost doesn't sit on the critical path once they release the mouse.
    /// </summary>
    public async Task PreWarmAsync()
    {
        try
        {
            await EnsureOnGoogleAsync();
        }
        catch (Exception ex)
        {
            // Not fatal — the real search will just redo this work when it runs.
            AppLog.Error("Прогрев WebView2 не удался", ex);
        }
    }

    /// <summary>Shows the window (sliding up the first time) and runs a search with this crop.</summary>
    public void ShowSearch(byte[] jpegBytes)
    {
        _jpegBytes = jpegBytes;
        if (!_revealed)
        {
            _revealed = true;
            Reveal();
        }

        if (_busy)
        {
            // A previous search is still in flight — coalesce to the latest crop
            // rather than piling up overlapping WebView2 navigations.
            _pendingJpegBytes = jpegBytes;
            return;
        }
        _ = RunSearchAsync(jpegBytes);
    }

    private void Reveal()
    {
        Visibility = Visibility.Visible;
        Activate();
        var targetTop = Top;
        Top = SystemParameters.WorkArea.Bottom;
        var slideUp = new DoubleAnimation(SystemParameters.WorkArea.Bottom, targetTop,
            TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(TopProperty, slideUp);
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
    }

    private async Task RunSearchAsync(byte[] jpegBytes)
    {
        _busy = true;
        SetStatus(searching: true);
        ShowLoading();

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

    /// <summary>Ensures CoreWebView2 exists and is sitting on a google.com page. Idempotent/no-op if already there.</summary>
    private async Task EnsureOnGoogleAsync()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // EnsureCoreWebView2Async throws if called again with a DIFFERENT
        // CoreWebView2Environment instance — which a fresh CreateAsync() call always is.
        // Only run this once; every later call reuses the existing CoreWebView2.
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
        AppLog.Info($"[perf] EnsureCoreWebView2: {sw.ElapsedMilliseconds}ms");

        // Skip navigating if we're already sitting on a google.com page (pre-warmed,
        // or a repeat search) — that round trip was pure dead weight every time.
        var onGoogle = Uri.TryCreate(Browser.CoreWebView2.Source, UriKind.Absolute, out var currentUri)
            && currentUri.Host.EndsWith("google.com", StringComparison.OrdinalIgnoreCase);
        if (!onGoogle)
        {
            sw.Restart();
            var navigated = new TaskCompletionSource();
            void OnNavCompleted(object? s, CoreWebView2NavigationCompletedEventArgs e) => navigated.TrySetResult();
            Browser.CoreWebView2.NavigationCompleted += OnNavCompleted;
            Browser.CoreWebView2.Navigate("https://www.google.com/");
            await navigated.Task;
            Browser.CoreWebView2.NavigationCompleted -= OnNavCompleted;
            AppLog.Info($"[perf] Navigate to google.com: {sw.ElapsedMilliseconds}ms");
        }
        else
        {
            AppLog.Info("[perf] Navigate to google.com: skipped (already there)");
        }
    }

    private async Task NavigateToLensResultsAsync(byte[] jpegBytes)
    {
        await EnsureOnGoogleAsync();

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

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Browser.CoreWebView2.ExecuteScriptAsync(script);
        var messageJson = await uploadDone.Task;
        Browser.CoreWebView2.WebMessageReceived -= OnMessage;
        AppLog.Info($"[perf] Upload fetch: {sw.ElapsedMilliseconds}ms");

        using var doc = JsonDocument.Parse(messageJson);
        var root = doc.RootElement;
        if (!root.GetProperty("ok").GetBoolean())
            throw new InvalidOperationException($"Google отклонил загрузку: {root.GetProperty("error").GetString()}");

        var resultUrl = root.GetProperty("url").GetString();
        if (string.IsNullOrEmpty(resultUrl))
            throw new InvalidOperationException("Google не вернул URL результата поиска.");

        sw.Restart();
        var resultsLoaded = new TaskCompletionSource();
        void OnResultsNavCompleted(object? s, CoreWebView2NavigationCompletedEventArgs e) => resultsLoaded.TrySetResult();
        Browser.CoreWebView2.NavigationCompleted += OnResultsNavCompleted;
        Browser.CoreWebView2.Navigate(resultUrl);
        await resultsLoaded.Task;
        Browser.CoreWebView2.NavigationCompleted -= OnResultsNavCompleted;
        AppLog.Info($"[perf] Navigate to results page: {sw.ElapsedMilliseconds}ms");
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
