using System.Net.Http.Json;

namespace ZRfrp.Server;

public sealed class NodeHeartbeatService : BackgroundService
{
    private readonly ServerOptions _options;
    private readonly FrpsManager _frps;
    private readonly ILogger<NodeHeartbeatService> _logger;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public NodeHeartbeatService(
        ServerOptions options,
        FrpsManager frps,
        ILogger<NodeHeartbeatService> logger)
    {
        _options = options;
        _frps = frps;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.MasterUrl) || string.IsNullOrWhiteSpace(_options.MasterKey))
        {
            if (_options.Mode.Equals("node", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError(
                    "节点模式缺少 MasterUrl 或 MasterKey，心跳服务未启动。请检查 /etc/zrfrp/zrfrp.env。");
            }
            return;
        }

        var failureCount = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var frpsOnline = await _frps.IsReachableAsync(stoppingToken);
                var heartbeat = new NodeHeartbeat(
                    string.IsNullOrWhiteSpace(_options.NodeId) ? Environment.MachineName : _options.NodeId,
                    string.IsNullOrWhiteSpace(_options.NodeName) ? "ZRfrp 节点" : _options.NodeName,
                    _options.PublicHost,
                    $"http://{_options.PublicHost}:7600",
                    _options.FrpsBindPort,
                    frpsOnline,
                    frpsOnline,
                    0,
                    0,
                    UpdateService.CurrentVersion,
                    _options.FrpAuthToken);
                using var request = new HttpRequestMessage(
                    HttpMethod.Post, _options.MasterUrl.TrimEnd('/') + "/api/peer/heartbeat");
                request.Headers.Add("X-ZRfrp-Peer-Key", _options.MasterKey);
                request.Content = JsonContent.Create(heartbeat);
                using var response = await _http.SendAsync(request, stoppingToken);
                if (!response.IsSuccessStatusCode)
                {
                    var detail = await response.Content.ReadAsStringAsync(stoppingToken);
                    throw new HttpRequestException(
                        $"主控返回 HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {detail}");
                }
                if (failureCount > 0)
                {
                    _logger.LogInformation("节点已恢复向主控 {MasterUrl} 上报心跳。", _options.MasterUrl);
                }
                failureCount = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                failureCount++;
                if (failureCount == 1 || failureCount % 4 == 0)
                {
                    _logger.LogWarning(
                        exception,
                        "向主控 {MasterUrl} 上报节点心跳失败（连续 {FailureCount} 次）。",
                        _options.MasterUrl,
                        failureCount);
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
}
