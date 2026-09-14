using System.Collections.Generic;

namespace SircleToSearch;

/// <summary>Tiny lookup-table localizer — English default, Russian fallback. No resx/satellite assemblies needed for two languages.</summary>
public static class Strings
{
    private static readonly Dictionary<string, (string En, string Ru)> Table = new()
    {
        ["OverlayHint"] = ("Drag a rectangle around what you want to search — Enter searches again, Esc closes",
                            "Тяни прямоугольник, двигай и меняй размер — Enter ищёт снова, Esc закрывает"),
        ["TrayTooltip"] = ("SircleToSearch — Win+Shift+Q", "SircleToSearch — Win+Shift+Q"),
        ["MenuSettings"] = ("Settings", "Настройки"),
        ["MenuAutostart"] = ("Launch with Windows", "Запускать со стартом Windows"),
        ["MenuFindNow"] = ("Search now (Win+Shift+Q)", "Найти сейчас (Win+Shift+Q)"),
        ["MenuExit"] = ("Exit", "Выход"),
        ["ResultHeaderIdle"] = ("Search with SircleToSearch", "Найти с SircleToSearch"),
        ["ResultHeaderSearching"] = ("Searching...", "Ищу..."),
        ["AlreadyRunning"] = ("SircleToSearch is already running — check the tray.",
                               "SircleToSearch уже запущен — смотри в трее."),
        ["HotkeyFailed"] = ("Couldn't register Win+Shift+Q — another program is already using it (Win32 error: {0}).",
                             "Не удалось зарегистрировать Win+Shift+Q — хоткей уже занят другой программой (код ошибки Win32: {0})."),
        ["AutostartFailed"] = ("Couldn't update autostart in the registry.", "Не удалось изменить автозагрузку в реестре."),

        ["SettingsTitle"] = ("SircleToSearch Settings", "Настройки SircleToSearch"),
        ["SettingsWelcome"] = ("Welcome! SircleToSearch runs in the tray and activates with Win+Shift+Q.",
                                "Добро пожаловать! SircleToSearch сидит в трее и активируется по Win+Shift+Q."),
        ["SettingsLanguage"] = ("Language", "Язык"),
        ["SettingsAutostart"] = ("Launch with Windows", "Запускать со стартом Windows"),
        ["SettingsReportBug"] = ("Report a bug", "Пожаловаться на баг"),
        ["SettingsHotkeyInfo"] = ("Hotkey: Win+Shift+Q", "Хоткей: Win+Shift+Q"),
        ["SettingsClose"] = ("Done", "Готово"),
        ["SettingsVersion"] = ("Version {0}", "Версия {0}"),
        ["SettingsCheckUpdate"] = ("Check for updates", "Проверить обновления"),
        ["SettingsCheckingUpdate"] = ("Checking...", "Проверяю..."),
        ["SettingsUpToDate"] = ("You're on the latest version", "У тебя последняя версия"),
        ["SettingsUpdateAvailable"] = ("Version {0} is available", "Доступна версия {0}"),
        ["SettingsDownload"] = ("Download", "Скачать"),
        ["SettingsUpdateCheckFailed"] = ("Couldn't check for updates", "Не удалось проверить обновления"),
        ["SettingsGitHub"] = ("GitHub", "GitHub"),
        ["SettingsAuthor"] = ("Author", "Автор"),
        ["SettingsBySomeone"] = ("by Kiki", "от Kiki"),
    };

    public static string Get(string key)
    {
        if (!Table.TryGetValue(key, out var pair)) return key;
        return AppSettings.Current.Language == "ru" ? pair.Ru : pair.En;
    }

    public static string Get(string key, params object[] args) => string.Format(Get(key), args);
}
