using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using DigitalSorter.Auth;
using DigitalSorter.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DigitalSorter.IntegrationTests;

public sealed class ReplaceableBrokerStoreTests
{
    [Fact]
    public async Task OAuthPairingTokensAndLogoutWorkWithAStoreThatDoesNotUseEfOrSqlite()
    {
        using var factory = new MemoryStoreFactory();
        using var phone = factory.Client();
        using var tv = factory.Client();
        Assert.True(factory.Store.Initialized);
        Assert.Equal(HttpStatusCode.Redirect, (await factory.Login(phone)).StatusCode);
        var web = (await phone.GetFromJsonAsync<SessionInfo>("/api/session"))!;
        Assert.True(web.Authenticated);
        phone.DefaultRequestHeaders.Add("X-CSRF-TOKEN", web.CsrfToken);
        var pair = (await (await tv.PostAsJsonAsync("/api/pair/start", new PairingRequest("TV"))).Content.ReadFromJsonAsync<PairingStarted>())!;
        Assert.Equal("TV", (await phone.GetFromJsonAsync<PairingDetails>("/api/pair/details?code=" + pair.UserCode))!.DeviceName);
        Assert.Equal(HttpStatusCode.OK, (await phone.PostAsJsonAsync("/api/pair/confirm", new PairingConfirmation(pair.UserCode))).StatusCode);
        var poll = (await (await tv.PostAsJsonAsync("/api/pair/poll", new PairingPoll(pair.Id, pair.DeviceSecret))).Content.ReadFromJsonAsync<PairingResult>())!;
        Assert.NotNull(poll.DeviceToken);
        tv.DefaultRequestHeaders.Authorization = new("Bearer", poll.DeviceToken);
        Assert.True((await tv.GetFromJsonAsync<SessionInfo>("/api/session"))!.Authenticated);
        Assert.Equal(HttpStatusCode.OK, (await tv.PostAsJsonAsync("/api/token", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await tv.PostAsJsonAsync("/api/logout", new { })).StatusCode);
        Assert.False((await tv.GetFromJsonAsync<SessionInfo>("/api/session"))!.Authenticated);
        Assert.True((await phone.GetFromJsonAsync<SessionInfo>("/api/session"))!.Authenticated);
        Assert.Equal(HttpStatusCode.NoContent, (await phone.PostAsJsonAsync("/api/disconnect", new { })).StatusCode);
        Assert.False((await phone.GetFromJsonAsync<SessionInfo>("/api/session"))!.Authenticated);
        Assert.Null(await factory.Store.FindAccountAsync("controlled-user", default));
        Assert.False(File.Exists(Path.Combine(factory.DataDirectory, "broker.db")));
    }

    private sealed class MemoryStoreFactory : BrokerFactory
    {
        public MemoryBrokerStore Store { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBrokerStore>();
                services.RemoveAll<IBrokerStoreInitializer>();
                services.RemoveAll<BrokerDb>();
                services.AddScoped<BrokerDb>(_ => throw new InvalidOperationException("The application must use the storage boundary."));
                services.AddSingleton<IBrokerStore>(Store);
                services.AddSingleton<IBrokerStoreInitializer>(Store);
            });
        }
    }

    // Test-only replacement. Production continues to use the durable SQLite store.
    private sealed class MemoryBrokerStore : IBrokerStore, IBrokerStoreInitializer
    {
        private readonly ConcurrentDictionary<string, GoogleAccount> _accounts = new();
        private readonly ConcurrentDictionary<string, BrokerSession> _sessions = new();
        private readonly ConcurrentDictionary<string, Pairing> _pairings = new();
        private readonly object _pairingGate = new();
        public bool Initialized { get; private set; }
        public Task InitializeAsync(CancellationToken ct = default) { Initialized = true; return Task.CompletedTask; }
        public Task<GoogleAccount?> FindAccountAsync(string subject, CancellationToken ct) => Task.FromResult(_accounts.GetValueOrDefault(subject));
        public Task SaveAccountAsync(GoogleAccount account, CancellationToken ct) { _accounts[account.Subject] = account; return Task.CompletedTask; }
        public Task<bool> HasAuthorizedAccountAsync(string? subject, string clientId, CancellationToken ct) =>
            Task.FromResult(subject is not null && _accounts.TryGetValue(subject, out var account) && !account.NeedsAuthorization && account.ClientId == clientId);
        public Task RequireAuthorizationAsync(string subject, CancellationToken ct) { _accounts[subject].NeedsAuthorization = true; return Task.CompletedTask; }
        public Task UpdateRefreshCipherAsync(string subject, string cipher, CancellationToken ct) { _accounts[subject].RefreshCipher = cipher; return Task.CompletedTask; }
        public Task DeleteAccountAsync(string subject, CancellationToken ct)
        {
            _accounts.TryRemove(subject, out _);
            foreach (var session in _sessions.Values.Where(x => x.Subject == subject)) _sessions.TryRemove(session.Id, out _);
            foreach (var pairing in _pairings.Values.Where(x => x.Subject == subject)) _pairings.TryRemove(pairing.Id, out _);
            return Task.CompletedTask;
        }
        public Task<bool> HasWebSessionAsync(string? id, DateTimeOffset now, CancellationToken ct) =>
            Task.FromResult(id is not null && _sessions.TryGetValue(id, out var session) && session.Kind == "web" && session.ExpiresAt > now);
        public Task<BrokerSession?> FindDeviceSessionAsync(string secretHash, DateTimeOffset now, CancellationToken ct) =>
            Task.FromResult(_sessions.Values.SingleOrDefault(x => x.SecretHash == secretHash && x.Kind == "device" && x.ExpiresAt > now));
        public Task AddSessionAsync(BrokerSession session, CancellationToken ct) { _sessions[session.Id] = session; return Task.CompletedTask; }
        public Task DeleteSessionAsync(string? id, CancellationToken ct) { if (id is not null) _sessions.TryRemove(id, out _); return Task.CompletedTask; }
        public Task AddPairingAsync(Pairing pairing, DateTimeOffset now, CancellationToken ct)
        {
            foreach (var expired in _pairings.Values.Where(x => x.ExpiresAt < now)) _pairings.TryRemove(expired.Id, out _);
            foreach (var expired in _sessions.Values.Where(x => x.ExpiresAt < now)) _sessions.TryRemove(expired.Id, out _);
            _pairings[pairing.Id] = pairing;
            return Task.CompletedTask;
        }
        public Task<Pairing?> FindPairingAsync(string id, CancellationToken ct) => Task.FromResult(_pairings.GetValueOrDefault(id));
        public Task<Pairing?> FindPendingPairingAsync(string codeHash, DateTimeOffset now, CancellationToken ct) =>
            Task.FromResult(_pairings.Values.SingleOrDefault(x => x.CodeHash == codeHash && x.Status == "pending" && x.ExpiresAt > now));
        public async Task<bool> ApprovePairingAsync(string codeHash, string subject, DateTimeOffset now, CancellationToken ct)
        {
            var pairing = await FindPendingPairingAsync(codeHash, now, ct);
            if (pairing is null) return false;
            pairing.Status = "approved"; pairing.Subject = subject;
            return true;
        }
        public Task<bool> TryConsumePairingAsync(string id, BrokerSession session, CancellationToken ct)
        {
            lock (_pairingGate)
            {
                if (!_pairings.TryGetValue(id, out var pairing) || pairing.Status != "approved") return Task.FromResult(false);
                _sessions[session.Id] = session;
                pairing.Status = "consumed";
                return Task.FromResult(true);
            }
        }
    }
}
