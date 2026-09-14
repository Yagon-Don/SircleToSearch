using System;
using System.Diagnostics;
using System.Windows;

namespace SircleToSearch;

public partial class SettingsWindow : Window
{
    private const string IssuesUrl = "https://github.com/Yagon-Don/SircleToSearch/issues/new";
    private const string RepoUrl = "https://github.com/Yagon-Don/SircleToSearch";
    private const string AuthorUrl = "https://github.com/Yagon-Don";
    private bool _loading = true;

    public event Action? LanguageChanged;

    public SettingsWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            LanguageCombo.SelectedIndex = AppSettings.Current.Language == "ru" ? 1 : 0;
            AutostartCheck.IsChecked = Autostart.IsEnabled();
            ApplyStrings();
            _loading = false;
        };
    }

    private void ApplyStrings()
    {
        Title = Strings.Get("SettingsTitle");
        TitleText.Text = "SircleToSearch";
        WelcomeText.Text = Strings.Get("SettingsWelcome");
        LanguageLabel.Text = Strings.Get("SettingsLanguage");
        AutostartCheck.Content = Strings.Get("SettingsAutostart");
        HotkeyInfoText.Text = Strings.Get("SettingsHotkeyInfo");
        ReportBugButton.Content = Strings.Get("SettingsReportBug");
        CloseButton.Content = Strings.Get("SettingsClose");
        GitHubButton.Content = Strings.Get("SettingsGitHub");
        AuthorButton.Content = Strings.Get("SettingsAuthor");
        BySomeoneText.Text = Strings.Get("SettingsBySomeone");
        VersionText.Text = Strings.Get("SettingsVersion", AppVersion.Current);
        CheckUpdateButton.Content = Strings.Get("SettingsCheckUpdate");
    }

    private void LanguageCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (LanguageCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;

        AppSettings.Current.Language = (string)item.Tag;
        AppSettings.Current.Save();
        ApplyStrings();
        LanguageChanged?.Invoke();
    }

    private void AutostartCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        var wantEnabled = AutostartCheck.IsChecked == true;
        if (!Autostart.TrySet(wantEnabled))
        {
            _loading = true;
            AutostartCheck.IsChecked = !wantEnabled;
            _loading = false;
            System.Windows.MessageBox.Show(this, Strings.Get("AutostartFailed"),
                "SircleToSearch", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = Strings.Get("SettingsCheckingUpdate");

        try
        {
            var result = await UpdateChecker.CheckAsync();
            if (result.UpdateAvailable)
            {
                UpdateStatusText.Text = Strings.Get("SettingsUpdateAvailable", result.LatestVersion);
                var downloadUrl = result.ReleaseUrl;
                UpdateStatusText.Cursor = System.Windows.Input.Cursors.Hand;
                UpdateStatusText.TextDecorations = TextDecorations.Underline;
                UpdateStatusText.MouseLeftButtonDown += (_, _) => OpenUrl(downloadUrl);
            }
            else
            {
                UpdateStatusText.Text = Strings.Get("SettingsUpToDate");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Проверка обновлений не удалась", ex);
            UpdateStatusText.Text = Strings.Get("SettingsUpdateCheckFailed");
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void ReportBugButton_Click(object sender, RoutedEventArgs e) => OpenUrl(IssuesUrl);
    private void GitHubButton_Click(object sender, RoutedEventArgs e) => OpenUrl(RepoUrl);
    private void AuthorButton_Click(object sender, RoutedEventArgs e) => OpenUrl(AuthorUrl);

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Error($"Не удалось открыть ссылку {url}", ex);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.FirstRunCompleted = true;
        AppSettings.Current.Save();
        Close();
    }
}
