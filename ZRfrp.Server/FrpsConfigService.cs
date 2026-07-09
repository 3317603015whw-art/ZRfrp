using System.Text.RegularExpressions;

namespace ZRfrp.Server;

public sealed class FrpsConfigService
{
    private readonly FrpsManager _frps;
    private readonly ServerOptions _options;

    public FrpsConfigService(FrpsManager frps, ServerOptions options)
    {
        _frps = frps;
        _options = options;
    }

    public async Task<FrpsConfigModel> ReadModelAsync()
    {
        var text = await _frps.ReadConfigAsync();
        return new FrpsConfigModel(
            ReadString(text, "bindAddr", "0.0.0.0"),
            ReadInt(text, "bindPort", _options.FrpsBindPort),
            ReadString(text, "auth.token", ""),
            ReadString(text, "webServer.addr", "127.0.0.1"),
            ReadInt(text, "webServer.port", 7500),
            ReadString(text, "webServer.user", "zrfrp"),
            ReadString(text, "webServer.password", ""),
            ReadRange(text, "start", _options.PortRangeStart),
            ReadRange(text, "end", _options.PortRangeEnd),
            ReadBool(text, "enablePrometheus", true),
            ReadString(text, "log.level", "info"),
            ReadInt(text, "log.maxDays", 7));
    }

    public string Render(FrpsConfigModel model) => $$"""
bindAddr = "{{Escape(model.BindAddress)}}"
bindPort = {{model.BindPort}}

auth.method = "token"
auth.token = "{{Escape(model.AuthToken)}}"

webServer.addr = "{{Escape(model.DashboardAddress)}}"
webServer.port = {{model.DashboardPort}}
webServer.user = "{{Escape(model.DashboardUser)}}"
webServer.password = "{{Escape(model.DashboardPassword)}}"
enablePrometheus = {{model.EnablePrometheus.ToString().ToLowerInvariant()}}

allowPorts = [
  { start = {{model.PortRangeStart}}, end = {{model.PortRangeEnd}} }
]

[[httpPlugins]]
name = "zrfrp-policy"
addr = "127.0.0.1:7600"
path = "/frp-plugin"
ops = ["Login", "NewProxy", "CloseProxy", "Ping", "NewUserConn"]

log.to = "/var/log/zrfrp/frps.log"
log.level = "{{Escape(model.LogLevel)}}"
log.maxDays = {{model.LogMaxDays}}
""";

    private static string ReadString(string text, string key, string fallback)
    {
        var match = Regex.Match(text, $@"(?m)^\s*{Regex.Escape(key)}\s*=\s*""(?<v>(?:\\.|[^""])*)""");
        return match.Success ? match.Groups["v"].Value.Replace("\\\"", "\"") : fallback;
    }
    private static int ReadInt(string text, string key, int fallback)
    {
        var match = Regex.Match(text, $@"(?m)^\s*{Regex.Escape(key)}\s*=\s*(?<v>\d+)");
        return match.Success && int.TryParse(match.Groups["v"].Value, out var value) ? value : fallback;
    }
    private static bool ReadBool(string text, string key, bool fallback)
    {
        var match = Regex.Match(text, $@"(?m)^\s*{Regex.Escape(key)}\s*=\s*(?<v>true|false)", RegexOptions.IgnoreCase);
        return match.Success ? bool.Parse(match.Groups["v"].Value) : fallback;
    }
    private static int ReadRange(string text, string key, int fallback)
    {
        var match = Regex.Match(text, $@"\b{Regex.Escape(key)}\s*=\s*(?<v>\d+)");
        return match.Success && int.TryParse(match.Groups["v"].Value, out var value) ? value : fallback;
    }
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
