using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using DigitalSorter.Contracts;
using Microsoft.Extensions.Caching.Memory;

namespace DigitalSorter.Auth;

public sealed class GoogleOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Authority { get; set; } = "https://accounts.google.com";
    public bool Configured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

public sealed class GoogleAuthorizationRequiredException : Exception;
public sealed class GoogleServiceUnavailableException : Exception;
public sealed class AccountLocks
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    public SemaphoreSlim Get(string subject) => _locks.GetOrAdd(subject, _ => new(1, 1));
}

public sealed class GoogleTokens(IBrokerStore store, SecretCipher cipher, IHttpClientFactory clients,
    GoogleOptions options, IMemoryCache cache, AccountLocks locks, TimeProvider clock)
{
    public async Task SaveAsync(string subject, string name, string? refresh, string scopes, CancellationToken ct)
    {
        var account = await store.FindAccountAsync(subject, ct);
        if (account is not null && (account.NeedsAuthorization || account.ClientId != options.ClientId) && string.IsNullOrEmpty(refresh))
            throw new GoogleAuthorizationRequiredException();
        if (account is null)
        {
            if (string.IsNullOrEmpty(refresh)) throw new GoogleAuthorizationRequiredException();
            account = new() { Subject = subject };
        }
        account.Name = name;
        account.ClientId = options.ClientId;
        if (!string.IsNullOrEmpty(refresh)) account.RefreshCipher = cipher.Protect(refresh);
        account.Scopes = scopes;
        account.NeedsAuthorization = false;
        cache.Remove("google:" + subject);
        await store.SaveAccountAsync(account, ct);
    }

    public async Task<TokenResponse> GetAsync(string subject, CancellationToken ct, bool force = false)
    {
        var gate = locks.Get(subject);
        await gate.WaitAsync(ct);
        try
        {
            var account = await GetAuthorizedAccountAsync(subject, ct);
            if (!force && cache.TryGetValue<TokenResponse>("google:" + subject, out var current) && current!.ExpiresAt > clock.GetUtcNow().AddMinutes(1)) return current;
            var token = await RefreshAsync(account, ct);
            cache.Set("google:" + subject, token, TimeSpan.FromMinutes(55));
            return token;
        }
        catch (HttpRequestException) { throw new GoogleServiceUnavailableException(); }
        finally { gate.Release(); }
    }

    private async Task<GoogleAccount> GetAuthorizedAccountAsync(string subject, CancellationToken ct)
    {
        var account = await store.FindAccountAsync(subject, ct);
        if (account is null || account.NeedsAuthorization || account.ClientId != options.ClientId)
            throw new GoogleAuthorizationRequiredException();
        return account;
    }

    private async Task<TokenResponse> RefreshAsync(GoogleAccount account, CancellationToken ct)
    {
        using var response = await clients.CreateClient("google").PostAsync("https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = options.ClientId, ["client_secret"] = options.ClientSecret,
                ["refresh_token"] = cipher.Unprotect(account.RefreshCipher), ["grant_type"] = "refresh_token"
            }), ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = json.RootElement;
        await ValidateRefreshResponseAsync(response, root, account.Subject, ct);
        var token = new TokenResponse(root.GetProperty("access_token").GetString()!, clock.GetUtcNow().AddSeconds(root.GetProperty("expires_in").GetInt32()));
        if (root.TryGetProperty("refresh_token", out var rotated) && !string.IsNullOrEmpty(rotated.GetString()))
            await store.UpdateRefreshCipherAsync(account.Subject, cipher.Protect(rotated.GetString()!), ct);
        return token;
    }

    private async Task ValidateRefreshResponseAsync(HttpResponseMessage response, JsonElement root, string subject, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            if (root.TryGetProperty("error", out var error) && error.GetString() == "invalid_grant")
                await RejectAuthorizationAsync(subject, ct);
            throw new GoogleServiceUnavailableException();
        }
        if (root.TryGetProperty("scope", out var granted) && !granted.GetString()!.Split(' ').Contains("https://www.googleapis.com/auth/drive.readonly"))
            await RejectAuthorizationAsync(subject, ct);
    }

    private async Task RejectAuthorizationAsync(string subject, CancellationToken ct)
    {
        await store.RequireAuthorizationAsync(subject, ct);
        cache.Remove("google:" + subject);
        throw new GoogleAuthorizationRequiredException();
    }

    public async Task DisconnectAsync(string subject, CancellationToken ct)
    {
        var gate = locks.Get(subject);
        await gate.WaitAsync(ct);
        try
        {
            var account = await store.FindAccountAsync(subject, ct);
            if (account is not null)
            {
                using var response = await clients.CreateClient("google").PostAsync("https://oauth2.googleapis.com/revoke",
                    new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = cipher.Unprotect(account.RefreshCipher) }), ct);
                if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.BadRequest) throw new GoogleServiceUnavailableException();
            }
            await store.DeleteAccountAsync(subject, ct);
            cache.Remove("google:" + subject);
        }
        catch (HttpRequestException) { throw new GoogleServiceUnavailableException(); }
        finally { gate.Release(); }
    }
}
