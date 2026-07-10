using System.Net.Http.Json;

namespace ZRfrp.Server;

public sealed class AccountResolver
{
    private readonly AccountService _accounts;
    private readonly ServerOptions _options;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public AccountResolver(AccountService accounts, ServerOptions options)
    {
        _accounts = accounts;
        _options = options;
    }

    public async Task<UserAccount?> ResolveAsync(string accessToken, CancellationToken cancellationToken)
    {
        var local = _accounts.ValidateAccessToken(accessToken);
        if (local is not null)
        {
            return local;
        }
        if (!_options.Mode.Equals("node", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(_options.MasterUrl)
            || string.IsNullOrWhiteSpace(_options.MasterKey)
            || string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, _options.MasterUrl.TrimEnd('/') + "/api/peer/account/validate");
            request.Headers.Add("X-ZRfrp-Peer-Key", _options.MasterKey);
            request.Content = JsonContent.Create(new PeerAccountValidationRequest(accessToken));
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            var result = await response.Content.ReadFromJsonAsync<PeerAccountValidationResponse>(
                cancellationToken: cancellationToken);
            return result is null ? null : new UserAccount
            {
                Id = result.Id,
                Username = result.Username,
                Role = "customer",
                Enabled = true,
                TrafficQuotaBytes = result.TrafficQuotaBytes,
                TrafficUsedBytes = result.TrafficUsedBytes
            };
        }
        catch
        {
            return null;
        }
    }
}
