using System.Diagnostics;
using System.IO;
using System.Text;

namespace FrpDesktop;

public sealed class FrpProcessRunner
{
    private readonly object _syncRoot = new();
    private Process? _process;

    public event Action<string>? LogReceived;
    public event Action<bool>? RunningChanged;

    public bool IsRunning
    {
        get
        {
            lock (_syncRoot)
            {
                return _process is { HasExited: false };
            }
        }
    }

    public async Task<ProcessResult> VerifyAsync(string frpcPath, string configPath, CancellationToken cancellationToken)
    {
        using var process = CreateProcess(frpcPath, workingDirectory: Path.GetDirectoryName(frpcPath));
        process.StartInfo.ArgumentList.Add("verify");
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add(configPath);

        var output = new StringBuilder();
        process.OutputDataReceived += (_, args) => AppendLine(output, args.Data);
        process.ErrorDataReceived += (_, args) => AppendLine(output, args.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var completed = await WaitForExitAsync(process, TimeSpan.FromSeconds(20), cancellationToken);
        if (!completed)
        {
            TryKill(process);
            return new ProcessResult(false, "配置校验超时，请检查 frpc.exe 是否可正常运行。");
        }

        return new ProcessResult(process.ExitCode == 0, output.ToString().Trim());
    }

    public void Start(string frpcPath, string configPath)
    {
        if (IsRunning)
        {
            LogReceived?.Invoke("frpc 已经在运行。");
            return;
        }

        var workingDirectory = Path.GetDirectoryName(frpcPath);
        var process = CreateProcess(frpcPath, workingDirectory);
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add(configPath);
        process.EnableRaisingEvents = true;

        process.OutputDataReceived += (_, args) => RaiseLog(args.Data);
        process.ErrorDataReceived += (_, args) => RaiseLog(args.Data);
        process.Exited += (_, _) =>
        {
            var exitCode = GetExitCode(process);
            lock (_syncRoot)
            {
                if (ReferenceEquals(_process, process))
                {
                    _process = null;
                }
            }

            RaiseLog($"frpc 已退出，退出码 {exitCode}。");
            process.Dispose();
            RunningChanged?.Invoke(false);
        };

        process.Start();
        lock (_syncRoot)
        {
            _process = process;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        RunningChanged?.Invoke(true);
        LogReceived?.Invoke($"frpc 已启动，配置文件：{configPath}");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Process? process;
        lock (_syncRoot)
        {
            process = _process;
        }

        if (process is null || process.HasExited)
        {
            LogReceived?.Invoke("frpc 当前未运行。");
            RunningChanged?.Invoke(false);
            return;
        }

        LogReceived?.Invoke("正在停止 frpc。");
        await StopProcessAsync(process, cancellationToken);
    }

    private static Process CreateProcess(string fileName, string? workingDirectory)
    {
        return new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                    ? Environment.CurrentDirectory
                    : workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            }
        };
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var exitTask = process.WaitForExitAsync(cancellationToken);
        var delayTask = Task.Delay(timeout, cancellationToken);
        var completedTask = await Task.WhenAny(exitTask, delayTask);
        return completedTask == exitTask;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch
        {
            // Process may already be gone.
        }
    }

    private static async Task StopProcessAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            process.Kill(entireProcessTree: true);
            var exitTask = process.WaitForExitAsync(cancellationToken);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            await Task.WhenAny(exitTask, timeoutTask);
        }
        catch
        {
            // Process may already be gone or access may be denied during shutdown.
        }
    }

    private static string GetExitCode(Process process)
    {
        try
        {
            return process.ExitCode.ToString();
        }
        catch
        {
            return "未知";
        }
    }

    private static void AppendLine(StringBuilder builder, string? line)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            builder.AppendLine(line);
        }
    }

    private void RaiseLog(string? line)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            LogReceived?.Invoke(line);
        }
    }
}

public sealed record ProcessResult(bool Success, string Output);
