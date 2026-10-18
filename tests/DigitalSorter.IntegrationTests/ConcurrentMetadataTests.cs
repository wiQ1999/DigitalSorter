using System.Net;
using DigitalSorter.Core;
using DigitalSorter.Data.GoogleDrive;
using Xunit;

namespace DigitalSorter.IntegrationTests;

public sealed class ConcurrentMetadataTests
{
    private sealed class DelayedDrive : HttpMessageHandler
    {
        public readonly TaskCompletionSource ThreeStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Active, Maximum, Started;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (!request.RequestUri!.Query.Contains("alt=media"))
                return ControlledDrive.Json(new { files = Enumerable.Range(0, 8).Select(i => new {
                    id = $"photo{i}", name = $"photo{i}.heic", mimeType = "image/heic"
                }) });
            var active = Interlocked.Increment(ref Active);
            Interlocked.Increment(ref Started);
            lock (Release) Maximum = Math.Max(Maximum, active);
            if (active == 3) ThreeStarted.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(ct);
                return new(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(MetadataTests.ExifTiff("2020:03:04 12:34:56")) };
            }
            finally { Interlocked.Decrement(ref Active); }
        }
    }

    [Fact]
    public async Task ReadsOverlapAtMostThreeAndKeepSourceOrder()
    {
        using var handler = new DelayedDrive();
        var read = new GoogleDriveSource(new(handler), new ControlledToken()).ReadAsync(["folder"]);
        await handler.ThreeStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(3, handler.Started);
        handler.Release.TrySetResult();
        var assets = await read;
        Assert.Equal(3, handler.Maximum);
        Assert.Equal(0, handler.Active);
        Assert.Equal(Enumerable.Range(0, 8).Select(i => $"photo{i}"), assets.Select(x => x.Id));
        Assert.All(assets, asset => Assert.NotNull(asset.CapturedAt));
    }

    [Fact]
    public async Task CancellingAbortsAllInflightMetadataRequests()
    {
        using var handler = new DelayedDrive();
        using var cancellation = new CancellationTokenSource();
        var read = new GoogleDriveSource(new(handler), new ControlledToken()).ReadAsync(["folder"], cancellation.Token);
        await handler.ThreeStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        Assert.Equal(0, handler.Active);
        Assert.Equal(3, handler.Started);
    }
}
