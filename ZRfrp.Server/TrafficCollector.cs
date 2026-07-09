using System.Text.Json;

namespace ZRfrp.Server;

public sealed class TrafficCollector : BackgroundService
{
    private readonly FrpsManager _frps;
    private readonly StateStore _store;

    public TrafficCollector(FrpsManager frps, StateStore store)
    {
        _frps = frps;
        _store = store;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var type in new[] { "tcp", "udp", "http", "https" })
                {
                    var json = await _frps.GetDashboardJsonAsync($"/api/proxy/{type}", stoppingToken);
                    if (json is not null)
                    {
                        Collect(type, json.Value);
                    }
                }
                await _store.SaveAsync();
            }
            catch
            {
                // Dashboard availability is reflected by the overview endpoint.
            }
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
    }

    private void Collect(string type, JsonElement element)
    {
        foreach (var proxy in EnumerateObjects(element))
        {
            var name = ReadString(proxy, "name", "proxy_name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }
            var account = _store.State.Accounts
                .Where(item => item.Role == "customer")
                .FirstOrDefault(item => name.StartsWith(item.Id + ".", StringComparison.Ordinal));
            if (account is null)
            {
                continue;
            }
            var current = ReadLong(proxy, "trafficIn", "traffic_in")
                + ReadLong(proxy, "trafficOut", "traffic_out");
            var key = $"{type}:{name}";
            _store.State.TrafficSnapshots.TryGetValue(key, out var previous);
            if (current >= previous)
            {
                account.TrafficUsedBytes += current - previous;
            }
            _store.State.TrafficSnapshots[key] = current;
        }
    }

    private static IEnumerable<JsonElement> EnumerateObjects(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            yield return element;
            foreach (var property in element.EnumerateObject())
            {
                foreach (var nested in EnumerateObjects(property.Value))
                {
                    yield return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in EnumerateObjects(item))
                {
                    yield return nested;
                }
            }
        }
    }

    private static string ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }
        }
        return "";
    }

    private static long ReadLong(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.TryGetInt64(out var result))
            {
                return result;
            }
        }
        return 0;
    }
}
