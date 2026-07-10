using System.Net.Http.Headers;
using System.Text.Json;

namespace ZRfrp.Server;

public sealed class UpdateService
{
    private const string ReleasesUrl = "https://api.github.com/repos/masZR-art/ZRfrp/releases/latest";
    private readonly HttpClient _http;

    public UpdateService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ZRfrp-Server", CurrentVersion));
    }

    public static string CurrentVersion =>
        typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public async Task<object> CheckAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(ReleasesUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var tag = document.RootElement.GetProperty("tag_name").GetString() ?? "v0.0.0";
        var latest = tag.TrimStart('v');
        return new
        {
            currentVersion = CurrentVersion,
            latestVersion = latest,
            updateAvailable = Compare(latest, CurrentVersion) > 0,
            releaseUrl = document.RootElement.GetProperty("html_url").GetString()
        };
    }

    private static int Compare(string left, string right)
    {
        _ = Version.TryParse(left, out var leftVersion);
        _ = Version.TryParse(right, out var rightVersion);
        return Comparer<Version>.Default.Compare(leftVersion ?? new(), rightVersion ?? new());
    }
}
