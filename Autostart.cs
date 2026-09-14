using System;
using System.IO;
using Microsoft.Win32;

namespace SircleToSearch;

public static class Autostart
{
    private const string AppName = "SircleToSearch";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(AppName) is not null;
    }

    public static bool TrySet(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null) return false;

            if (enabled)
            {
                // Environment.ProcessPath resolves to the actual launched .exe (the
                // self-contained single-file build), not a temp self-extraction path —
                // that's what needs to survive into the Run key.
                var exePath = Environment.ProcessPath
                    ?? Path.Combine(AppContext.BaseDirectory, "SircleToSearch.exe");
                if (!File.Exists(exePath)) return false;

                key.SetValue(AppName, $"\"{exePath}\"");
                return string.Equals(key.GetValue(AppName) as string, $"\"{exePath}\"", StringComparison.Ordinal);
            }

            key.DeleteValue(AppName, throwOnMissingValue: false);
            return key.GetValue(AppName) is null;
        }
        catch (Exception ex)
        {
            AppLog.Error("Не удалось изменить автозагрузку", ex);
            return false;
        }
    }
}
