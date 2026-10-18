using DigitalSorter.Core;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace DigitalSorter.Data.GoogleDrive;

public static class CaptureMetadata
{
    public static async Task<DateTimeOffset?> ReadAsync(string name, Func<long, int, CancellationToken, Task<byte[]>> read, CancellationToken ct, long? fileSize = null)
    {
        try
        {
            var extension = Path.GetExtension(name).ToLowerInvariant();
            if (extension is ".jpg" or ".jpeg")
            {
                var jpeg = await new JpegCapture(read, ct).ReadAsync();
                if (jpeg.Jpeg) return jpeg.Date;
            }
            if (extension is ".mp4" or ".mov")
            {
                return await QuickTimeCapture.ReadAsync(read, ct);
            }
            if (extension == ".webm")
            {
                return await WebmCapture.ReadAsync(read, ct, fileSize);
            }
            // Image metadata is decoded in memory and never written to a file/cache.
            using var memory = new MemoryStream();
            const int chunk = 1024 * 1024;
            for (long offset = 0; fileSize is null || offset < fileSize; offset += chunk)
            {
                var bytes = await read(offset, chunk, ct);
                memory.Write(bytes);
                if (bytes.Length < chunk) break;
            }
            return ReadEmbedded(memory.ToArray(), true);
        }
        catch (Exception ex) when (ex is ImageProcessingException or IOException or ArgumentException or OverflowException or InvalidOperationException)
        { return null; }
    }

    public static DateTimeOffset? ReadEmbedded(byte[] bytes, bool image)
    {
        var directories = ImageMetadataReader.ReadMetadata(new MemoryStream(bytes));
        foreach (var directory in directories.OfType<ExifSubIfdDirectory>())
        {
            var original = directory.GetString(ExifDirectoryBase.TagDateTimeOriginal);
            // EXIF OffsetTimeOriginal is optional; offset-free capture times use UTC consistently.
            var offset = directory.GetString(0x9011);
            if (CaptureDate.Parse(original + offset) is { } date) return date;
        }
        if (image) return null;
        foreach (var directory in directories.Where(x => x.GetType().Name == "QuickTimeMetadataHeaderDirectory"))
        foreach (var tag in directory.Tags.Where(x => x.Name.Contains("Creation", StringComparison.OrdinalIgnoreCase)))
        {
            if (CaptureDate.Parse(directory.GetString(tag.Type)) is { } date) return date;
        }
        // QuickTime's embedded creation timestamp is metadata, never Drive createdTime/modifiedTime.
        foreach (var directory in directories.Where(x => x.GetType().Name == "QuickTimeMovieHeaderDirectory"))
        foreach (var tag in directory.Tags.Where(x => x.Name is "Created" or "Creation Time" or "Creation Date"))
        {
            if (directory.GetObject(tag.Type) is DateTime date && date.Year > 1904)
                return new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc));
            if (CaptureDate.Parse(directory.GetString(tag.Type)) is { } parsed) return parsed;
        }
        return null;
    }

}
