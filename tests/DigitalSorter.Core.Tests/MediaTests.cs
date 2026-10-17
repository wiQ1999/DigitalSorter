using DigitalSorter.Core;
using Xunit;

namespace DigitalSorter.Core.Tests;

public sealed class FormatTests
{
    [Theory]
    [InlineData("a.jpg", MediaKind.Image)] [InlineData("a.jpeg", MediaKind.Image)]
    [InlineData("a.PNG", MediaKind.Image)] [InlineData("a.webp", MediaKind.Image)]
    [InlineData("a.heic", MediaKind.Image)] [InlineData("a.HEIF", MediaKind.Image)]
    [InlineData("a.mp4", MediaKind.Video)] [InlineData("a.WebM", MediaKind.Video)]
    [InlineData("a.MOV", MediaKind.Video)]
    public void SupportedExtensionsAreClassified(string name, MediaKind expected)
    {
        var actual = MediaFormats.GetKind(name);
        Assert.Equal(expected, actual);
    }
    [Theory]
    [InlineData("photo.gif")] [InlineData("video.avi")] [InlineData("photo.jpg.exe")]
    [InlineData("")] [InlineData("photo")] [InlineData("document.txt")]
    public void OtherFormatsAreIgnored(string name) => Assert.Null(MediaFormats.GetKind(name));
}

public sealed class DateTests
{
    [Theory]
    [InlineData("2024:02:29 12:34:56", "2024-02-29T12:34:56Z")]
    [InlineData("2024:02:29 12:34:56+02:00", "2024-02-29T10:34:56Z")]
    [InlineData("2024-02-29T12:34:56Z", "2024-02-29T12:34:56Z")]
    [InlineData("2024-02-29T12:34:56.123Z", "2024-02-29T12:34:56.123Z")]
    [InlineData(" 2024-02-29T12:34:56-05:00 ", "2024-02-29T17:34:56Z")]
    [InlineData("2000:02:29 00:00:00", "2000-02-29T00:00:00Z")]
    [InlineData("2020-12-31T23:59:59+14:00", "2020-12-31T09:59:59Z")]
    [InlineData("2020-01-01T00:00:00-12:00", "2020-01-01T12:00:00Z")]
    [InlineData("2020-01-01T00:00:00.1234567Z", "2020-01-01T00:00:00.1234567Z")]
    [InlineData("2020:06:01 01:00:00-03:30", "2020-06-01T04:30:00Z")]
    [InlineData("2020:01:01 00:00:00Z", "2020-01-01T00:00:00Z")]
    public void FullCaptureTimestampsAreNormalized(string raw, string expected)
    {
        var result = CaptureDate.Parse(raw);
        Assert.Equal(DateTimeOffset.Parse(expected), result);
        Assert.Equal(TimeSpan.Zero, result!.Value.Offset);
    }
    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("not a date")]
    [InlineData("2023:02:29 12:00:00")] [InlineData("2024-02-30T12:00:00Z")]
    [InlineData("2024-02-29")] [InlineData("2024:00:12 10:00:00")]
    [InlineData("2024:12:12 25:00:00")] [InlineData("2024-02-29T12:34Z")]
    [InlineData("1900:02:29 12:00:00")] [InlineData("2100:02:29 12:00:00")]
    [InlineData("2020:04:31 12:00:00")] [InlineData("2020:06:31 12:00:00")]
    [InlineData("2020:09:31 12:00:00")] [InlineData("2020:11:31 12:00:00")]
    [InlineData("2020:13:01 12:00:00")] [InlineData("2020:01:00 12:00:00")]
    [InlineData("2020:01:01 23:60:00")] [InlineData("2020:01:01 23:59:60")]
    [InlineData("2020-01-01T00:00:00+15:00")] [InlineData("2020-01-01T00:00:00+01:60")]
    [InlineData("0000:01:01 12:00:00")] [InlineData("2020:01:01")]
    [InlineData("12:00:00")] [InlineData("01/02/2020 12:00:00")]
    public void MissingPartialOrInvalidCaptureTimestampIsInvalid(string? raw) => Assert.Null(CaptureDate.Parse(raw));
}

public sealed class ClassifierTests
{
    private static MediaAsset Asset(string id, DateTimeOffset? captured, string name = "photo.jpg", string folder = "one", bool available = true) =>
        new(id, name, "image/jpeg", MediaKind.Image, captured, folder, available);

    [Fact]
    public void ValidMediaIsChronologicalAndUndatedMediaRemainsSeparate()
    {
        var early = DateTimeOffset.Parse("2000-01-01T00:00:00Z");
        var late = early.AddYears(10);
        var assets = new[] { Asset("late", late), Asset("missing", null), Asset("early", early), Asset("ignored", null, "notes.txt") };
        var result = MediaClassifier.Classify(assets);
        Assert.Equal(["early", "late"], result.Chronological.Select(x => x.Id));
        Assert.Equal("missing", Assert.Single(result.Invalid).Id);
    }
    [Fact]
    public void EqualDatesPreserveSourceOrderRegardlessOfNamesFoldersOrAvailability()
    {
        var time = DateTimeOffset.Parse("2020-01-01T00:00:00Z");
        var result = MediaClassifier.Classify([Asset("first", time, "z.jpg", "z", false), Asset("second", time, "a.jpg", "a")]);
        Assert.Equal(["first", "second"], result.Chronological.Select(x => x.Id));
    }
    [Fact]
    public void RepeatedFilesAcrossFoldersAppearOnce()
    {
        var result = MediaClassifier.Classify([Asset("same", null), Asset("same", null, folder: "two")]);
        Assert.Single(result.Invalid);
    }
    [Fact]
    public void EmptyInputHasTwoEmptyLists()
    {
        var result = MediaClassifier.Classify([]);
        Assert.Empty(result.Chronological); Assert.Empty(result.Invalid);
        Assert.Empty(MediaCollection.Empty.Chronological); Assert.Empty(MediaCollection.Empty.Invalid);
    }
}

public sealed class GalleryTests
{
    private static MediaCollection Collection(int count = 8) => new(
        Enumerable.Range(0, count).Select(i => new MediaAsset(i.ToString(), "photo.jpg", "image/jpeg", MediaKind.Image,
            DateTimeOffset.UnixEpoch.AddDays(i), "source")).ToArray(),
        [new("invalid", "undated.mp4", "video/mp4", MediaKind.Video, null, "source")]);

    [Fact]
    public void EmptyGalleryCannotNavigateOrEnterFullscreen()
    {
        var state = new GalleryState();
        state.Previous(); state.Next(); state.Select(-1); state.Select(1); state.ToggleFullscreen();
        Assert.Null(state.Current); Assert.Equal(0, state.Position); Assert.False(state.CanPrevious); Assert.False(state.CanNext);
        Assert.False(state.IsFullscreen); Assert.Empty(state.GetUpcoming(5));
    }
    [Fact]
    public void NavigationStopsAtBothBoundaries()
    {
        var state = new GalleryState(); state.Load(Collection(2));
        state.Previous(); Assert.Equal(1, state.Position);
        state.Next(); state.Next(); Assert.Equal(2, state.Position); Assert.False(state.CanNext);
        state.Previous(); Assert.Equal(1, state.Position); Assert.False(state.CanPrevious);
    }
    [Theory]
    [InlineData(-1, 0)] [InlineData(0, 0)] [InlineData(1, 1)] [InlineData(3, 3)] [InlineData(6, 6)] [InlineData(20, 7)]
    public void UpcomingUsesRequestedCapacityAndNeverIncludesCurrentOrMissingItems(int capacity, int expected)
    {
        var state = new GalleryState(); state.Load(Collection());
        Assert.Equal(Enumerable.Range(1, expected).Select(x => x.ToString()), state.GetUpcoming(capacity).Select(x => x.Id));
        state.Select(6); Assert.Equal(capacity > 0 ? new[] { "7" } : [], state.GetUpcoming(capacity).Select(x => x.Id));
        state.Select(7); Assert.Empty(state.GetUpcoming(capacity));
    }
    [Fact]
    public void SelectionIgnoresInvalidIndexes()
    {
        var state = new GalleryState(); state.Load(Collection(2)); state.Select(1); state.Select(-1); state.Select(2);
        Assert.Equal("1", state.Current!.Id);
    }
    [Fact]
    public void FullscreenTogglesAndCanBeExited()
    {
        var state = new GalleryState(); state.Load(Collection());
        state.ToggleFullscreen(); Assert.True(state.IsFullscreen);
        state.ToggleFullscreen(); Assert.False(state.IsFullscreen);
        state.ToggleFullscreen(); state.ExitFullscreen(); Assert.False(state.IsFullscreen);
    }
    [Fact]
    public void ListChangeResetsPositionAndFullscreen()
    {
        var state = new GalleryState(); state.Load(Collection()); state.Select(4); state.ToggleFullscreen();
        state.SelectList(MediaList.Invalid);
        Assert.Equal("invalid", state.Current!.Id); Assert.Equal(1, state.Position); Assert.Equal(1, state.InvalidCount); Assert.False(state.IsFullscreen);
        state.SelectList(MediaList.Chronological); Assert.Equal("0", state.Current!.Id);
    }
    [Fact]
    public void LoadingAnotherCollectionResetsListAndPosition()
    {
        var state = new GalleryState(); state.Load(Collection()); state.SelectList(MediaList.Invalid); state.ToggleFullscreen();
        state.Load(Collection(0));
        Assert.Equal(MediaList.Chronological, state.ActiveList); Assert.Equal(0, state.Index); Assert.False(state.IsFullscreen); Assert.Null(state.Current);
    }
}
