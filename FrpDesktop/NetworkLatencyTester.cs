using System.Diagnostics;
using System.Net.Sockets;

namespace FrpDesktop;

public sealed record LatencyTestResult(bool Success, int? Milliseconds, string Message);

public static class NetworkLatencyTester
{
    private const int SampleCount = 4;

    public static async Task<LatencyTestResult> TestAsync(string host, int port, TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(host) || port <= 0)
        {
            return new LatencyTestResult(false, null, "未配置");
        }

        var samples = new List<int>(SampleCount);
        var lastFailure = "失败";

        for (var index = 0; index < SampleCount; index++)
        {
            var result = await TestOnceAsync(host, port, timeout);
            if (result.Success && result.Milliseconds is > 0)
            {
                samples.Add(result.Milliseconds.Value);
            }
            else
            {
                lastFailure = result.Message;
            }

            if (index < SampleCount - 1)
            {
                await Task.Delay(80);
            }
        }

        if (samples.Count == 0)
        {
            return new LatencyTestResult(false, null, lastFailure);
        }

        samples.Sort();
        var median = samples[samples.Count / 2];
        return new LatencyTestResult(true, median, samples.Count == SampleCount ? "正常" : "部分成功");
    }

    private static async Task<LatencyTestResult> TestOnceAsync(string host, int port, TimeSpan timeout)
    {
        try
        {
            using var client = new TcpClient();
            client.NoDelay = true;
            var stopwatch = Stopwatch.StartNew();
            await client.ConnectAsync(host, port).WaitAsync(timeout);
            stopwatch.Stop();

            return new LatencyTestResult(true, Math.Max(1, (int)stopwatch.ElapsedMilliseconds), "正常");
        }
        catch (TimeoutException)
        {
            return new LatencyTestResult(false, null, "超时");
        }
        catch (OperationCanceledException)
        {
            return new LatencyTestResult(false, null, "超时");
        }
        catch
        {
            return new LatencyTestResult(false, null, "失败");
        }
    }
}
