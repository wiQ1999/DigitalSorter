using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DigitalSorter.Auth;

public sealed class DeviceAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, BrokerDb db, TimeProvider clock) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal)) return AuthenticateResult.NoResult();
        var token = header[7..];
        if (token.Length != 64) return AuthenticateResult.Fail("Invalid device credential.");
        var hash = SecretCipher.Hash(token);
        var now = clock.GetUtcNow();
        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.SecretHash == hash && x.Kind == "device" && x.ExpiresAt > now, Context.RequestAborted);
        if (session is null) return AuthenticateResult.Fail("Expired device session.");
        var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.Subject == session.Subject, Context.RequestAborted);
        if (account is null) return AuthenticateResult.Fail("Disconnected account.");
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new("sub", session.Subject), new("ds_session", session.Id), new(ClaimTypes.Name, account.Name)
        ], Scheme.Name));
        return AuthenticateResult.Success(new(principal, Scheme.Name));
    }
}
