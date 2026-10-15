using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using DigitalSorter.Auth;
using DigitalSorter.Contracts;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalSorter.IntegrationTests;

public sealed class BrokerTests
{
    private static async Task<SessionInfo> Session(HttpClient client)
    {
        var session = (await client.GetFromJsonAsync<SessionInfo>("/api/session"))!;
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", session.CsrfToken);
        return session;
    }
    private static Task<HttpResponseMessage> Post(HttpClient client, string path) => client.PostAsJsonAsync(path, new { });

    [Fact]
    public async Task OAuthCodeExchangeValidatesPkceAndStoresOnlyEncryptedRefreshToken()
    {
        using var factory = new BrokerFactory(); using var client = factory.Client();
        var response = await factory.Login(client);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(response.Headers.Location!.OriginalString == "/", factory.Failure);
        Assert.True((await Session(client)).Authenticated);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-DigitalSorter.Session="));
        Assert.Contains("secure", cookie); Assert.Contains("httponly", cookie);
        using var scope = factory.Services.CreateScope();
        var account = await scope.ServiceProvider.GetRequiredService<BrokerDb>().Accounts.SingleAsync();
        Assert.Equal(ControlledGoogle.Client, account.ClientId);
        Assert.DoesNotContain("controlled-refresh-token", account.RefreshCipher);
        Assert.Equal("controlled-refresh-token", scope.ServiceProvider.GetRequiredService<SecretCipher>().Unprotect(account.RefreshCipher));
        Assert.DoesNotContain("controlled-refresh-token", cookie);
        Assert.DoesNotContain("controlled-access-token", cookie);
    }
    [Fact]
    public async Task OAuthRejectsInvalidStateAndDoesNotCreateAccount()
    {
        using var factory = new BrokerFactory(); using var client = factory.Client();
        var response = await client.GetAsync("/auth/callback?code=invalid&state=tampered");
        Assert.Contains("error=oauth", response.Headers.Location!.OriginalString);
        Assert.False((await Session(client)).Authenticated);
    }
    [Fact]
    public async Task OAuthRejectsInvalidNonce()
    {
        using var factory = new BrokerFactory(); factory.Google.NonceOverride = "wrong-nonce"; using var client = factory.Client();
        var response = await factory.Login(client);
        Assert.Contains("error=oauth", response.Headers.Location!.OriginalString);
        Assert.False((await Session(client)).Authenticated);
    }
    [Fact]
    public async Task OAuthRequiresReadOnlyDrivePermission()
    {
        using var factory = new BrokerFactory(); factory.Google.GrantedScopes = "openid"; using var client = factory.Client();
        var response = await factory.Login(client);
        Assert.Contains("error=permission", response.Headers.Location!.OriginalString);
        Assert.False((await Session(client)).Authenticated);
    }
    [Fact]
    public async Task MissingRefreshTokenIsPreservedForExistingAccount()
    {
        using var factory = new BrokerFactory(); using var client = factory.Client();
        await factory.Login(client); factory.Google.IncludeRefresh = false;
        await factory.Login(client);
        Assert.True((await Session(client)).Authenticated);
        Assert.Equal(HttpStatusCode.OK, (await Post(client, "/api/token")).StatusCode);
    }
    [Fact]
    public async Task ChangingOAuthClientRequiresNewConsentBeforeUsingStoredRefreshToken()
    {
        using var factory = new BrokerFactory(); using var client = factory.Client();
        await factory.Login(client); await Session(client);
        factory.Services.GetRequiredService<GoogleOptions>().ClientId = "replacement-client";
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "/api/token")).StatusCode);
        Assert.Equal(0, factory.Google.RefreshCalls);
        using var scope = factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<GoogleTokens>();
        await Assert.ThrowsAsync<GoogleAuthorizationRequiredException>(() => tokens.SaveAsync(
            "controlled-user", "Test", null, "openid https://www.googleapis.com/auth/drive.readonly", CancellationToken.None));
    }
    [Fact]
    public async Task NewAccountWithoutRefreshTokenRequiresConsent()
    {
        using var factory = new BrokerFactory(); factory.Google.IncludeRefresh = false; using var client = factory.Client();
        var response = await factory.Login(client);
        Assert.Contains("error=consent", response.Headers.Location!.OriginalString);
        Assert.False((await Session(client)).Authenticated);
    }
    [Fact]
    public async Task TokenRequiresAuthenticationAndCsrfForBrowser()
    {
        using var factory = new BrokerFactory(); using var client = factory.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "/api/token")).StatusCode);
        await factory.Login(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(client, "/api/token")).StatusCode);
        await Session(client);
        var token = await (await Post(client, "/api/token")).Content.ReadFromJsonAsync<TokenResponse>();
        Assert.Equal("controlled-access-token", token!.AccessToken);
        Assert.Equal("no-store", (await Post(client, "/api/token")).Headers.CacheControl!.ToString());
    }
    [Fact]
    public async Task InvalidRefreshTokenCannotBeReactivatedWithoutNewConsent()
    {
        using var factory = new BrokerFactory(); using var client = factory.Client();
        await factory.Login(client); await Session(client); factory.Google.RefreshError = "invalid_grant";
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "/api/token")).StatusCode);
        factory.Google.IncludeRefresh = false;
        var response = await factory.Login(client);
        Assert.Contains("error=consent", response.Headers.Location!.OriginalString);
    }
    [Fact]
    public async Task ConcurrentPairingPollsIssueOnlyOneDeviceCredential()
    {
        using var factory = new BrokerFactory(); using var tv = factory.Client(); using var phone = factory.Client();
        var pair = (await (await tv.PostAsJsonAsync("/api/pair/start", new PairingRequest("TV"))).Content.ReadFromJsonAsync<PairingStarted>())!;
        await factory.Login(phone); await Session(phone);
        await phone.PostAsJsonAsync("/api/pair/confirm", new PairingConfirmation(pair.UserCode));
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => tv.PostAsJsonAsync("/api/pair/poll", new PairingPoll(pair.Id, pair.DeviceSecret))));
        var results = await Task.WhenAll(responses.Select(x => x.Content.ReadFromJsonAsync<PairingResult>()));
        Assert.Single(results, x => x!.DeviceToken is not null);
        Assert.Equal(5, results.Count(x => x!.Status == "consumed"));
    }
    [Fact]
    public async Task ConcurrentRefreshIsSharedBetweenSessions()
    {
        using var factory = new BrokerFactory(); using var client = factory.Client();
        await factory.Login(client); await Session(client);
        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Post(client, "/api/token")));
        Assert.All(responses, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));
        Assert.Equal(1, factory.Google.RefreshCalls);
        factory.Clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(HttpStatusCode.OK, (await Post(client, "/api/token")).StatusCode);
        Assert.Equal(2, factory.Google.RefreshCalls);
    }
    [Fact]
    public async Task InvalidGrantRequiresReauthorizationWhileNetworkFailureKeepsCredentials()
    {
        using var factory = new BrokerFactory(); using var client = factory.Client();
        await factory.Login(client); await Session(client); factory.Google.RefreshError = "temporarily_unavailable";
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Post(client, "/api/token")).StatusCode);
        factory.Google.RefreshError = null;
        Assert.Equal(HttpStatusCode.OK, (await Post(client, "/api/token")).StatusCode);
        factory.Google.RefreshError = "invalid_grant";
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(client, "/api/token?force=true")).StatusCode);
    }
    [Fact]
    public async Task LogoutInvalidatesOnlyOneSession()
    {
        using var factory = new BrokerFactory(); using var first = factory.Client(); using var second = factory.Client();
        await factory.Login(first); await factory.Login(second); await Session(first);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(first, "/api/logout")).StatusCode);
        Assert.False((await Session(first)).Authenticated); Assert.True((await Session(second)).Authenticated);
        Assert.Equal(0, factory.Google.RevocationCalls);
    }
    [Fact]
    public async Task DisconnectRevokesGoogleAndAllAssociatedSessions()
    {
        using var factory = new BrokerFactory(); using var first = factory.Client(); using var second = factory.Client();
        await factory.Login(first); await factory.Login(second); await Session(first);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(first, "/api/disconnect")).StatusCode);
        Assert.False((await Session(second)).Authenticated);
        Assert.Equal(1, factory.Google.RevocationCalls);
        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<BrokerDb>().Accounts.ToListAsync());
    }
    [Fact]
    public async Task BrowserSessionAndEncryptedKeysSurviveHostRestart()
    {
        string path; string cookie;
        using (var first = new BrokerFactory())
        {
            path = first.DataDirectory; using var client = first.Client();
            cookie = (await first.Login(client)).Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-DigitalSorter.Session=")).Split(';')[0];
        }
        using var second = new BrokerFactory(path); using var restored = second.Client();
        restored.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.True((await Session(restored)).Authenticated);
        Assert.Equal(HttpStatusCode.OK, (await Post(restored, "/api/token")).StatusCode);
    }
    [Fact]
    public async Task SessionExpirationIsEnforcedFromPersistentStore()
    {
        using var factory = new BrokerFactory(); using var client = factory.Client();
        await factory.Login(client); factory.Clock.Advance(TimeSpan.FromDays(31));
        Assert.False((await Session(client)).Authenticated);
    }
    [Fact]
    public async Task ApiAndOAuthUnknownRoutesNeverReturnSpaHtml()
    {
        using var factory = new BrokerFactory(); using var client = factory.Client();
        foreach (var route in new[] { "/api/missing", "/auth/missing" })
        {
            var response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain("<!DOCTYPE", await response.Content.ReadAsStringAsync());
        }
    }
    [Fact]
    public void EncryptionRejectsModifiedCiphertext()
    {
        var cipher = new SecretCipher(RandomNumberGenerator.GetBytes(32));
        var encrypted = Convert.FromBase64String(cipher.Protect("credential")); encrypted[^1] ^= 1;
        Assert.Throws<AuthenticationTagMismatchException>(() => cipher.Unprotect(Convert.ToBase64String(encrypted)));
    }
    [Fact]
    public async Task PairingIsConfirmedExplicitlyAndSecretIsRequiredToConsumeOnce()
    {
        using var factory = new BrokerFactory(); using var tv = factory.Client(); using var phone = factory.Client();
        var pair = (await (await tv.PostAsJsonAsync("/api/pair/start", new PairingRequest("Salon"))).Content.ReadFromJsonAsync<PairingStarted>())!;
        var invalid = await (await tv.PostAsJsonAsync("/api/pair/poll", new PairingPoll(pair.Id, new string('0', 64)))).Content.ReadFromJsonAsync<PairingResult>();
        Assert.Equal("invalid", invalid!.Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.PostAsJsonAsync("/api/pair/confirm", new PairingConfirmation(pair.UserCode))).StatusCode);
        await factory.Login(phone); await Session(phone);
        Assert.Equal("Salon", (await phone.GetFromJsonAsync<PairingDetails>("/api/pair/details?code=" + pair.UserCode))!.DeviceName);
        Assert.Equal(HttpStatusCode.OK, (await phone.PostAsJsonAsync("/api/pair/confirm", new PairingConfirmation(pair.UserCode))).StatusCode);
        var poll = await (await tv.PostAsJsonAsync("/api/pair/poll", new PairingPoll(pair.Id, pair.DeviceSecret))).Content.ReadFromJsonAsync<PairingResult>();
        Assert.Equal("approved", poll!.Status); Assert.NotNull(poll.DeviceToken);
        var replay = await (await tv.PostAsJsonAsync("/api/pair/poll", new PairingPoll(pair.Id, pair.DeviceSecret))).Content.ReadFromJsonAsync<PairingResult>();
        Assert.Equal("consumed", replay!.Status); Assert.Null(replay.DeviceToken);
        tv.DefaultRequestHeaders.Authorization = new("Bearer", poll.DeviceToken);
        Assert.True((await Session(tv)).Authenticated);
        Assert.Equal(HttpStatusCode.OK, (await Post(tv, "/api/token")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(tv, "/api/logout")).StatusCode);
        Assert.False((await Session(tv)).Authenticated); Assert.True((await Session(phone)).Authenticated);
    }
    [Fact]
    public async Task ExpiredPairCannotBeConfirmedOrConsumed()
    {
        using var factory = new BrokerFactory(); using var tv = factory.Client(); using var phone = factory.Client();
        var pair = (await (await tv.PostAsJsonAsync("/api/pair/start", new PairingRequest("TV"))).Content.ReadFromJsonAsync<PairingStarted>())!;
        factory.Clock.Advance(TimeSpan.FromMinutes(6));
        await factory.Login(phone); await Session(phone);
        Assert.Equal(HttpStatusCode.Conflict, (await phone.PostAsJsonAsync("/api/pair/confirm", new PairingConfirmation(pair.UserCode))).StatusCode);
        var result = await (await tv.PostAsJsonAsync("/api/pair/poll", new PairingPoll(pair.Id, pair.DeviceSecret))).Content.ReadFromJsonAsync<PairingResult>();
        Assert.Equal("expired", result!.Status);
    }
    [Fact]
    public async Task PairingStartsAreRateLimitedAndMalformedJsonIsRejected()
    {
        using var factory = new BrokerFactory(); using var client = factory.Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/pair/poll", new { })).StatusCode);
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/pair/start", new PairingRequest("TV"))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/pair/start", new PairingRequest("TV"))).StatusCode);
    }
}
