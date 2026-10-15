using System.Text;
using DigitalSorter.Core;

namespace DigitalSorter.Data.GoogleDrive;

internal static class WebmCapture
{
    private const long Segment = 0x18538067, SeekHead = 0x114D9B74, Tags = 0x1254C367;
    private const int MaximumMetadata = 2 * 1024 * 1024;

    public static async Task<DateTimeOffset?> ReadAsync(Func<long, int, CancellationToken, Task<byte[]>> read, CancellationToken ct, long? fileSize)
    {
        var header = Decode(await read(0, 16, ct));
        if (header.Id != 0x1A45DFA3 || header.Unknown || header.Size > MaximumMetadata) return null;
        var offset = header.Width + header.Size;
        header = Decode(await read(offset, 16, ct));
        if (header.Id != Segment) return null;
        var segmentStart = offset + header.Width;
        offset = segmentStart;
        var end = header.Unknown ? fileSize ?? long.MaxValue : segmentStart + header.Size;
        for (var count = 0; count < 4096 && offset < end; count++)
        {
            header = Decode(await read(offset, 16, ct));
            if (header.Width == 0) return null;
            if (!header.Unknown && header.Size <= MaximumMetadata)
            {
                if (header.Id == Tags)
                    return FindRecordedDate(await read(offset + header.Width, (int)header.Size, ct));
                if (header.Id == SeekHead)
                {
                    var target = FindTagsOffset(await read(offset + header.Width, (int)header.Size, ct));
                    if (target.HasValue && target.Value <= long.MaxValue - segmentStart)
                    {
                        var targetOffset = segmentStart + target.Value;
                        var tagHeader = Decode(await read(targetOffset, 16, ct));
                        if (tagHeader.Id == Tags && !tagHeader.Unknown && tagHeader.Size <= MaximumMetadata)
                            return FindRecordedDate(await read(targetOffset + tagHeader.Width, (int)tagHeader.Size, ct));
                    }
                }
            }
            if (header.Unknown || header.Size > long.MaxValue - offset - header.Width) return null;
            offset += header.Width + header.Size;
        }
        return null;
    }

    private static long? FindTagsOffset(byte[] bytes)
    {
        foreach (var entry in Elements(bytes))
        {
            if (entry.Id != 0x4DBB) continue;
            long id = 0, position = -1;
            foreach (var field in Elements(entry.Body))
            {
                if (field.Body.Length > 8) continue;
                long value = 0;
                foreach (var b in field.Body) value = checked((value << 8) | b);
                if (field.Id == 0x53AB) id = value;
                if (field.Id == 0x53AC) position = value;
            }
            if (id == Tags && position >= 0) return position;
        }
        return null;
    }
    private static DateTimeOffset? FindRecordedDate(byte[] bytes, int depth = 0)
    {
        if (depth > 12) return null;
        string? name = null, value = null;
        foreach (var element in Elements(bytes))
        {
            if (element.Id is 0x7373 or 0x67C8)
            {
                if (FindRecordedDate(element.Body, depth + 1) is { } date) return date;
            }
            if (element.Id == 0x45A3 && element.Body.Length <= 64) name = Encoding.UTF8.GetString(element.Body);
            if (element.Id == 0x4487 && element.Body.Length <= 128) value = Encoding.UTF8.GetString(element.Body);
        }
        return name is "DATE_RECORDED" or "DATE_SHOT" ? CaptureDate.Parse(value) : null;
    }
    private static IEnumerable<(long Id, byte[] Body)> Elements(byte[] bytes)
    {
        var offset = 0;
        while (offset < bytes.Length)
        {
            var header = Decode(bytes.AsSpan(offset));
            if (header.Width == 0 || header.Unknown || header.Size > bytes.Length - offset - header.Width) yield break;
            yield return (header.Id, bytes.AsSpan(offset + header.Width, (int)header.Size).ToArray());
            offset += header.Width + (int)header.Size;
        }
    }
    private static (long Id, long Size, int Width, bool Unknown) Decode(ReadOnlySpan<byte> bytes)
    {
        var (id, idWidth) = Vint(bytes, false);
        if (idWidth == 0 || idWidth > 4) return default;
        var (size, width) = Vint(bytes[idWidth..], true);
        if (width == 0) return default;
        return (id, size, idWidth + width, size == (1L << (width * 7)) - 1);
    }
    private static (long Value, int Width) Vint(ReadOnlySpan<byte> bytes, bool removeMarker)
    {
        if (bytes.IsEmpty || bytes[0] == 0) return default;
        var width = 1; var marker = 0x80;
        while ((bytes[0] & marker) == 0) { width++; marker >>= 1; }
        if (width > 8 || bytes.Length < width) return default;
        long value = removeMarker ? bytes[0] & (marker - 1) : bytes[0];
        for (var i = 1; i < width; i++) value = (value << 8) | bytes[i];
        return (value, width);
    }
}
