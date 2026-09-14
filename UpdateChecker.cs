using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace SircleToSearch;

public static class UpdateChecker
{
    private const string LatestReleaseApiUrl =
        "https://api.github.com/repos/Yagon-Don/SircleToSearch/releases/latest";

    public sealed record Result(bool UpdateAvailable, string LatestVersion, string ReleaseUrl);

    /// <summary>Manual check only — never called automatically. GitHub API requires a User-Agent.</summary>
    public static async Task<Result> CheckAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SircleToSearch-UpdateChecker");

        var json = await client.GetStringAsync(LatestReleaseApiUrl);
        using var doc = JsonDocument.Parse(json);

        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        var url = doc.RootElement.TryGetProperty("html_url", out var urlProp)
            ? urlProp.GetString() ?? ""
            : "";
        var latestVersion = tag.TrimStart('v', 'V');

        return new Result(IsNewer(latestVersion, AppVersion.Current), latestVersion, url);
    }

    private static bool IsNewer(string latest, string current)
    {
        if (Version.TryParse(PadForVersion(latest), out var latestV)
            && Version.TryParse(PadForVersion(current), out var currentV))
        {
            return latestV > currentV;
        }
        // Fall back to a plain inequality check if either string isn't a parseable version.
        return !string.Equals(latest, current, StringComparison.OrdinalIgnoreCase);
    }

    private static string PadForVersion(string v) => v.Contains('.') ? v : v + ".0";
}
