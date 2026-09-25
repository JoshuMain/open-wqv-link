using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using WqvLink.Core.Imaging;

namespace WqvLink.Core.Tests.TestData;

/// <summary>
/// Test-only PNG reader: walks chunks, checks every CRC and inflates 8-bit greyscale with filter type 0.
/// Deliberately independent of <see cref="PngWriter"/> apart from the CRC-32 routine, which is tested separately.
/// </summary>
internal sealed class MiniPngReader
{
    public sealed record Chunk(string Type, byte[] Data, bool CrcOk);

    public List<Chunk> Chunks { get; } = [];
    public int Width { get; private set; }
    public int Height { get; private set; }
    public byte BitDepth { get; private set; }
    public byte ColourType { get; private set; }
    public byte[] Pixels { get; private set; } = [];
    public Dictionary<string, string> Text { get; } = [];

    public static MiniPngReader Read(byte[] png)
    {
        var sig = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        if (!png.AsSpan(0, 8).SequenceEqual(sig))
        {
            throw new InvalidDataException("bad signature");
        }

        var r = new MiniPngReader();
        var pos = 8;
        while (pos < png.Length)
        {
            var len = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
            var type = Encoding.ASCII.GetString(png, pos + 4, 4);
            var data = png.AsSpan(pos + 8, len).ToArray();
            var crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos + 8 + len));
            r.Chunks.Add(new Chunk(type, data, Crc32.Compute(png.AsSpan(pos + 4, len + 4)) == crc));
            pos += 12 + len;
        }

        var ihdr = r.Chunks.First(c => c.Type == "IHDR").Data;
        r.Width = BinaryPrimitives.ReadInt32BigEndian(ihdr);
        r.Height = BinaryPrimitives.ReadInt32BigEndian(ihdr.AsSpan(4));
        r.BitDepth = ihdr[8];
        r.ColourType = ihdr[9];

        foreach (var t in r.Chunks.Where(c => c.Type == "tEXt"))
        {
            var nul = Array.IndexOf(t.Data, (byte)0);
            r.Text[Encoding.Latin1.GetString(t.Data, 0, nul)] = Encoding.Latin1.GetString(t.Data, nul + 1, t.Data.Length - nul - 1);
        }

        var idat = r.Chunks.Where(c => c.Type == "IDAT").SelectMany(c => c.Data).ToArray();
        using var z = new ZLibStream(new MemoryStream(idat), CompressionMode.Decompress);
        using var raw = new MemoryStream();
        z.CopyTo(raw);
        var rows = raw.ToArray();
        if (rows.Length != r.Height * (r.Width + 1))
        {
            throw new InvalidDataException($"IDAT inflated to {rows.Length} bytes");
        }
        r.Pixels = new byte[r.Width * r.Height];
        for (var y = 0; y < r.Height; y++)
        {
            if (rows[y * (r.Width + 1)] != 0)
            {
                throw new InvalidDataException("only filter type 0 is supported");
            }
            Array.Copy(rows, y * (r.Width + 1) + 1, r.Pixels, y * r.Width, r.Width);
        }
        return r;
    }
}
