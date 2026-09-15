using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
    private double _targetLeft;
    private double _targetTop;
    private const string MobileUserAgent =
        "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/124.0.0.0 Mobile Safari/537.36";

    public ResultWindow()
    {
        InitializeComponent();

        // Sized and parked off-screen BEFORE the first Show() paints a frame — doing
        // this in Loaded instead left a visible blink: with no Width/Height/Left/Top
        // set yet, WPF+Windows composite one frame at the OS's default placement for
        // an unpositioned window (roughly left-of-center) before it jumps off-screen.
        Width = 460;
        var screenHeight = SystemParameters.WorkArea.Height;
        Height = Math.Min(760, screenHeight * 0.82);
        _targetLeft = SystemParameters.WorkArea.Right - Width - 24;
        _targetTop = SystemParameters.WorkArea.Bottom - Height;

        // Parked far off any monitor until Reveal() — this window is created and shown
        // (so its WebView2 control gets a real HWND to initialize in) as soon as the
        // overlay opens, well before the user finishes dragging a selection, so
        // PreWarmAsync can eat the WebView2 startup cost while they're still drawing.
        // Opacity=0/Visibility.Hidden tricks both left visible artifacts (DWM ghosts a
        // layered window that hasn't composited a real frame, or the brief window
        // between Show() and the property taking effect flashes on screen) — physically
        // parking it off-screen means there's nothing for the compositor to ever draw.
        Left = -5000;
        Top = _targetTop;

        Loaded += ResultWindow_Loaded;
    }

    private void ResultWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        _loader = new MorphingLoader(SpinnerShape, radius: 24);
    }

    /// <summary>
    /// Fire-and-forget from the moment the overlay opens: gets WebView2's environment/
    /// controller (and, in the non-fast mode, google.com) spun up before the user has
    /// even finished selecting anything, so that cost doesn't sit on the critical path
    /// once they release the mouse.
    /// </summary>
    public async Task PreWarmAsync()
    {
        try
        {
            if (AppSettings.Current.FastSearch)
                await EnsureCoreWebView2Async();
            else
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
        Activate();
        Left = _targetLeft;
        Top = SystemParameters.WorkArea.Bottom;
        var slideUp = new DoubleAnimation(SystemParameters.WorkArea.Bottom, _targetTop,
            TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(TopProperty, slideUp);
    }

    private async Task RunSearchAsync(byte[] jpegBytes)
    {
        _busy = true;
        SetStatus(searching: true);
        ShowLoading();

        try
        {
            if (AppSettings.Current.FastSearch)
                await NavigateToLensResultsFastAsync(jpegBytes);
            else
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

    /// <summary>Ensures CoreWebView2 exists. Idempotent — a no-op after the first call.</summary>
    private async Task EnsureCoreWebView2Async()
    {
        if (Browser.CoreWebView2 is not null) return;

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // EnsureCoreWebView2Async throws if called again with a DIFFERENT
        // CoreWebView2Environment instance — which a fresh CreateAsync() call always is —
        // hence the CoreWebView2-is-null guard above.
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SircleToSearch", "WebView2");
        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
        await Browser.EnsureCoreWebView2Async(env);

        Browser.CoreWebView2!.Settings.UserAgent = MobileUserAgent;
        Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        AppLog.Info($"[perf] EnsureCoreWebView2: {sw.ElapsedMilliseconds}ms");
    }

    /// <summary>Ensures CoreWebView2 exists and is sitting on a google.com page. Idempotent/no-op if already there.</summary>
    private async Task EnsureOnGoogleAsync()
    {
        await EnsureCoreWebView2Async();

        // Skip navigating if we're already sitting on a google.com page (pre-warmed,
        // or a repeat search) — that round trip was pure dead weight every time.
        var onGoogle = Uri.TryCreate(Browser.CoreWebView2.Source, UriKind.Absolute, out var currentUri)
            && currentUri.Host.EndsWith("google.com", StringComparison.OrdinalIgnoreCase);
        if (!onGoogle)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
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

    /// <summary>
    /// Fast path: uploads via a plain HttpClient POST instead of a WebView2-hosted
    /// fetch() — skips ever navigating WebView2 to the google.com homepage first, which
    /// is noticeably quicker. The catch: the uploaded image is tied to the HttpClient's
    /// own session while WebView2 has a separate cookie jar, so the exact cookies Google
    /// set during the upload get copied into WebView2's CookieManager before navigating,
    /// putting the results page in the same session that actually holds the image. Since
    /// the request never visits google.com in a real browser session first, Google
    /// occasionally decides it's suspicious and shows a captcha instead of results — that
    /// tradeoff is why this path is opt-in (Settings > Fast search) rather than default.
    /// </summary>
    private async Task NavigateToLensResultsFastAsync(byte[] jpegBytes)
    {
        await EnsureCoreWebView2Async();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (resultUrl, cookies) = await UploadViaHttpAsync(jpegBytes);
        AppLog.Info($"[perf] Upload fetch: {sw.ElapsedMilliseconds}ms");

        var cookieManager = Browser.CoreWebView2.CookieManager;
        foreach (var cookie in cookies)
        {
            var wvCookie = cookieManager.CreateCookie(cookie.Name, cookie.Value, cookie.Domain, cookie.Path);
            wvCookie.IsSecure = cookie.Secure;
            wvCookie.IsHttpOnly = cookie.HttpOnly;
            cookieManager.AddOrUpdateCookie(wvCookie);
        }

        sw.Restart();
        var resultsLoaded = new TaskCompletionSource();
        void OnResultsNavCompleted(object? s, CoreWebView2NavigationCompletedEventArgs e) => resultsLoaded.TrySetResult();
        Browser.CoreWebView2.NavigationCompleted += OnResultsNavCompleted;
        Browser.CoreWebView2.Navigate(resultUrl);
        await resultsLoaded.Task;
        Browser.CoreWebView2.NavigationCompleted -= OnResultsNavCompleted;
        AppLog.Info($"[perf] Navigate to results page: {sw.ElapsedMilliseconds}ms");
    }

    private static async Task<(string ResultUrl, System.Collections.Generic.List<Cookie> Cookies)> UploadViaHttpAsync(byte[] jpegBytes)
    {
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            AllowAutoRedirect = false,
        };
        using var client = new HttpClient(handler);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(MobileUserAgent);

        using var content = new MultipartFormDataContent();
        var imageContent = new ByteArrayContent(jpegBytes);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(imageContent, "encoded_image", "screenshot.jpg");
        content.Add(new StringContent(""), "image_url");
        content.Add(new StringContent("th"), "sbisrc");

        using var response = await client.PostAsync("https://www.google.com/searchbyimage/upload", content);

        if ((int)response.StatusCode is < 300 or >= 400 || response.Headers.Location is null)
            throw new InvalidOperationException($"Google не вернул редирект на результат (статус {(int)response.StatusCode}).");

        var location = response.Headers.Location;
        var resultUrl = location.IsAbsoluteUri ? location.ToString() : "https://www.google.com" + location;
        var cookies = cookieContainer.GetCookies(new Uri("https://www.google.com")).Cast<Cookie>().ToList();

        return (resultUrl, cookies);
    }

    /// <summary>
    /// Default (reliable) path: does the upload from inside WebView2 itself via fetch(),
    /// after first navigating it to google.com, so the whole thing runs in one real
    /// browser session start to finish. Slower than the fast path (that extra
    /// google.com round trip), but doesn't trigger Google's captcha the way a
    /// same-origin-but-never-actually-visited HttpClient upload occasionally does.
    /// </summary>
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
