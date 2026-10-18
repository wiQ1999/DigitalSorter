using System.Net;
using System.Net.Http.Json;
using DigitalSorter.Contracts;
using DigitalSorter.Data.GoogleDrive;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace DigitalSorter.IntegrationTests;

public sealed class LocalTvTests
{
    [Fact]
    public async Task PairingCanUseLanAddressWithoutChangingGoogleCallback()
    {
        using var factory = new BrokerFactory { PairingOrigin = "https://192.168.0.222:7443/" };
        using var client = factory.Client();
        var response = await client.PostAsJsonAsync("/api/pair/start", new PairingRequest("TV"));
        var pair = (await response.Content.ReadFromJsonAsync<PairingStarted>())!;
        Assert.StartsWith("https://192.168.0.222:7443/pair?code=", pair.VerificationUri);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/pair/qr?uri=" + Uri.EscapeDataString(pair.VerificationUri))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/pair/qr?uri=" + Uri.EscapeDataString("https://evil.example/pair?code=ABCD2345"))).StatusCode);
        var login = await client.GetAsync("/auth/login");
        Assert.Equal("https://localhost/auth/callback", QueryHelpers.ParseQuery(login.Headers.Location!.Query)["redirect_uri"]);
    }

    [Theory]
    [InlineData("http://192.168.0.222:7443")]
    [InlineData("https://example.com/path")]
    [InlineData("https://user:password@example.com")]
    [InlineData("https://example.com?redirect=evil")]
    public void PairingOriginIsExplicitAndValidated(string origin)
    {
        using var factory = new BrokerFactory { PairingOrigin = origin };
        Assert.Contains("Broker:PairingOrigin", Assert.Throws<InvalidOperationException>(() => factory.Client()).ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LargeJpegMetadataDoesNotDownloadCompressedPixels(bool dated)
    {
        var tiff = MetadataTests.ExifTiff("2020:03:04 12:34:56");
        byte[] exif = [(byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0, .. tiff];
        var size = exif.Length + 2;
        byte[] prefix = dated ? [0xff, 0xd8, 0xff, 0xe1, (byte)(size >> 8), (byte)size, .. exif, 0xff, 0xda, 0, 2] : [0xff, 0xd8, 0xff, 0xda, 0, 2];
        var photo = new byte[12 * 1024 * 1024];
        prefix.CopyTo(photo, 0);
        var transferred = 0;
        var date = await CaptureMetadata.ReadAsync("photo.jpg", (offset, length, _) =>
        {
            var bytes = photo.AsSpan((int)offset, Math.Min(length, photo.Length - (int)offset)).ToArray();
            transferred += bytes.Length;
            return Task.FromResult(bytes);
        }, default, photo.Length);
        Assert.Equal(dated ? DateTimeOffset.Parse("2020-03-04T12:34:56Z") : null, date);
        Assert.InRange(transferred, 1, 64 * 1024);
    }
}
