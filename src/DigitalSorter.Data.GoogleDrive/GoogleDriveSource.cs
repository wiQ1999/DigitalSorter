using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DigitalSorter.Core;

namespace DigitalSorter.Data.GoogleDrive;

public interface IAccessTokenProvider
{
    Task<string> GetAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
}

public enum DriveFailure { Authentication, Permission, Missing, Quota, Network }
public sealed class DriveException(DriveFailure reason) : Exception(reason switch
{
    DriveFailure.Authentication => "Połącz ponownie konto Google.",
    DriveFailure.Permission => "Brak uprawnień do wybranego materiału lub folderu.",
    DriveFailure.Missing => "Materiał lub folder nie jest już dostępny.",
    DriveFailure.Quota => "Limit Google Drive został przekroczony. Spróbuj ponownie później.",
    _ => "Nie udało się połączyć z Google Drive. Sprawdź połączenie i spróbuj ponownie."
})
{
    public DriveFailure Reason { get; } = reason;
}

public sealed class DriveOptions
{
    public string FilesEndpoint { get; set; } = "https://www.googleapis.com/drive/v3/files";
}

public sealed class GoogleDriveSource(HttpClient http, IAccessTokenProvider tokens, DriveOptions? options = null) : IMediaLibrarySource
{
    private string Files => options?.FilesEndpoint ?? "https://www.googleapis.com/drive/v3/files";
    private const string FolderMime = "application/vnd.google-apps.folder";
    private const string Fields = "nextPageToken,files(id,name,mimeType,size,parents,imageMediaMetadata(time),capabilities(canDownload))";

    public async Task<IReadOnlyList<SourceFolder>> GetFoldersAsync(string? parentId, CancellationToken cancellationToken = default)
    {
        var parent = parentId is null ? "('root' in parents or sharedWithMe = true)" : $"'{Escape(parentId)}' in parents";
        var files = await ListAsync($"trashed = false and mimeType = '{FolderMime}' and {parent}", cancellationToken);
        return files.Select(x => new SourceFolder(x.GetProperty("id").GetString()!, x.GetProperty("name").GetString()!)).ToArray();
    }

    public async Task<IReadOnlyList<MediaAsset>> ReadAsync(IEnumerable<string> folderIds, CancellationToken cancellationToken = default)
    {
        var result = new List<MediaAsset>();
        var pending = new Queue<string>(folderIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal));
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var seenFiles = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryDequeue(out var folder))
        {
            if (!visited.Add(folder)) continue;
            foreach (var file in await ListAsync($"trashed = false and '{Escape(folder)}' in parents", cancellationToken))
            {
                var id = file.GetProperty("id").GetString()!;
                var mime = file.GetProperty("mimeType").GetString()!;
                if (mime == FolderMime) { pending.Enqueue(id); continue; }
                var name = file.GetProperty("name").GetString()!;
                if (MediaFormats.GetKind(name) is not { } kind || !seenFiles.Add(id)) continue;
                var available = !file.TryGetProperty("capabilities", out var capabilities) ||
                    !capabilities.TryGetProperty("canDownload", out var canDownload) || canDownload.GetBoolean();
                DateTimeOffset? captured = null;
                if (file.TryGetProperty("imageMediaMetadata", out var image) && image.TryGetProperty("time", out var time)) captured = CaptureDate.Parse(time.GetString());
                if (captured is null && available)
                {
                    var size = file.TryGetProperty("size", out var sizeJson) && long.TryParse(sizeJson.GetString(), out var parsedSize) ? parsedSize : (long?)null;
                    try { captured = await CaptureMetadata.ReadAsync(name, async (offset, length, ct) => await ReadRangeAsync(id, offset, length, ct), cancellationToken, size); }
                    catch (DriveException ex) when (ex.Reason is DriveFailure.Permission or DriveFailure.Missing) { available = false; }
                }
                result.Add(new(id, name, mime, kind, captured, folder, available));
            }
        }
        return result;
    }

    private async Task<List<JsonElement>> ListAsync(string query, CancellationToken ct)
    {
        var result = new List<JsonElement>();
        string? page = null;
        do
        {
            var uri = Files + "?pageSize=1000&supportsAllDrives=true&includeItemsFromAllDrives=true&fields=" + Uri.EscapeDataString(Fields)
                + "&q=" + Uri.EscapeDataString(query) + (page is null ? "" : "&pageToken=" + Uri.EscapeDataString(page));
            using var response = await SendAsync(uri, null, ct);
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            result.AddRange(json.RootElement.GetProperty("files").EnumerateArray().Select(x => x.Clone()));
            page = json.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        } while (!string.IsNullOrEmpty(page));
        return result;
    }

    public async Task<byte[]> ReadRangeAsync(string id, long offset, int length, CancellationToken ct)
    {
        using var response = await SendAsync(Files + "/" + Uri.EscapeDataString(id) + "?alt=media&supportsAllDrives=true", new(offset, offset + length - 1), ct);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable) return [];
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        // Google supports byte ranges; don't accidentally treat a complete body as a tail range.
        if (offset > 0 && response.StatusCode != HttpStatusCode.PartialContent) throw new DriveException(DriveFailure.Network);
        var buffer = new byte[length];
        var read = 0;
        while (read < length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read), ct);
            if (count == 0) break;
            read += count;
        }
        return buffer.AsSpan(0, read).ToArray();
    }

    private async Task<HttpResponseMessage> SendAsync(string uri, RangeHeaderValue? range, CancellationToken ct)
    {
        try
        {
            var refreshed = false;
            for (var attempt = 0; ; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Authorization = new("Bearer", await tokens.GetAsync(refreshed, ct));
                request.Headers.Range = range;
                var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.IsSuccessStatusCode || (range is not null && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)) return response;
                var status = response.StatusCode;
                response.Dispose();
                if (status == HttpStatusCode.Unauthorized && !refreshed) { refreshed = true; continue; }
                if ((status == HttpStatusCode.TooManyRequests || (int)status >= 500) && attempt < 2)
                { await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), ct); continue; }
                throw new DriveException(status switch
                {
                    HttpStatusCode.Unauthorized => DriveFailure.Authentication,
                    HttpStatusCode.Forbidden => DriveFailure.Permission,
                    HttpStatusCode.NotFound => DriveFailure.Missing,
                    HttpStatusCode.TooManyRequests => DriveFailure.Quota,
                    _ => DriveFailure.Network
                });
            }
        }
        catch (HttpRequestException) { throw new DriveException(DriveFailure.Network); }
    }
    private static string Escape(string id) => id.Replace("\\", "\\\\").Replace("'", "\\'");
}
