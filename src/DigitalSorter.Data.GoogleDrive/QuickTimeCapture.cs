using System.Buffers.Binary;
using System.Text;

namespace DigitalSorter.Data.GoogleDrive;

internal static class QuickTimeCapture
{
    public static async Task<DateTimeOffset?> ReadAsync(Func<long, int, CancellationToken, Task<byte[]>> read, CancellationToken ct)
    {
        byte[]? ftyp = null;
        for (long offset = 0, atoms = 0; atoms < 4096; atoms++)
        {
            var header = await read(offset, 16, ct);
            if (header.Length < 8) return null;
            var size = (long)BinaryPrimitives.ReadUInt32BigEndian(header);
            var type = Encoding.ASCII.GetString(header, 4, 4);
            if (size == 1)
            {
                if (header.Length < 16) return null;
                var wide = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8));
                if (wide > long.MaxValue) return null;
                size = (long)wide;
            }
            if (size < 8 || size > long.MaxValue - offset) return null;
            if (type == "ftyp" && size <= 4096) ftyp = await read(offset, (int)size, ct);
            if (type == "moov" && size <= 16 * 1024 * 1024)
            {
                var movie = await read(offset, (int)size, ct);
                return CaptureMetadata.ReadEmbedded([.. ftyp ?? [], .. movie], false);
            }
            offset += size;
        }
        return null;
    }
}
