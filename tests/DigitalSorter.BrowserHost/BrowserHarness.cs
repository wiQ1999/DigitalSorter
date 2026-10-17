using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Runtime.InteropServices;
using DigitalSorter.IntegrationTests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

public static class BrowserHarness
{
    public static async Task Main()
    {
        var port = int.Parse(Environment.GetEnvironmentVariable("TEST_PORT") ?? "7443");
        var origin = Environment.GetEnvironmentVariable("TEST_ORIGIN") ?? "https://localhost:" + port;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var address) || address.Scheme != "https" || address.Port != port ||
            address.AbsolutePath != "/" || !string.IsNullOrEmpty(address.Query) || !string.IsNullOrEmpty(address.Fragment) || !string.IsNullOrEmpty(address.UserInfo))
            throw new InvalidOperationException("Set TEST_ORIGIN to an HTTPS origin matching TEST_PORT.");
        origin = address.GetLeftPart(UriPartial.Authority);
        var certificate = Environment.GetEnvironmentVariable("TEST_CERTIFICATE") ?? throw new InvalidOperationException("Set TEST_CERTIFICATE to the test PFX.");
        var password = Environment.GetEnvironmentVariable("TEST_CERTIFICATE_PASSWORD") ?? "";
        var bindAddress = IPAddress.Parse(Environment.GetEnvironmentVariable("TEST_BIND_ADDRESS") ?? "0.0.0.0");
        using var factory = new BrowserFactory(origin, Environment.GetEnvironmentVariable("TEST_FIXTURES") ?? "tests/fixtures");
        factory.UseKestrel(options => options.Listen(bindAddress, port, listen => listen.UseHttps(certificate, password)));
        factory.StartServer();
        Console.WriteLine("Controlled OAuth/Drive test host ready at " + origin);
        var stop = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };
        using var terminate = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM,
            signal => { signal.Cancel = true; stop.TrySetResult(); });
        await stop.Task;
    }
}

public sealed class BrowserFactory : BrokerFactory
{
    private readonly string _fixtures;
    public BrowserFactory(string origin, string fixtures) : base(Environment.GetEnvironmentVariable("TEST_DATA_DIRECTORY"))
    {
        PublicOrigin = origin; _fixtures = Path.GetFullPath(fixtures);
        Google.AuthorizationEndpoint = origin + "/test-oauth/authorize";
    }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("AllowedHosts", "localhost;127.0.0.1;10.0.2.2;" + new Uri(PublicOrigin).Host);
        if (Environment.GetEnvironmentVariable("TEST_WEB_ROOT") is { Length: > 0 } publishedRoot)
        {
            builder.UseWebRoot(Path.GetFullPath(publishedRoot));
            builder.UseSetting(WebHostDefaults.StaticWebAssetsKey, Path.Combine(publishedRoot, "unused-test-manifest.json"));
        }
        builder.UseSetting("Testing:DriveFilesEndpoint", PublicOrigin + "/test-drive/files");
        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new ControlledEndpoints(Google, PublicOrigin, _fixtures)));
    }
}

public sealed class ControlledEndpoints(ControlledGoogle google, string origin, string fixturePath) : IStartupFilter
{
    private sealed record FileSpec(string Id, string Name, string Mime, string? Date, string Fixture, bool Available = true);
    private static readonly FileSpec[] Files =
    [
        new("mountain", "Góry o świcie.jpg", "image/jpeg", "2020-04-20T06:30:00Z", "landscape.jpg"),
        new("coast", "Wybrzeże.png", "image/png", "2021-07-12T12:15:00Z", "landscape.png"),
        new("webp", "Leśna ścieżka.webp", "image/webp", "2022-01-02T10:00:00Z", "landscape.webp"),
        new("heic", "Zdjęcie z telefonu.heic", "image/heic", "2022-06-01T10:00:00Z", "landscape.heic"),
        new("heif", "Drugie zdjęcie.heif", "image/heif", "2022-07-01T10:00:00Z", "landscape.heif"),
        new("mp4", "Zachód słońca.mp4", "video/mp4", "2023-01-01T10:00:00Z", "motion.mp4"),
        new("webm", "Chwila nad wodą.webm", "video/webm", "2023-06-01T10:00:00Z", "motion.webm"),
        new("mov", "Spacer.mov", "video/quicktime", "2023-07-01T10:00:00Z", "motion.mov"),
        new("undated", "Bez daty.jpg", "image/jpeg", null, "undated.jpg"),
        new("unavailable", "Niedostępne zdjęcie.jpg", "image/jpeg", "2024-01-01T10:00:00Z", "landscape.jpg", false),
        new("ignored", "Notatki.txt", "text/plain", null, "notes.txt")
    ];
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        var unavailableThumbnails = false;
        app.Use(async (context, continueRequest) =>
        {
            var path = context.Request.Path.Value ?? "";
            if (path == "/test-drive/control" && context.Request.Method == "POST")
            {
                using var body = await JsonDocument.ParseAsync(context.Request.Body);
                unavailableThumbnails = body.RootElement.GetProperty("unavailableThumbnails").GetBoolean();
                context.Response.StatusCode = 204;
                return;
            }
            if (path == "/test-oauth/control" && context.Request.Method == "POST")
            {
                using var body = await JsonDocument.ParseAsync(context.Request.Body);
                var mode = body.RootElement.GetProperty("mode").GetString();
                if (mode is not ("normal" or "revoked" or "missing-refresh")) { context.Response.StatusCode = 400; return; }
                google.RefreshError = mode == "revoked" ? "invalid_grant" : null;
                google.IncludeRefresh = mode != "missing-refresh";
                context.Response.StatusCode = 204;
                return;
            }
            var clientOrigin = context.Request.Scheme + "://" + context.Request.Host;
            if (path == "/api/client-config")
            {
                await context.Response.WriteAsJsonAsync(new { DriveFilesEndpoint = clientOrigin + "/test-drive/files" });
                return;
            }
            if (path == "/test-oauth/authorize")
            {
                var authorizationUrl = origin + path + context.Request.QueryString;
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync("<!doctype html><html lang=pl><title>Kontrolowany OAuth</title><body style='background:#0b1018;color:white;font:20px system-ui;padding:60px'><h1>Kontrolowany serwer Google OAuth</h1><p>Wyłącznie syntetyczne konto i dane testowe.</p><form method=post action=/test-oauth/approve><input type=hidden name=request value='" + HtmlEncoder.Default.Encode(authorizationUrl) + "'><button>Zatwierdź dostęp testowy</button></form></body></html>");
                return;
            }
            if (path == "/test-oauth/approve")
            {
                var form = await context.Request.ReadFormAsync();
                var url = new Uri(form["request"]!);
                var query = QueryHelpers.ParseQuery(url.Query);
                var code = google.Authorize(url);
                context.Response.Redirect(origin + "/auth/callback?code=" + code + "&state=" + Uri.EscapeDataString(query["state"]!)); return;
            }
            if (!path.StartsWith("/test-drive/", StringComparison.Ordinal)) { await continueRequest(); return; }
            if (google.RefreshError == "invalid_grant" || context.Request.Headers.Authorization != "Bearer controlled-access-token") { context.Response.StatusCode = 401; return; }
            if (unavailableThumbnails && path.StartsWith("/test-drive/thumb/", StringComparison.Ordinal)) { context.Response.StatusCode = 403; return; }
            if (path == "/test-drive/files")
            {
                var q = context.Request.Query["q"].ToString();
                if (q.Contains("mimeType = 'application/vnd.google-apps.folder'"))
                {
                    await context.Response.WriteAsJsonAsync(new { files = new[] { new { id = "folder-travel", name = "Podróże i wspomnienia", mimeType = "application/vnd.google-apps.folder" } } }); return;
                }
                if (!q.Contains("'folder-travel'")) { context.Response.StatusCode = 404; return; }
                await context.Response.WriteAsJsonAsync(new { files = Files.Select(f => new
                {
                    id = f.Id, name = f.Name, mimeType = f.Mime, size = File.Exists(Path.Combine(fixturePath, f.Fixture)) ? new FileInfo(Path.Combine(fixturePath, f.Fixture)).Length.ToString() : "0",
                    imageMediaMetadata = new { time = f.Date }, capabilities = new { canDownload = f.Available },
                    createdTime = "2026-01-01T00:00:00Z", modifiedTime = "2026-02-01T00:00:00Z"
                }) }); return;
            }
            var id = path.Split('/').Last(); var spec = Files.SingleOrDefault(x => x.Id == id);
            if (spec is null || !spec.Available) { context.Response.StatusCode = 404; return; }
            if (context.Request.Query.ContainsKey("fields"))
            { await context.Response.WriteAsJsonAsync(new { thumbnailLink = clientOrigin + "/test-drive/thumb/" + id }); return; }
            var fixture = path.Contains("/thumb/") ? "landscape.jpg" : spec.Fixture;
            var bytes = await File.ReadAllBytesAsync(Path.Combine(fixturePath, fixture));
            context.Response.ContentType = path.Contains("/thumb/") ? "image/jpeg" : spec.Mime;
            context.Response.Headers.AcceptRanges = "bytes";
            context.Response.Headers.CacheControl = "no-store";
            if (context.Request.Headers.Range.ToString() is { Length: > 0 } range && range.StartsWith("bytes="))
            {
                var parts = range[6..].Split('-'); var start = long.Parse(parts[0]);
                var end = parts.Length > 1 && long.TryParse(parts[1], out var last) ? Math.Min(last, bytes.Length - 1) : bytes.Length - 1;
                if (start >= bytes.Length) { context.Response.StatusCode = 416; context.Response.Headers.ContentRange = "bytes */" + bytes.Length; return; }
                context.Response.StatusCode = 206;
                context.Response.Headers.ContentRange = $"bytes {start}-{end}/{bytes.Length}";
                context.Response.ContentLength = end - start + 1;
                await context.Response.Body.WriteAsync(bytes.AsMemory((int)start, (int)(end - start + 1))); return;
            }
            context.Response.ContentLength = bytes.Length;
            await context.Response.Body.WriteAsync(bytes);
        });
        next(app);
    };
}
