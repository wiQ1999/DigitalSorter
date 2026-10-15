using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DigitalSorter.Core;
using DigitalSorter.Data.GoogleDrive;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace DigitalSorter.IntegrationTests;

public sealed class ControlledDrive(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); Requests.Add(request);
        return Task.FromResult(respond(request));
    }
    public static HttpResponseMessage Json(object data) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json") };
}
public sealed class ControlledToken : IAccessTokenProvider
{
    public int Forced;
    public Task<string> GetAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    { if (forceRefresh) Forced++; return Task.FromResult(forceRefresh ? "fresh" : "initial"); }
}

public sealed class DriveTests
{
    private static object File(string id, string name, string? captured, string mime = "image/jpeg", bool available = true) =>
        new { id, name, mimeType = mime, imageMediaMetadata = new { time = captured }, capabilities = new { canDownload = available }, createdTime = "2026-01-01T00:00:00Z", modifiedTime = "2026-02-01T00:00:00Z" };

    [Fact]
    public async Task PublicSourceTraversesPagesAndFoldersAndDeduplicatesOverlappingSources()
    {
        using var handler = new ControlledDrive(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            var query = QueryHelpers.ParseQuery(request.RequestUri!.Query);
            Assert.Equal("true", query["supportsAllDrives"]);
            if (query["q"].ToString().Contains("'nested'")) return ControlledDrive.Json(new { files = new[] { File("same", "shared.jpg", "2020-01-01T00:00:00Z"), File("video", "movie.mp4", "2021-01-01T00:00:00Z", "video/mp4") } });
            if (query.ContainsKey("pageToken")) return ControlledDrive.Json(new { files = new[] { File("later", "later.jpg", "2022-01-01T00:00:00Z") } });
            return ControlledDrive.Json(new { nextPageToken = "second", files = new[] { File("same", "shared.jpg", "2020-01-01T00:00:00Z"), File("nested", "Subfolder", null, "application/vnd.google-apps.folder"), File("other", "notes.txt", null) } });
        });
        var source = new GoogleDriveSource(new(handler), new ControlledToken());
        var assets = await source.ReadAsync(["main", "main", "nested"]);
        Assert.Equal(3, assets.Count);
        Assert.Single(assets, x => x.Id == "same");
        Assert.Equal(3, handler.Requests.Count);
        Assert.DoesNotContain(assets, x => x.Name == "notes.txt");
    }
    [Fact]
    public async Task MissingCaptureMetadataNeverFallsBackToUploadOrModificationDate()
    {
        using var handler = new ControlledDrive(request => request.RequestUri!.Query.Contains("alt=media")
            ? new(HttpStatusCode.PartialContent) { Content = new ByteArrayContent([1, 2, 3]) }
            : ControlledDrive.Json(new { files = new[] { File("undated", "undated.jpg", null) } }));
        var assets = await new GoogleDriveSource(new(handler), new ControlledToken()).ReadAsync(["source"]);
        Assert.Null(Assert.Single(assets).CapturedAt);
        Assert.Single(MediaClassifier.Classify(assets).Invalid);
    }
    [Fact]
    public async Task MissingFileRemainsInInvalidListAndDoesNotStopOtherMedia()
    {
        using var handler = new ControlledDrive(request => request.RequestUri!.Query.Contains("alt=media")
            ? new(HttpStatusCode.NotFound)
            : ControlledDrive.Json(new { files = new[] { File("deleted", "deleted.jpg", null), File("valid", "photo.png", "2020-01-01T00:00:00Z") } }));
        var assets = await new GoogleDriveSource(new(handler), new ControlledToken()).ReadAsync(["source"]);
        Assert.False(assets.Single(x => x.Id == "deleted").IsAvailable);
        Assert.Single(MediaClassifier.Classify(assets).Chronological);
    }
    [Fact]
    public async Task DownloadRestrictionsAreReportedWithoutReadingFileBody()
    {
        using var handler = new ControlledDrive(_ => ControlledDrive.Json(new { files = new[] { File("denied", "photo.jpg", null, available: false) } }));
        var assets = await new GoogleDriveSource(new(handler), new ControlledToken()).ReadAsync(["source"]);
        Assert.False(Assert.Single(assets).IsAvailable); Assert.Single(handler.Requests);
    }
    [Fact]
    public async Task FolderListingIncludesSharedFoldersAndEscapesParentId()
    {
        using var handler = new ControlledDrive(_ => ControlledDrive.Json(new { files = new[] { new { id = "folder", name = "Wspólne", mimeType = "application/vnd.google-apps.folder" } } }));
        var source = new GoogleDriveSource(new(handler), new ControlledToken());
        Assert.Equal("Wspólne", Assert.Single(await source.GetFoldersAsync(null)).Name);
        Assert.Contains("sharedWithMe", QueryHelpers.ParseQuery(handler.Requests[0].RequestUri!.Query)["q"].ToString());
        await source.GetFoldersAsync("a'b");
        Assert.Contains("a\\'b", QueryHelpers.ParseQuery(handler.Requests[1].RequestUri!.Query)["q"].ToString());
    }
    [Fact]
    public async Task UnauthorizedRequestRefreshesOnceAndRetriesWithNewToken()
    {
        using var handler = new ControlledDrive(request => request.Headers.Authorization!.Parameter == "initial"
            ? new(HttpStatusCode.Unauthorized) : ControlledDrive.Json(new { files = Array.Empty<object>() }));
        var tokens = new ControlledToken();
        await new GoogleDriveSource(new(handler), tokens).GetFoldersAsync(null);
        Assert.Equal(2, handler.Requests.Count); Assert.Equal(1, tokens.Forced);
    }
    [Theory]
    [InlineData(HttpStatusCode.Forbidden, DriveFailure.Permission, 1)]
    [InlineData(HttpStatusCode.NotFound, DriveFailure.Missing, 1)]
    [InlineData(HttpStatusCode.Unauthorized, DriveFailure.Authentication, 2)]
    [InlineData(HttpStatusCode.TooManyRequests, DriveFailure.Quota, 3)]
    [InlineData(HttpStatusCode.ServiceUnavailable, DriveFailure.Network, 3)]
    public async Task FailuresHaveBoundedRetriesAndUsefulReason(HttpStatusCode status, DriveFailure reason, int count)
    {
        using var handler = new ControlledDrive(_ => new(status));
        var error = await Assert.ThrowsAsync<DriveException>(() => new GoogleDriveSource(new(handler), new ControlledToken()).GetFoldersAsync(null));
        Assert.Equal(reason, error.Reason); Assert.Equal(count, handler.Requests.Count);
    }
    [Fact]
    public async Task CancellationStopsRemoteReads()
    {
        using var handler = new ControlledDrive(_ => throw new InvalidOperationException("Should not run"));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GoogleDriveSource(new(handler), new ControlledToken()).GetFoldersAsync(null, cancellation.Token));
    }
    [Fact]
    public async Task RangeReaderAcceptsEndOfFileAndRejectsIgnoredTailRange()
    {
        using var end = new ControlledDrive(_ => new(HttpStatusCode.RequestedRangeNotSatisfiable));
        Assert.Empty(await new GoogleDriveSource(new(end), new ControlledToken()).ReadRangeAsync("image", 100, 100, default));
        using var ignored = new ControlledDrive(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        await Assert.ThrowsAsync<DriveException>(() => new GoogleDriveSource(new(ignored), new ControlledToken()).ReadRangeAsync("image", 100, 100, default));
    }
}

public sealed class MetadataTests
{
    [Theory]
    [InlineData("landscape.jpg")] [InlineData("landscape.png")] [InlineData("landscape.webp")]
    [InlineData("landscape.heic")] [InlineData("landscape.heif")]
    [InlineData("motion.mp4")] [InlineData("motion.mov")] [InlineData("motion.webm")]
    public async Task RealSupportedContainersExposeTheirEmbeddedCaptureDate(string name)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", name));
        var date = await CaptureMetadata.ReadAsync(name, (offset, length, _) => Task.FromResult(bytes.AsSpan((int)Math.Min(offset, bytes.Length), (int)Math.Min(length, Math.Max(0, bytes.Length - offset))).ToArray()), default, bytes.Length);
        Assert.Equal(DateTimeOffset.Parse("2020-01-02T03:04:05Z"), date);
    }
    [Fact]
    public void EmbeddedExifOriginalTimestampIsReadWithoutFileSystem()
    {
        var bytes = ExifTiff("2020:03:04 12:34:56");
        Assert.Equal(DateTimeOffset.Parse("2020-03-04T12:34:56Z"), CaptureMetadata.ReadEmbedded(bytes, true));
    }
    [Fact]
    public async Task InvalidOrMissingEmbeddedDateIsNotInvented()
    {
        foreach (var name in new[] { "a.jpg", "a.png", "a.webp", "a.heic", "a.heif", "a.mp4", "a.mov", "a.webm" })
            Assert.Null(await CaptureMetadata.ReadAsync(name, (_, _, _) => Task.FromResult(new byte[] { 0, 0, 0 }), default));
    }
    [Fact]
    public async Task WebmRecordedTagIsReadButMuxTimestampIsNotCaptureTime()
    {
        var name = Encoding.UTF8.GetBytes("DATE_RECORDED"); var date = Encoding.UTF8.GetBytes("2020-01-02T03:04:05Z");
        byte[] tags = [0x45, 0xA3, (byte)(0x80 | name.Length), .. name, 0x44, 0x87, (byte)(0x80 | date.Length), .. date];
        byte[] simpleTag = [0x67, 0xC8, (byte)(0x80 | tags.Length), .. tags];
        byte[] tag = [0x73, 0x73, (byte)(0x80 | simpleTag.Length), .. simpleTag];
        byte[] container = [0x1A, 0x45, 0xDF, 0xA3, 0x80, 0x18, 0x53, 0x80, 0x67, 0xFF, 0x12, 0x54, 0xC3, 0x67, (byte)(0x80 | tag.Length), .. tag];
        var actual = await CaptureMetadata.ReadAsync("clip.webm", (offset, length, _) => Task.FromResult(container.AsSpan((int)Math.Min(offset, container.Length), (int)Math.Min(length, Math.Max(0, container.Length - offset))).ToArray()), default, container.Length);
        Assert.Equal(DateTimeOffset.Parse("2020-01-02T03:04:05Z"), actual);
        byte[] muxDate = [0x44, 0x61, 0x88, 0, 0, 0, 0, 0, 0, 0, 0];
        Assert.Null(await CaptureMetadata.ReadAsync("clip.webm", (_, _, _) => Task.FromResult(muxDate), default));
        Assert.Null(await CaptureMetadata.ReadAsync("clip.webm", (_, _, _) => Task.FromResult(tags), default));
    }
    [Fact]
    public async Task QuicktimeReaderSkipsMediaPayloadToFindMovieMetadata()
    {
        var requested = new List<long>();
        byte[] header = [0, 0, 16, 0, (byte)'m', (byte)'d', (byte)'a', (byte)'t', 0, 0, 0, 0, 0, 0, 0, 0];
        var result = await CaptureMetadata.ReadAsync("movie.mov", (offset, _, _) => { requested.Add(offset); return Task.FromResult(offset == 0 ? header : Array.Empty<byte>()); }, default);
        Assert.Null(result); Assert.Equal([0L, 4096L], requested);
    }
    public static byte[] ExifTiff(string date)
    {
        var bytes = new byte[80]; bytes[0] = (byte)'I'; bytes[1] = (byte)'I'; bytes[2] = 42; bytes[4] = 8;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10), 0x8769);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(14), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(18), 26);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28), 0x9003);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(30), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32), 20);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(36), 44);
        Encoding.ASCII.GetBytes(date + "\0").CopyTo(bytes, 44);
        return bytes;
    }
}
