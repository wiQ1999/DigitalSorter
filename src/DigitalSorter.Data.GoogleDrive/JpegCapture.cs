using System.Buffers.Binary;

namespace DigitalSorter.Data.GoogleDrive;

// Walk JPEG headers without downloading the compressed image after Start Of Scan.
internal sealed class JpegCapture(Func<long, int, CancellationToken, Task<byte[]>> read, CancellationToken ct)
{
    private byte[] _block = [];
    private long _start = -1;
    public async Task<(bool Jpeg, DateTimeOffset? Date)> ReadAsync()
    {
        var signature = await Bytes(0, 2);
        if (!signature.AsSpan().SequenceEqual(new byte[] { 0xff, 0xd8 })) return (false, null);
        for (long offset = 2; ;)
        {
            ct.ThrowIfCancellationRequested();
            var header = await Bytes(offset, 4);
            if (header.Length < 4 || header[0] != 0xff) return (true, null);
            if (header[1] == 0xff) { offset++; continue; }
            if (header[1] is 0xda or 0xd9) return (true, null);
            var size = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2));
            if (size < 2) return (true, null);
            if (header[1] == 0xe1)
            {
                var segment = await Bytes(offset, size + 2);
                if (segment.Length != size + 2) return (true, null);
                if (CaptureMetadata.ReadEmbedded([0xff, 0xd8, .. segment, 0xff, 0xd9], true) is { } date)
                    return (true, date);
            }
            offset += size + 2;
        }
    }

    private async Task<byte[]> Bytes(long offset, int length)
    {
        using var result = new MemoryStream(length);
        while (result.Length < length)
        {
            if (offset < _start || offset >= _start + _block.Length)
            {
                _start = offset;
                _block = await read(offset, 64 * 1024, ct);
                if (_block.Length == 0) break;
            }
            var index = (int)(offset - _start);
            var count = Math.Min(length - (int)result.Length, _block.Length - index);
            result.Write(_block, index, count);
            offset += count;
        }
        return result.ToArray();
    }
}
