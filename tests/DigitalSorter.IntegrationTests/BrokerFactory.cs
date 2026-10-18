using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalSorter.Auth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace DigitalSorter.IntegrationTests;

public sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

public sealed class ControlledGoogle : IDisposable
{
    public const string Scope = "openid https://www.googleapis.com/auth/drive.readonly";
    public const string Client = "controlled-client";
    public const string Secret = "controlled-secret-not-a-real-credential";
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly ConcurrentDictionary<string, (string Nonce, string Challenge, string Subject)> _codes = new();
    public int RefreshCalls;
    public int RevocationCalls;
    public string? RefreshError;
    public string GrantedScopes = Scope;
    public bool IncludeRefresh = true;
    public string? NonceOverride;
    public string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    public RsaSecurityKey Key => new(_rsa) { KeyId = "controlled-key" };
    public OpenIdConnectConfiguration Configuration => new()
    {
        Issuer = "https://accounts.google.com", AuthorizationEndpoint = AuthorizationEndpoint,
        TokenEndpoint = "https://oauth2.googleapis.com/token", SigningKeys = { Key }
    };
    public string Authorize(Uri location, string subject = "controlled-user")
    {
        var query = QueryHelpers.ParseQuery(location.Query);
        var code = Guid.NewGuid().ToString("N");
        _codes[code] = (query["nonce"]!, query["code_challenge"]!, subject);
        return code;
    }
    public HttpMessageHandler Handler() => new ControlledHandler(this);
    public async Task<HttpResponseMessage> Respond(HttpRequestMessage request, CancellationToken ct)
    {
        var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(ct));
        if (request.RequestUri!.AbsolutePath.EndsWith("/revoke")) { Interlocked.Increment(ref RevocationCalls); return Json(new { }); }
        if (form["client_id"] != Client || form["client_secret"] != Secret) return Json(new { error = "invalid_client" }, HttpStatusCode.BadRequest);
        if (form["grant_type"] == "refresh_token")
        {
            Interlocked.Increment(ref RefreshCalls);
            return RefreshError is null ? Json(new { access_token = "controlled-access-token", expires_in = 3600, scope = Scope, token_type = "Bearer" })
                : Json(new { error = RefreshError }, HttpStatusCode.BadRequest);
        }
        if (!_codes.TryRemove(form["code"]!, out var code)) return Json(new { error = "invalid_grant" }, HttpStatusCode.BadRequest);
        var verifier = form["code_verifier"].ToString();
        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        if (challenge != code.Challenge) return Json(new { error = "invalid_grant" }, HttpStatusCode.BadRequest);
        var now = DateTime.UtcNow;
        var idToken = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://accounts.google.com", Audience = Client, IssuedAt = now, NotBefore = now.AddSeconds(-5), Expires = now.AddMinutes(5),
            Subject = new ClaimsIdentity([new("sub", code.Subject), new("nonce", NonceOverride ?? code.Nonce), new("name", "Konto testowe")]),
            SigningCredentials = new(Key, SecurityAlgorithms.RsaSha256)
        });
        var response = new Dictionary<string, object> { ["access_token"] = "controlled-access-token", ["expires_in"] = 3600,
            ["token_type"] = "Bearer", ["scope"] = GrantedScopes, ["id_token"] = idToken };
        if (IncludeRefresh) response["refresh_token"] = "controlled-refresh-token";
        return Json(response);
    }
    private static HttpResponseMessage Json(object value, HttpStatusCode code = HttpStatusCode.OK) => new(code)
    { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    private sealed class ControlledHandler(ControlledGoogle provider) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => provider.Respond(request, cancellationToken);
    }
    public void Dispose() => _rsa.Dispose();
}

public class BrokerFactory : WebApplicationFactory<Program>
{
    public string DataDirectory { get; }
    public ControlledGoogle Google { get; } = new();
    public TestClock Clock { get; } = new();
    public string? Failure { get; private set; }
    public string PublicOrigin { get; set; } = "https://localhost";
    public string? PairingOrigin { get; set; }
    public string EnvironmentName { get; set; } = "Testing";
    public BrokerFactory(string? dataDirectory = null) => DataDirectory = dataDirectory ?? Path.Combine(Path.GetTempPath(), "digitalsorter-tests-" + Guid.NewGuid());
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DigitalSorter.slnx"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Run integration tests from the DigitalSorter checkout.");
        builder.UseContentRoot(Path.Combine(root.FullName, "src", "DigitalSorter.Auth"));
        builder.UseEnvironment(EnvironmentName);
        builder.UseSetting("Broker:DataDirectory", DataDirectory);
        builder.UseSetting("Broker:PublicOrigin", PublicOrigin);
        if (PairingOrigin is not null) builder.UseSetting("Broker:PairingOrigin", PairingOrigin);
        builder.UseSetting("Broker:TokenEncryptionKey", Convert.ToBase64String(Enumerable.Repeat((byte)17, 32).ToArray()));
        builder.UseSetting("Google:ClientId", ControlledGoogle.Client);
        builder.UseSetting("Google:ClientSecret", ControlledGoogle.Secret);
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.AddHttpClient("google").ConfigurePrimaryHttpMessageHandler(Google.Handler);
            services.PostConfigure<OpenIdConnectOptions>("Google", options =>
            {
                options.Configuration = Google.Configuration;
                options.ConfigurationManager = new Microsoft.IdentityModel.Protocols.StaticConfigurationManager<OpenIdConnectConfiguration>(Google.Configuration);
                options.Backchannel = new HttpClient(Google.Handler());
                var previous = options.Events.OnRemoteFailure;
                options.Events.OnRemoteFailure = async context => { Failure = context.Failure?.ToString(); await previous(context); };
            });
        });
    }
    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new(PublicOrigin), AllowAutoRedirect = false });
    public async Task<HttpResponseMessage> Login(HttpClient client, string subject = "controlled-user", bool consent = false, string returnUrl = "/")
    {
        var challenge = await client.GetAsync("/auth/login?returnUrl=" + Uri.EscapeDataString(returnUrl) + (consent ? "&consent=true" : ""));
        var location = challenge.Headers.Location ?? throw new InvalidOperationException("OAuth challenge missing: " + await challenge.Content.ReadAsStringAsync());
        var query = QueryHelpers.ParseQuery(location.Query);
        var code = Google.Authorize(location, subject);
        return await client.GetAsync("/auth/callback?code=" + code + "&state=" + Uri.EscapeDataString(query["state"]!));
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) Google.Dispose();
    }
}
