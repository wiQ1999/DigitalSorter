using System.Globalization;

namespace DigitalSorter.Core;

public enum MediaKind { Image, Video }
public enum MediaList { Chronological, Invalid }

public sealed record SourceFolder(string Id, string Name);
// Total is unknown until all source folders and pages have been discovered.
public sealed record MediaReadProgress(int Processed, int? Total);
public sealed record MediaAsset(string Id, string Name, string MimeType, MediaKind Kind,
    DateTimeOffset? CapturedAt, string FolderId, bool IsAvailable = true);
public sealed record MediaCollection(IReadOnlyList<MediaAsset> Chronological, IReadOnlyList<MediaAsset> Invalid)
{
    public static MediaCollection Empty { get; } = new([], []);
}

public interface IMediaLibrarySource
{
    Task<IReadOnlyList<SourceFolder>> GetFoldersAsync(string? parentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaAsset>> ReadAsync(IEnumerable<string> folderIds, CancellationToken cancellationToken = default,
        IProgress<MediaReadProgress>? progress = null);
}

public static class MediaFormats
{
    public static MediaKind? GetKind(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".png" or ".webp" or ".heic" or ".heif" => MediaKind.Image,
        ".mp4" or ".webm" or ".mov" => MediaKind.Video,
        _ => null
    };
}

public static class CaptureDate
{
    private static readonly string[] Formats =
    [
        "yyyy:MM:dd HH:mm:ss", "yyyy:MM:dd HH:mm:sszzz", "yyyy:MM:dd HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"
    ];

    public static DateTimeOffset? Parse(string? value) =>
        DateTimeOffset.TryParseExact(value?.Trim(), Formats, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date) ? date : null;
}

public static class MediaClassifier
{
    public static MediaCollection Classify(IEnumerable<MediaAsset> assets)
    {
        var supported = assets.Where(x => MediaFormats.GetKind(x.Name) is not null)
            .DistinctBy(x => x.Id, StringComparer.Ordinal).ToArray();
        // Stable ordering preserves source order for equal capture timestamps.
        return new(supported.Where(x => x.CapturedAt.HasValue).OrderBy(x => x.CapturedAt).ToArray(),
            supported.Where(x => !x.CapturedAt.HasValue).ToArray());
    }
}

public sealed class GalleryState
{
    private MediaCollection _collection = MediaCollection.Empty;
    public MediaList ActiveList { get; private set; }
    public int Index { get; private set; }
    public bool IsFullscreen { get; private set; }
    public IReadOnlyList<MediaAsset> Items => ActiveList == MediaList.Chronological
        ? _collection.Chronological : _collection.Invalid;
    public MediaAsset? Current => Items.Count == 0 ? null : Items[Index];
    public int Position => Current is null ? 0 : Index + 1;
    public int InvalidCount => _collection.Invalid.Count;
    public bool CanPrevious => Index > 0;
    public bool CanNext => Index + 1 < Items.Count;
    public IEnumerable<MediaAsset> GetUpcoming(int count) => Items.Skip(Index + 1).Take(count);

    public void Load(MediaCollection collection)
    {
        _collection = collection;
        ActiveList = MediaList.Chronological;
        Index = 0;
        IsFullscreen = false;
    }
    public void SelectList(MediaList list) { ActiveList = list; Index = 0; IsFullscreen = false; }
    public void Previous() { if (CanPrevious) Index--; }
    public void Next() { if (CanNext) Index++; }
    public void Select(int index) { if (index >= 0 && index < Items.Count) Index = index; }
    public void ToggleFullscreen() { if (Current is not null) IsFullscreen = !IsFullscreen; }
    public void ExitFullscreen() => IsFullscreen = false;
}
