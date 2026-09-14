using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace SircleToSearch;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private WinForms.NotifyIcon? _trayIcon;
    private HotkeyManager? _hotkeyManager;
    private OverlayWindow? _activeOverlay;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        WinForms.Application.SetHighDpiMode(WinForms.HighDpiMode.PerMonitorV2);
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Error("Необработанное исключение (UI-поток)", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                AppLog.Error("Необработанное исключение (фон)", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Error("Необработанное исключение (Task)", args.Exception);
            args.SetObserved();
        };
        AppLog.ErrorRaised += message =>
        {
            Current?.Dispatcher.Invoke(() =>
                _trayIcon?.ShowBalloonTip(4000, "SircleToSearch", message, WinForms.ToolTipIcon.Error));
        };

        _singleInstanceMutex = new Mutex(true, "SircleToSearch_SingleInstance", out var isNew);
        if (!isNew)
        {
            System.Windows.MessageBox.Show(Strings.Get("AlreadyRunning"),
                "SircleToSearch", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        SetupTrayIcon();

        _hotkeyManager = new HotkeyManager();
        _hotkeyManager.HotkeyPressed += ShowOverlay;

        if (!AppSettings.Current.FirstRunCompleted)
        {
            OpenSettings();
        }
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => ShowOverlay();

        RebuildTrayMenu();
    }

    private void RebuildTrayMenu()
    {
        if (_trayIcon is null) return;

        var menu = new WinForms.ContextMenuStrip();

        var settingsItem = new WinForms.ToolStripMenuItem(Strings.Get("MenuSettings"));
        settingsItem.Click += (_, _) => OpenSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new WinForms.ToolStripSeparator());

        var triggerItem = new WinForms.ToolStripMenuItem(Strings.Get("MenuFindNow"));
        triggerItem.Click += (_, _) => ShowOverlay();
        menu.Items.Add(triggerItem);

        menu.Items.Add(new WinForms.ToolStripSeparator());

        var exitItem = new WinForms.ToolStripMenuItem(Strings.Get("MenuExit"));
        exitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(exitItem);

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.Text = Strings.Get("TrayTooltip");
    }

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow();
        _settingsWindow.LanguageChanged += RebuildTrayMenu;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ShowOverlay()
    {
        if (_activeOverlay is not null) return;

        Current.Dispatcher.Invoke(() =>
        {
            _activeOverlay = new OverlayWindow();
            _activeOverlay.Closed += (_, _) => _activeOverlay = null;
            _activeOverlay.Show();
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyManager?.Dispose();
        _trayIcon?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        base.OnExit(e);
    }
}
