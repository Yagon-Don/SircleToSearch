using System;
using System.IO;

namespace SircleToSearch;

public static class AppLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SircleToSearch", "app.log");

    public static event Action<string>? ErrorRaised;

    public static void Error(string message, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTime.Now:O} {message}: {ex}\n\n");
        }
        catch
        {
            // Logging must never itself throw and take down the app.
        }

        ErrorRaised?.Invoke(message);
    }

    /// <summary>Non-error diagnostic line — used for perf timing. Never raises a tray balloon.</summary>
    public static void Info(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTime.Now:O} {message}\n");
        }
        catch
        {
        }
    }
}
