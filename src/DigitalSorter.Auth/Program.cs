using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using DigitalSorter.Auth;
using DigitalSorter.Contracts;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using QRCoder;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
var google = builder.Configuration.GetSection("Google").Get<GoogleOptions>() ?? new();
var dataDirectory = Path.GetFullPath(builder.Configuration["Broker:DataDirectory"] ?? Path.Combine(builder.Environment.ContentRootPath, "data"));
Directory.CreateDirectory(dataDirectory);
var publicOrigin = BrokerOrigins.Validate(builder.Configuration["Broker:PublicOrigin"] ?? "https://localhost:7043", "Broker:PublicOrigin");
builder.Configuration["Broker:PublicOrigin"] = publicOrigin;
var pairingOrigin = BrokerOrigins.Validate(BrokerOrigins.Pairing(builder.Configuration), "Broker:PairingOrigin");
builder.Configuration["Broker:PairingOrigin"] = pairingOrigin;
var encryptionKey = builder.Configuration["Broker:TokenEncryptionKey"];
if (string.IsNullOrEmpty(encryptionKey))
{
    if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
        throw new InvalidOperationException("Configure Broker:TokenEncryptionKey using a secret manager (base64, 32 bytes).");
    var keyFile = Path.Combine(dataDirectory, "development.key");
    if (!File.Exists(keyFile))
    {
        File.WriteAllText(keyFile, Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(keyFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
    encryptionKey = File.ReadAllText(keyFile);
}
var key = Convert.FromBase64String(encryptionKey);
if (key.Length != 32) throw new InvalidOperationException("Broker:TokenEncryptionKey must contain 32 bytes.");
if (builder.Environment.IsProduction() && !google.Configured)
    throw new InvalidOperationException("Configure Google:ClientId and Google:ClientSecret using environment settings.");
builder.Services.AddSingleton(google);
builder.Services.AddSingleton(new SecretCipher(key));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AccountLocks>();
builder.Services.AddMemoryCache();
builder.Services.AddSqliteBrokerStorage(builder.Configuration, dataDirectory);
builder.Services.AddDataProtection().SetApplicationName("DigitalSorter").PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")));
builder.Services.AddOptions<KeyManagementOptions>().Configure<SecretCipher>((o, c) => o.XmlEncryptor = new KeyRingEncryptor(c));
builder.Services.AddScoped<GoogleTokens>();
builder.Services.AddScoped<PairingService>();
builder.Services.AddHttpClient("google", c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.RespectNullableAnnotations = true;
    o.SerializerOptions.RespectRequiredConstructorParameters = true;
});
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-CSRF-TOKEN";
    o.Cookie.Name = "__Host-DigitalSorter.Csrf";
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.AddAuthentication(o =>
{
    o.DefaultScheme = "Broker";
    o.DefaultChallengeScheme = "Broker";
}).AddPolicyScheme("Broker", "Broker", o => o.ForwardDefaultSelector = c => c.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.Ordinal) ? "Device" : "Web")
.AddScheme<AuthenticationSchemeOptions, DeviceAuthentication>("Device", _ => { })
.AddCookie("Web", o =>
{
    o.Cookie.Name = "__Host-DigitalSorter.Session";
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.ExpireTimeSpan = TimeSpan.FromDays(30);
    o.SlidingExpiration = false;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
    o.Events.OnValidatePrincipal = async c =>
    {
        var store = c.HttpContext.RequestServices.GetRequiredService<IBrokerStore>();
        var now = c.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        var id = c.Principal?.FindFirstValue("ds_session");
        if (!await store.HasWebSessionAsync(id, now, c.HttpContext.RequestAborted)) c.RejectPrincipal();
    };
}).AddOpenIdConnect("Google", o =>
{
    o.SignInScheme = "Web";
    o.Authority = google.Authority;
    o.ClientId = google.Configured ? google.ClientId : "not-configured";
    o.ClientSecret = google.ClientSecret;
    o.ResponseType = OpenIdConnectResponseType.Code;
    o.ResponseMode = OpenIdConnectResponseMode.Query;
    o.CallbackPath = "/auth/callback";
    o.MapInboundClaims = false;
    o.Scope.Clear(); o.Scope.Add("openid"); o.Scope.Add("https://www.googleapis.com/auth/drive.readonly");
    o.UsePkce = true;
    o.SaveTokens = true;
    o.GetClaimsFromUserInfoEndpoint = false;
    o.TokenValidationParameters.NameClaimType = "name";
    o.Events.OnTokenResponseReceived = c => { c.Properties!.Items["granted_scopes"] = c.TokenEndpointResponse.Scope; return Task.CompletedTask; };
    o.Events.OnRedirectToIdentityProvider = c =>
    {
        c.ProtocolMessage.RedirectUri = publicOrigin.TrimEnd('/') + "/auth/callback";
        c.ProtocolMessage.SetParameter("access_type", "offline");
        if (c.Properties.Items.ContainsKey("consent")) c.ProtocolMessage.Prompt = "consent";
        return Task.CompletedTask;
    };
    o.Events.OnTicketReceived = async c =>
    {
        var ct = c.HttpContext.RequestAborted;
        var principal = c.Principal!;
        var subject = principal.FindFirstValue("sub")!;
        var scopes = c.Properties!.Items.TryGetValue("granted_scopes", out var grantedScopes) ? grantedScopes ?? "" : "";
        if (!scopes.Split(' ').Contains("https://www.googleapis.com/auth/drive.readonly"))
        { c.Response.Redirect(FailureRedirect(c.ReturnUri, "permission")); c.HandleResponse(); return; }
        try
        {
            await c.HttpContext.RequestServices.GetRequiredService<GoogleTokens>().SaveAsync(subject,
                principal.FindFirstValue("name") ?? "Konto Google", c.Properties.GetTokenValue("refresh_token"), scopes, ct);
        }
        catch (GoogleAuthorizationRequiredException)
        { c.Response.Redirect(FailureRedirect(c.ReturnUri, "consent")); c.HandleResponse(); return; }
        var store = c.HttpContext.RequestServices.GetRequiredService<IBrokerStore>();
        var session = new BrokerSession { Id = SecretCipher.RandomSecret(), Subject = subject,
            ExpiresAt = c.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().AddDays(30) };
        await store.AddSessionAsync(session, ct);
        c.Principal = new(new ClaimsIdentity([new("sub", subject), new("ds_session", session.Id),
            new(ClaimTypes.Name, principal.FindFirstValue("name") ?? "Konto Google")], "Web"));
        c.Properties.StoreTokens([]);
        c.Properties.IsPersistent = true;
        c.Properties.ExpiresUtc = session.ExpiresAt;
    };
    o.Events.OnRemoteFailure = c => { c.Response.Redirect(FailureRedirect(c.Properties?.RedirectUri, "oauth")); c.HandleResponse(); return Task.CompletedTask; };
});
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    foreach (var (name, limit) in new[] { ("pair-start", 5), ("pair-confirm", 10), ("pair-poll", 120), ("login", 20) })
        o.AddPolicy(name, context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var ip in builder.Configuration.GetSection("Broker:KnownProxies").Get<string[]>() ?? []) o.KnownProxies.Add(IPAddress.Parse(ip));
});

var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<IBrokerStoreInitializer>().InitializeAsync();
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.Use(async (c, next) =>
{
    c.Response.Headers["X-Content-Type-Options"] = "nosniff";
    c.Response.Headers["Referrer-Policy"] = "no-referrer";
    c.Response.Headers["X-Frame-Options"] = "DENY";
    c.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' blob: data: https://*.googleusercontent.com; media-src 'self' blob:; connect-src 'self' https://www.googleapis.com https://*.googleusercontent.com; worker-src 'self' blob:; frame-ancestors 'none'; base-uri 'self'";
    if (c.Request.Path.StartsWithSegments("/api") || c.Request.Path.StartsWithSegments("/auth")) c.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseBlazorFrameworkFiles();
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = c =>
{
    if (Path.GetExtension(c.File.Name) is ".js" or ".css" or ".html") c.Context.Response.Headers.CacheControl = "no-cache";
}});
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", googleConfigured = google.Configured }));
var assetVersion = WebAssets.Revision(app.Environment.WebRootFileProvider);
app.MapGet("/api/client-config", () => Results.Ok(new
{
    AssetVersion = assetVersion,
    LoginOrigin = publicOrigin,
    DriveFilesEndpoint = app.Environment.IsEnvironment("Testing")
        ? builder.Configuration["Testing:DriveFilesEndpoint"] ?? "https://www.googleapis.com/drive/v3/files"
        : "https://www.googleapis.com/drive/v3/files"
}));
app.MapGet("/auth/login", (HttpContext c, string? returnUrl, bool? consent) =>
{
    if (!google.Configured) return Results.Problem("Konfiguracja Google OAuth nie została uzupełniona.", statusCode: 503);
    var target = returnUrl is not null && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") && !returnUrl.Contains('\\') && !returnUrl.Any(char.IsControl) ? returnUrl : "/";
    var props = new AuthenticationProperties { RedirectUri = target };
    if (consent == true) props.Items["consent"] = "true";
    return Results.Challenge(props, ["Google"]);
}).RequireRateLimiting("login");
app.MapGet("/api/session", async (HttpContext c, IAntiforgery csrf, IBrokerStore store, CancellationToken ct) =>
{
    c.Response.Headers.CacheControl = "no-cache, no-store";
    c.Response.Headers.Pragma = "no-cache";
    var authenticated = c.User.Identity?.IsAuthenticated == true;
    var subject = c.User.FindFirstValue("sub");
    var reauthorize = authenticated && !await store.HasAuthorizedAccountAsync(subject, google.ClientId, ct);
    return Results.Ok(new SessionInfo(authenticated, c.User.Identity?.Name, c.User.Identity?.AuthenticationType == "Device",
        csrf.GetAndStoreTokens(c).RequestToken, reauthorize));
});
app.MapPost("/api/pair/start", async (PairingRequest request, PairingService pairs, CancellationToken ct) =>
    Results.Ok(await pairs.StartAsync(request.DeviceName, ct))).RequireRateLimiting("pair-start");
app.MapPost("/api/pair/poll", async (PairingPoll request, PairingService pairs, CancellationToken ct) =>
{
    if (request.Id.Length != 64 || request.DeviceSecret.Length != 64) return Results.BadRequest();
    return Results.Ok(await pairs.PollAsync(request, ct));
}).RequireRateLimiting("pair-poll");
app.MapGet("/api/pair/details", async (string code, PairingService pairs, CancellationToken ct) =>
    await pairs.DetailsAsync(code, ct) is { } details ? Results.Ok(details) : Results.NotFound()).RequireAuthorization().RequireRateLimiting("pair-confirm");
app.MapGet("/api/pair/qr", (string uri) =>
{
    if (!uri.StartsWith(pairingOrigin + "/pair?code=", StringComparison.Ordinal) || uri.Length > 512) return Results.BadRequest();
    using var data = QRCodeGenerator.GenerateQrCode(uri, QRCodeGenerator.ECCLevel.M);
    return Results.Text(new SvgQRCode(data).GetGraphic(8), "image/svg+xml");
}).RequireRateLimiting("pair-poll");
var secured = app.MapGroup("/api").RequireAuthorization().AddEndpointFilter(async (context, next) =>
{
    var c = context.HttpContext;
    if (c.Request.Method == "POST" && c.User.Identity?.AuthenticationType != "Device")
    {
        try { await c.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(c); }
        catch (AntiforgeryValidationException) { return Results.BadRequest(new { error = "csrf" }); }
    }
    try { return await next(context); }
    catch (GoogleAuthorizationRequiredException) { return Results.Json(new { error = "google_reauthorization_required" }, statusCode: 401); }
    catch (GoogleServiceUnavailableException) { return Results.Problem("Google jest chwilowo niedostępne. Spróbuj ponownie.", statusCode: 503); }
});
secured.MapPost("/token", async (HttpContext c, bool? force, GoogleTokens tokens, CancellationToken ct) => Results.Ok(await tokens.GetAsync(c.User.FindFirstValue("sub")!, ct, force == true)));
secured.MapPost("/pair/confirm", async (PairingConfirmation request, HttpContext c, PairingService pairs, GoogleTokens tokens, CancellationToken ct) =>
{
    var subject = c.User.FindFirstValue("sub")!;
    await tokens.GetAsync(subject, ct, force: true);
    return await pairs.ApproveAsync(request.Code, subject, ct) ? Results.Ok() : Results.Conflict();
}).RequireRateLimiting("pair-confirm");
secured.MapPost("/logout", async (HttpContext c, IBrokerStore store, CancellationToken ct) =>
{
    var id = c.User.FindFirstValue("ds_session");
    await store.DeleteSessionAsync(id, ct);
    if (c.User.Identity?.AuthenticationType != "Device") await c.SignOutAsync("Web");
    return Results.NoContent();
});
secured.MapPost("/disconnect", async (HttpContext c, GoogleTokens tokens, CancellationToken ct) =>
{
    await tokens.DisconnectAsync(c.User.FindFirstValue("sub")!, ct);
    if (c.User.Identity?.AuthenticationType != "Device") await c.SignOutAsync("Web");
    return Results.NoContent();
});
app.Map("/api/{**path}", () => Results.NotFound());
app.Map("/auth/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");
app.Run();

static string FailureRedirect(string? target, string error) =>
    target is not null && target.StartsWith("/pair?", StringComparison.Ordinal)
        ? QueryHelpers.AddQueryString(target, "error", error) : "/?error=" + error;

public partial class Program;
