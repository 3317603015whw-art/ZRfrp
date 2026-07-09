namespace ZRfrp.Server;

public sealed class AccountService
{
    private readonly StateStore _store;
    private readonly ServerOptions _options;

    public AccountService(StateStore store, ServerOptions options)
    {
        _store = store;
        _options = options;
    }

    public UserAccount? ValidatePassword(string username, string password) =>
        _store.State.Accounts.FirstOrDefault(account =>
            account.Enabled
            && account.Username.Equals(username.Trim(), StringComparison.OrdinalIgnoreCase)
            && Security.Verify(password, account.PasswordHash));

    public async Task<(UserAccount Account, string Token, DateTimeOffset ExpiresAt)> CreateSessionAsync(
        UserAccount account)
    {
        var token = Security.CreateSecret(32);
        var expiresAt = DateTimeOffset.UtcNow.AddHours(Math.Max(1, _options.SessionHours));
        _store.State.AccountSessions.RemoveAll(session => session.ExpiresAt <= DateTimeOffset.UtcNow);
        _store.State.AccountSessions.Add(new AccountSession
        {
            AccountId = account.Id,
            TokenHash = Security.HashToken(token),
            ExpiresAt = expiresAt
        });
        await _store.SaveAsync();
        return (account, token, expiresAt);
    }

    public UserAccount? ValidateAccessToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }
        var now = DateTimeOffset.UtcNow;
        foreach (var session in _store.State.AccountSessions.Where(item => item.ExpiresAt > now))
        {
            if (Security.VerifyToken(token, session.TokenHash))
            {
                return _store.State.Accounts.FirstOrDefault(account => account.Id == session.AccountId && account.Enabled);
            }
        }
        return null;
    }

    public UserAccount? Find(string id) => _store.State.Accounts.FirstOrDefault(account => account.Id == id);

    public bool IsQuotaExceeded(UserAccount account) =>
        account.TrafficQuotaBytes > 0 && account.TrafficUsedBytes >= account.TrafficQuotaBytes;
}
