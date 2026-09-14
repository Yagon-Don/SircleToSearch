using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace SircleToSearch;

public sealed class HotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 0xA11CE;

    [Flags]
    private enum Modifiers : uint
    {
        Alt = 0x0001,
        Control = 0x0002,
        Shift = 0x0004,
        Win = 0x0008,
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource _source;
    private bool _registered;

    public event Action? HotkeyPressed;

    public HotkeyManager()
    {
        // Plain invisible top-level window (0x0 size, no WS_VISIBLE). HWND_MESSAGE
        // windows are unreliable for WM_HOTKEY delivery on some setups — this is the
        // pattern every WPF global-hotkey library actually ships.
        var parameters = new HwndSourceParameters("SircleToSearchHotkeySink")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = 0x80, // WS_EX_TOOLWINDOW — keeps it out of alt-tab/taskbar
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        const uint VK_Q = 0x51;
        _registered = RegisterHotKey(_source.Handle, HOTKEY_ID,
            (uint)(Modifiers.Win | Modifiers.Shift), VK_Q);
        var win32Error = _registered ? 0 : Marshal.GetLastWin32Error();

        var log = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SircleToSearch", "hotkey.log");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(log)!);
        System.IO.File.AppendAllText(log,
            $"{DateTime.Now:O} hwnd={_source.Handle} registered={_registered} win32Error={win32Error}\n");

        if (!_registered)
        {
            MessageBox.Show(Strings.Get("HotkeyFailed", win32Error),
                "SircleToSearch", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            var log = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SircleToSearch", "hotkey.log");
            System.IO.File.AppendAllText(log, $"{DateTime.Now:O} WM_HOTKEY fired\n");

            HotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered)
        {
            UnregisterHotKey(_source.Handle, HOTKEY_ID);
            _registered = false;
        }
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
