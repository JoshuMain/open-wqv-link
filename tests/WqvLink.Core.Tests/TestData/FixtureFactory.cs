using System.Text;
using WqvLink.Core.Imaging;
using static WqvLink.Core.Protocol.WqvConstants;

namespace WqvLink.Core.Tests.TestData;

/// <summary>Generates synthetic image records in code. No real photos live in the test project.</summary>
internal static class FixtureFactory
{
    /// <summary>Builds one 7229-byte record.</summary>
    public static byte[] Record(string name, byte[] date, Func<int, int, int>? pixel = null)
    {
        var rec = new byte[RecordSize];
        var nameBytes = Encoding.ASCII.GetBytes(name);
        Array.Fill(rec, (byte)' ', 0, NameLength);
        nameBytes.AsSpan(0, Math.Min(nameBytes.Length, NameLength)).CopyTo(rec);
        date.CopyTo(rec, NameLength);

        pixel ??= DefaultPattern;
        for (var y = 0; y < ImageSize; y++)
        {
            for (var x = 0; x < ImageSize; x += 2)
            {
                var left = pixel(x, y) & 0xF;
                var right = pixel(x + 1, y) & 0xF;
                rec[NameLength + DateLength + (y * ImageSize + x) / 2] = (byte)(left | (right << 4));
            }
        }
        return rec;
    }

    /// <summary>A diagonal gradient so every nibble value and both halves of a byte get used.</summary>
    public static int DefaultPattern(int x, int y) => (x + y * 3) % 16;

    /// <summary>A dump of <paramref name="count"/> distinct records plus optional trailing bytes.</summary>
    public static byte[] Dump(int count, int trailing = 0)
    {
        var dump = new List<byte>();
        for (var i = 0; i < count; i++)
        {
            var seed = i;
            dump.AddRange(Record($"IMG{i + 1}", [24, (byte)(1 + i % 12), (byte)(1 + i), (byte)(i % 24), (byte)(i * 7 % 60)],
                (x, y) => (x * (seed + 1) + y) % 16));
        }
        dump.AddRange(Enumerable.Repeat((byte)0xAA, trailing));
        return dump.ToArray();
    }

    /// <summary>Synthetic images for the fake watch.</summary>
    public static IReadOnlyList<WqvImage> Images(int count) =>
        ImageDecoder.DecodeDump(Dump(count), out _);

    /// <summary>Walks up from the test binary to the repository root (the folder holding WqvLink.sln).</summary>
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WqvLink.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("WqvLink.sln not found above the test binary");
    }
}
