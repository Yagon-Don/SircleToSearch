using System;
using System.IO;
using System.Text.Json;

namespace SircleToSearch;

public sealed class AppSettings
{
    public string Language { get; set; } = "en";
    public bool FirstRunCompleted { get; set; }

    // Default off: the direct-HttpClient upload is faster but sometimes gets Google to
    // show a captcha, because the request never actually visits google.com in a real
    // browser session first. The WebView2 fetch()-based path is slower but has not
    // triggered that.
    public bool FastSearch { get; set; }

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SircleToSearch", "settings.json");

    public static AppSettings Current { get; private set; } = Load();

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded is not null) return loaded;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Не удалось прочитать settings.json", ex);
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this));
        }
        catch (Exception ex)
        {
            AppLog.Error("Не удалось сохранить settings.json", ex);
        }
    }
}
