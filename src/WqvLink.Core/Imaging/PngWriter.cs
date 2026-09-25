using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace WqvLink.Core.Imaging;

/// <summary>
/// Minimal 8-bit greyscale PNG encoder - Chunk order: IHDR, tEXt…, eXIf, IDAT, IEND.
/// </summary>
public static class PngWriter
{
    public static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Encodes <paramref name="grey8"/> (one byte per pixel).</summary>
    /// <param name="meta">Metadata for tEXt and eXIf chunks - null writes neither</param>
    /// <param name="scale">Nearest-neighbour upscale factor applied before encoding (1 = none)</param>
    public static byte[] Encode(byte[] grey8, int width, int height, PngMetadata? meta, int scale = 1)
    {
        ArgumentNullException.ThrowIfNull(grey8);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(scale, 1);
        if (grey8.Length != width * height)
        {
            throw new ArgumentException($"expected {width * height} pixels, got {grey8.Length}", nameof(grey8));
        }

        var (pixels, w, h) = Upscale(grey8, width, height, scale);

        using var png = new MemoryStream();
        png.Write(Signature);

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, w);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), h);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 0;  // colour type: greyscale
        ihdr[10] = 0; // compression
        ihdr[11] = 0; // filter method
        ihdr[12] = 0; // no interlace
        WriteChunk(png, "IHDR", ihdr);

        foreach (var (keyword, text) in meta?.TextEntries() ?? [])
        {
            var k = Latin1.Encode(keyword);
            var t = Latin1.Encode(text);
            var body = new byte[k.Length + 1 + t.Length];
            k.CopyTo(body, 0);
            t.CopyTo(body, k.Length + 1);
            WriteChunk(png, "tEXt", body);
        }

        if (meta is not null)
        {
            WriteChunk(png, "eXIf", ExifBuilder.Build(meta));
        }
        WriteChunk(png, "IDAT", Deflate(pixels, w, h));
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    //Nearest-neighbour upscale if the user wishes, returns the input unchanged for scale 1
    internal static (byte[] Pixels, int Width, int Height) Upscale(byte[] src, int width, int height, int scale)
    {
        if (scale <= 1)
        {
            return (src, width, height);
        }
        int w = width * scale, h = height * scale;
        var dst = new byte[w * h];
        for (var y = 0; y < h; y++)
        {
            var srcRow = y / scale * width;
            for (var x = 0; x < w; x++)
            {
                dst[y * w + x] = src[srcRow + x / scale];
            }
        }
        return (dst, w, h);
    }

    private static byte[] Deflate(byte[] pixels, int w, int h)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (var y = 0; y < h; y++)
            {
                z.WriteByte(0); // filter type None
                z.Write(pixels, y * w, w);
            }
        }
        return ms.ToArray();
    }

    private static void WriteChunk(Stream s, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buf, data.Length);
        s.Write(buf);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);

        var crc = Crc32.Finish(Crc32.Update(Crc32.Update(Crc32.Start, typeBytes), data));
        BinaryPrimitives.WriteUInt32BigEndian(buf, crc);
        s.Write(buf);
    }
}
