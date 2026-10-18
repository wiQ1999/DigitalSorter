using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DigitalSorter.Contracts;
using DigitalSorter.Data.GoogleDrive;
using Microsoft.JSInterop;

namespace DigitalSorter.Web;

public sealed class BrokerClient(HttpClient http, IJSRuntime js, DriveOptions driveOptions) : IAccessTokenProvider, IAsyncDisposable
{
    private string? _deviceToken;
    private TokenResponse? _googleToken;
    private IJSObjectReference? _module;
    private ClientConfiguration? _configuration;
    private DotNetObjectReference<BrokerClient>? _reference;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public bool IsTv { get; private set; }
    public string LoginOrigin { get; private set; } = "";
    public string LoginUrl(string returnUrl = "/", bool consent = false) => LoginOrigin + "/auth/login?" +
        (consent ? "consent=true&" : "") + "returnUrl=" + Uri.EscapeDataString(returnUrl);
    public SessionInfo Session { get; private set; } = new(false);
    public IJSObjectReference Module => _module!;

    public async Task InitializeAsync()
    {
        var configuration = await ConfigurationAsync();
        if (_module is null)
        {
            _module = await js.InvokeAsync<IJSObjectReference>("import", "./js/media.js?v=" + Uri.EscapeDataString(configuration.AssetVersion ?? "1"));
            IsTv = await _module.InvokeAsync<bool>("isNativeTv");
            if (IsTv) _deviceToken = await _module.InvokeAsync<string?>("secureStorage", "get", null);
            _reference = DotNetObjectReference.Create(this);
            await _module.InvokeVoidAsync("initialize", _reference, IsTv, driveOptions.FilesEndpoint, configuration.AssetVersion);
        }
        await InitializeSessionAsync();
    }

    private async Task<ClientConfiguration> ConfigurationAsync()
    {
        _configuration ??= await http.GetFromJsonAsync<ClientConfiguration>("api/client-config") ?? throw new HttpRequestException("Missing client configuration.");
        driveOptions.FilesEndpoint = _configuration.DriveFilesEndpoint;
        LoginOrigin = _configuration.LoginOrigin ?? "";
        return _configuration;
    }

    public async Task InitializeSessionAsync()
    {
        await ConfigurationAsync();
        using var response = await SendAsync(HttpMethod.Get, "api/session");
        response.EnsureSuccessStatusCode();
        Session = (await response.Content.ReadFromJsonAsync<SessionInfo>())!;
        if (!Session.Authenticated && _deviceToken is not null)
        {
            _deviceToken = null;
            await _module!.InvokeVoidAsync("secureStorage", "clear", null);
        }
    }

    [JSInvokable]
    public async Task<string> GoogleAccessToken(bool forceRefresh) => await GetAsync(forceRefresh);
    public async Task<string> GetAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _googleToken is not null && _googleToken.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return _googleToken.AccessToken;
            using var response = await SendAsync(HttpMethod.Post, "api/token" + (forceRefresh ? "?force=true" : ""), ct: cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized) throw new DriveException(DriveFailure.Authentication);
            if (!response.IsSuccessStatusCode) throw new DriveException(DriveFailure.Network);
            _googleToken = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken) ?? throw new DriveException(DriveFailure.Authentication);
            return _googleToken.AccessToken;
        }
        finally { _gate.Release(); }
    }

    public async Task<PairingStarted> StartPairingAsync()
    {
        using var response = await SendAsync(HttpMethod.Post, "api/pair/start", new PairingRequest("DigitalSorter Android TV"));
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Nie można rozpocząć parowania. Spróbuj ponownie za chwilę.");
        return (await response.Content.ReadFromJsonAsync<PairingStarted>())!;
    }
    public async Task<string> PollAsync(PairingStarted pairing, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/pair/poll", new PairingPoll(pairing.Id, pairing.DeviceSecret), ct);
        if (!response.IsSuccessStatusCode) return "pending";
        var result = (await response.Content.ReadFromJsonAsync<PairingResult>(ct))!;
        if (result.DeviceToken is not null)
        {
            await _module!.InvokeVoidAsync("secureStorage", "set", result.DeviceToken);
            _deviceToken = result.DeviceToken;
            await InitializeAsync();
        }
        return result.Status;
    }
    public async Task<PairingDetails?> DetailsAsync(string code)
    {
        using var response = await SendAsync(HttpMethod.Get, "api/pair/details?code=" + Uri.EscapeDataString(code));
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<PairingDetails>() : null;
    }
    public async Task<bool> ConfirmAsync(string code)
    {
        using var response = await SendAsync(HttpMethod.Post, "api/pair/confirm", new PairingConfirmation(code));
        if (response.StatusCode == HttpStatusCode.Unauthorized) await InitializeSessionAsync();
        return response.IsSuccessStatusCode;
    }
    public async Task SignOutAsync(bool disconnect)
    {
        using var response = await SendAsync(HttpMethod.Post, disconnect ? "api/disconnect" : "api/logout");
        if (!response.IsSuccessStatusCode && (disconnect || response.StatusCode != HttpStatusCode.Unauthorized))
            throw new InvalidOperationException("Nie udało się zakończyć sesji. Spróbuj ponownie.");
        if (IsTv) await _module!.InvokeVoidAsync("secureStorage", "clear", null);
        _deviceToken = null; _googleToken = null; Session = new(false);
        await _module!.InvokeVoidAsync("clearMedia");
    }
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(method, path);
        if (_deviceToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _deviceToken);
        if (Session.CsrfToken is not null) request.Headers.Add("X-CSRF-TOKEN", Session.CsrfToken);
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType());
        return await http.SendAsync(request, ct);
    }
    public async ValueTask DisposeAsync()
    {
        _reference?.Dispose();
        if (_module is not null) await _module.DisposeAsync();
        _gate.Dispose();
    }
}

public sealed record ClientConfiguration(string DriveFilesEndpoint, string? AssetVersion = null, string? LoginOrigin = null);
