using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FrpDesktop;

public sealed class FrpEnvironmentService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/fatedier/frp/releases/latest";
    private const string FallbackVersion = "0.69.1";

    private readonly string _runtimeDirectory;
    private HttpClient _httpClient = CreateHttpClient(new NetworkProxyOptions("none", "HTTP", "", 0, "", ""));

    public FrpEnvironmentService(string appDataDirectory)
    {
        _runtimeDirectory = Path.Combine(appDataDirectory, "runtime");
    }

    public void ConfigureProxy(NetworkProxyOptions proxyOptions)
    {
        var oldClient = _httpClient;
        _httpClient = CreateHttpClient(proxyOptions);
        oldClient.Dispose();
    }

    public Task<string?> DetectAsync(IEnumerable<string?> configuredPaths, CancellationToken cancellationToken = default)
    {
        var paths = configuredPaths.ToArray();
        return Task.Run(() => Detect(paths, cancellationToken), cancellationToken);
    }

    public async Task<string> GetVersionAsync(string frpcPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(frpcPath))
        {
            return "";
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = frpcPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("--version");

        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = string.Join(" ", await outputTask, await errorTask).Trim();
            return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        }
        catch
        {
            return "";
        }
    }

    public async Task<FrpInstallResult> InstallLatestAsync(
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_runtimeDirectory);
        var release = await ResolveLatestReleaseAsync(cancellationToken);
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "ZRfrp", Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(temporaryDirectory, "frp.zip");
        var extractDirectory = Path.Combine(temporaryDirectory, "extracted");

        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            progress?.Report(0);
            await DownloadAsync(release.DownloadUrl, archivePath, progress, cancellationToken);
            Directory.CreateDirectory(extractDirectory);
            ZipFile.ExtractToDirectory(archivePath, extractDirectory);

            var extractedFrpc = Directory
                .EnumerateFiles(extractDirectory, "frpc.exe", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (extractedFrpc is null)
            {
                throw new InvalidOperationException("下载包中没有找到 frpc.exe。");
            }

            var versionName = release.Version.TrimStart('v');
            var installDirectory = Path.Combine(_runtimeDirectory, $"frp_{versionName}_windows_amd64");
            Directory.CreateDirectory(installDirectory);
            var installedFrpc = Path.Combine(installDirectory, "frpc.exe");
            File.Copy(extractedFrpc, installedFrpc, overwrite: true);
            progress?.Report(100);

            return new FrpInstallResult(installedFrpc, versionName);
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporaryDirectory))
                {
                    Directory.Delete(temporaryDirectory, recursive: true);
                }
            }
            catch
            {
                // Temporary files can be cleaned by Windows if another process still holds them.
            }
        }
    }

    private string? Detect(IEnumerable<string?> configuredPaths, CancellationToken cancellationToken)
    {
        var candidates = new List<string>();

        foreach (var configuredPath in configuredPaths)
        {
            AddCandidate(candidates, configuredPath);
        }

        AddCandidate(candidates, Path.Combine(AppContext.BaseDirectory, "frpc.exe"));
        AddCandidate(candidates, Path.Combine(Environment.CurrentDirectory, "frpc.exe"));

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            AddCandidate(candidates, Path.Combine(directory.Trim('"'), "frpc.exe"));
        }

        AddFilesFromDirectory(candidates, _runtimeDirectory, recursive: true, cancellationToken);

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var searchDirectories = new[]
        {
            Path.Combine(userProfile, "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Path.Combine(userProfile, "OneDrive", "Desktop"),
            Path.Combine(userProfile, "OneDrive", "桌面"),
            @"C:\frp"
        };

        foreach (var directory in searchDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            AddFilesFromDownloadLocation(candidates, directory, cancellationToken);
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private static void AddFilesFromDownloadLocation(
        ICollection<string> candidates,
        string directory,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        AddCandidate(candidates, Path.Combine(directory, "frpc.exe"));

        try
        {
            foreach (var childDirectory in Directory.EnumerateDirectories(directory, "frp*"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddFilesFromDirectory(candidates, childDirectory, recursive: true, cancellationToken);
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
    }

    private static void AddFilesFromDirectory(
        ICollection<string> candidates,
        string directory,
        bool recursive,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        try
        {
            foreach (var path in Directory.EnumerateFiles(
                         directory,
                         "frpc.exe",
                         recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddCandidate(candidates, path);
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
    }

    private static void AddCandidate(ICollection<string> candidates, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
            if (!candidates.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(fullPath);
            }
        }
        catch
        {
            // Ignore malformed paths discovered from environment variables or old settings.
        }
    }

    private async Task<ReleaseDownload> ResolveLatestReleaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(LatestReleaseApi, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            var version = root.GetProperty("tag_name").GetString() ?? $"v{FallbackVersion}";

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (!name.EndsWith("_windows_amd64.zip", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var url = asset.GetProperty("browser_download_url").GetString();
                if (!string.IsNullOrWhiteSpace(url))
                {
                    return new ReleaseDownload(version, url);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Fall back to the version matching this desktop manager when GitHub API access is limited.
        }

        return new ReleaseDownload(
            $"v{FallbackVersion}",
            $"https://github.com/fatedier/frp/releases/download/v{FallbackVersion}/frp_{FallbackVersion}_windows_amd64.zip");
    }

    private async Task DownloadAsync(
        string url,
        string destinationPath,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var totalBytes = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(destinationPath);
        var buffer = new byte[81920];
        long downloadedBytes = 0;
        var lastProgress = -1;

        while (true)
        {
            var bytesRead = await source.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            downloadedBytes += bytesRead;

            if (totalBytes is > 0)
            {
                var currentProgress = (int)Math.Clamp(downloadedBytes * 100 / totalBytes.Value, 0, 99);
                if (currentProgress != lastProgress)
                {
                    lastProgress = currentProgress;
                    progress?.Report(currentProgress);
                }
            }
        }
    }

    private static HttpClient CreateHttpClient(NetworkProxyOptions proxyOptions)
    {
        var handler = new HttpClientHandler
        {
            UseProxy = false
        };

        var mode = proxyOptions.Mode.Trim().ToLowerInvariant();
        if (mode == "system")
        {
            handler.UseProxy = true;
            handler.UseDefaultCredentials = true;
        }
        else if (mode == "manual" && !string.IsNullOrWhiteSpace(proxyOptions.Host) && proxyOptions.Port > 0)
        {
            var normalizedAddress = NormalizeProxyAddress(proxyOptions.Type, proxyOptions.Host, proxyOptions.Port);
            handler.UseProxy = true;
            var proxy = new WebProxy(normalizedAddress);
            if (!string.IsNullOrWhiteSpace(proxyOptions.Username))
            {
                proxy.Credentials = new NetworkCredential(proxyOptions.Username, proxyOptions.Password);
            }

            handler.Proxy = proxy;
        }

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ZRfrp", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static string NormalizeProxyAddress(string proxyType, string proxyHost, int proxyPort)
    {
        var scheme = proxyType.Trim().ToLowerInvariant() switch
        {
            "https" => "https",
            "socks4" => "socks4",
            "socks5" => "socks5",
            _ => "http"
        };

        return $"{scheme}://{proxyHost.Trim()}:{proxyPort}";
    }

    private sealed record ReleaseDownload(string Version, string DownloadUrl);
}

public sealed record FrpInstallResult(string FrpcPath, string Version);

public sealed record NetworkProxyOptions(
    string Mode,
    string Type,
    string Host,
    int Port,
    string Username,
    string Password);
