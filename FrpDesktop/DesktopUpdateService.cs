using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace FrpDesktop;

public sealed record DesktopUpdateInfo(
    string CurrentVersion, string LatestVersion, bool UpdateAvailable, string DownloadUrl);

public sealed class DesktopUpdateService
{
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/masZR-art/ZRfrp/releases/latest";
    private readonly HttpClient _http = new(new HttpClientHandler { UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public DesktopUpdateService()
    {
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("ZRfrp-Desktop", CurrentVersion));
    }

    public static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public async Task<DesktopUpdateInfo> CheckAsync()
    {
        using var response = await _http.GetAsync(LatestReleaseUrl);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var latest = (document.RootElement.GetProperty("tag_name").GetString() ?? "v0.0.0")
            .TrimStart('v');
        var asset = document.RootElement.GetProperty("assets").EnumerateArray()
            .FirstOrDefault(item =>
                (item.GetProperty("name").GetString() ?? "")
                .Equals($"ZRfrp-Desktop-v{latest}-win-x64.zip", StringComparison.OrdinalIgnoreCase));
        var downloadUrl = asset.ValueKind == JsonValueKind.Undefined
            ? ""
            : asset.GetProperty("browser_download_url").GetString() ?? "";
        return new DesktopUpdateInfo(
            CurrentVersion, latest, Compare(latest, CurrentVersion) > 0 && downloadUrl.Length > 0, downloadUrl);
    }

    public async Task DownloadAndApplyAsync(DesktopUpdateInfo update, string dataDirectory)
    {
        var updateDirectory = Path.Combine(dataDirectory, "update", update.LatestVersion);
        var extractDirectory = Path.Combine(updateDirectory, "files");
        Directory.CreateDirectory(updateDirectory);
        if (Directory.Exists(extractDirectory))
        {
            Directory.Delete(extractDirectory, true);
        }
        Directory.CreateDirectory(extractDirectory);

        var archivePath = Path.Combine(updateDirectory, "update.zip");
        await using (var source = await _http.GetStreamAsync(update.DownloadUrl))
        await using (var target = File.Create(archivePath))
        {
            await source.CopyToAsync(target);
        }
        ZipFile.ExtractToDirectory(archivePath, extractDirectory, true);

        var installDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var executableName = Path.GetFileName(Environment.ProcessPath);
        if (string.IsNullOrWhiteSpace(executableName))
        {
            executableName = "ZRfrp.exe";
        }
        var stagedExecutable = Path.Combine(extractDirectory, executableName);
        if (!File.Exists(stagedExecutable))
        {
            stagedExecutable = Path.Combine(extractDirectory, "ZRfrp.exe");
        }
        if (!File.Exists(stagedExecutable))
        {
            throw new InvalidDataException("更新包中缺少 ZRfrp.exe。");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = stagedExecutable,
            WorkingDirectory = extractDirectory,
            UseShellExecute = true
        };
        if (!CanWriteToDirectory(installDirectory))
        {
            startInfo.Verb = "runas";
        }
        startInfo.ArgumentList.Add("--apply-update");
        startInfo.ArgumentList.Add("--target-process-id");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add("--install-directory");
        startInfo.ArgumentList.Add(installDirectory);
        startInfo.ArgumentList.Add("--executable-name");
        startInfo.ArgumentList.Add(Path.GetFileName(stagedExecutable));
        startInfo.ArgumentList.Add("--update-log");
        startInfo.ArgumentList.Add(Path.Combine(updateDirectory, "apply-update.log"));
        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动独立更新进程。");
    }

    public static bool IsStagedUpdaterCommand(IReadOnlyList<string> arguments) =>
        arguments.Any(argument =>
            argument.Equals("--apply-update", StringComparison.OrdinalIgnoreCase));

    public static int RunStagedUpdater(IReadOnlyList<string> arguments)
    {
        var values = ParseUpdaterArguments(arguments);
        var logPath = values.GetValueOrDefault("--update-log")
            ?? Path.Combine(Path.GetTempPath(), "zrfrp-update.log");
        var installDirectory = values.GetValueOrDefault("--install-directory") ?? "";
        var executableName = values.GetValueOrDefault("--executable-name") ?? "ZRfrp.exe";
        var targetExecutable = Path.Combine(installDirectory, executableName);

        try
        {
            if (!int.TryParse(values.GetValueOrDefault("--target-process-id"), out var processId)
                || processId <= 0 || string.IsNullOrWhiteSpace(installDirectory))
            {
                throw new InvalidDataException("更新器启动参数无效。");
            }

            WaitForTargetExit(processId);
            CopyUpdateFiles(AppContext.BaseDirectory, installDirectory);
            if (!File.Exists(targetExecutable))
            {
                throw new FileNotFoundException("更新后找不到主程序。", targetExecutable);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? Path.GetTempPath());
            File.WriteAllText(logPath, $"更新完成：{DateTimeOffset.Now:O}{Environment.NewLine}");
            StartInstalledApplication(targetExecutable, installDirectory);
            return 0;
        }
        catch (Exception exception)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? Path.GetTempPath());
                File.WriteAllText(logPath, exception.ToString());
            }
            catch
            {
                // Preserve the original updater failure.
            }

            // A failed update must not leave the application silently closed.
            try
            {
                if (File.Exists(targetExecutable))
                {
                    StartInstalledApplication(targetExecutable, installDirectory);
                }
            }
            catch
            {
                // The updater log remains available for diagnosis.
            }
            return 1;
        }
    }

    private static Dictionary<string, string> ParseUpdaterArguments(IReadOnlyList<string> arguments)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index + 1 < arguments.Count; index++)
        {
            if (arguments[index].StartsWith("--", StringComparison.Ordinal)
                && !arguments[index].Equals("--apply-update", StringComparison.OrdinalIgnoreCase))
            {
                result[arguments[index]] = arguments[++index];
            }
        }
        return result;
    }

    private static void WaitForTargetExit(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.WaitForExit(45_000))
            {
                process.Kill(true);
                process.WaitForExit(10_000);
            }
        }
        catch (ArgumentException)
        {
            // The old application already exited.
        }
    }

    private static void CopyUpdateFiles(string sourceDirectory, string installDirectory)
    {
        Directory.CreateDirectory(installDirectory);
        foreach (var source in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, source);
            var destination = Path.Combine(installDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? installDirectory);

            Exception? lastError = null;
            for (var attempt = 0; attempt < 40; attempt++)
            {
                try
                {
                    File.Copy(source, destination, true);
                    lastError = null;
                    break;
                }
                catch (IOException exception)
                {
                    lastError = exception;
                    Thread.Sleep(250);
                }
                catch (UnauthorizedAccessException exception)
                {
                    lastError = exception;
                    Thread.Sleep(250);
                }
            }
            if (lastError is not null)
            {
                throw new IOException($"无法更新文件 {relativePath}。", lastError);
            }
        }
    }

    private static void StartInstalledApplication(string executable, string workingDirectory) =>
        Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true
        });

    private static bool CanWriteToDirectory(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, $".zrfrp-update-{Guid.NewGuid():N}.tmp");
            using (new FileStream(
                       probe, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       1, FileOptions.DeleteOnClose))
            {
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    private static int Compare(string left, string right)
    {
        _ = Version.TryParse(left, out var leftVersion);
        _ = Version.TryParse(right, out var rightVersion);
        return Comparer<Version>.Default.Compare(leftVersion ?? new(), rightVersion ?? new());
    }
}
