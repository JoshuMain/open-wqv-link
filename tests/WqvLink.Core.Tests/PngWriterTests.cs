using System.Buffers.Binary;
using System.Text;
using WqvLink.Core.Imaging;
using WqvLink.Core.Tests.TestData;

namespace WqvLink.Core.Tests;

public sealed class PngWriterTests
{
    private static (WqvImage Img, byte[] Grey) Sample(string name = "MY CAT", byte[]? date = null)
    {
        var img = ImageDecoder.DecodeRecord(FixtureFactory.Record(name, date ?? [24, 12, 30, 13, 50]), 12);
        return (img, ImageDecoder.ToGrey8(img));
    }

    [Fact]
    public void Crc32_KnownVector()
    {
        Assert.Equal(0xCBF43926u, Crc32.Compute("123456789"u8));
        // IEND chunk CRC from any PNG.
        Assert.Equal(0xAE426082u, Crc32.Compute("IEND"u8));
    }

    [Fact]
    public void Encode_SignatureIhdrAndAllCrcsValid()
    {
        var (img, grey) = Sample();
        var png = MiniPngReader.Read(PngWriter.Encode(grey, 120, 120, PngMetadata.For(img)));

        Assert.All(png.Chunks, c => Assert.True(c.CrcOk, $"{c.Type} CRC"));
        Assert.Equal("IHDR", png.Chunks[0].Type);
        Assert.Equal("IEND", png.Chunks[^1].Type);
        Assert.Equal(120, png.Width);
        Assert.Equal(120, png.Height);
        Assert.Equal(8, png.BitDepth);
        Assert.Equal(0, png.ColourType);
        Assert.Equal(new byte[] { 0, 0, 0 }, png.Chunks[0].Data[10..13]);
    }

    [Fact]
    public void Encode_PixelsRoundTrip()
    {
        var (img, grey) = Sample();
        var png = MiniPngReader.Read(PngWriter.Encode(grey, 120, 120, PngMetadata.For(img)));
        Assert.Equal(grey, png.Pixels);
    }

    [Fact]
    public void Encode_TextKeywords()
    {
        var (img, grey) = Sample();
        var png = MiniPngReader.Read(PngWriter.Encode(grey, 120, 120, PngMetadata.For(img)));

        Assert.Equal("MY CAT", png.Text["Title"]);
        Assert.Equal("CASIO WQV-1 wrist camera, image 12", png.Text["Description"]);
        Assert.Equal("CASIO WQV-1", png.Text["Source"]);
        Assert.StartsWith("Open WQV Link ", png.Text["Software"]);
        Assert.Equal("2024-12-30T13:50:00", png.Text["Creation Time"]);
    }

    [Fact]
    public void Encode_UntitledUndatedOmitsOptionalFields()
    {
        var (img, grey) = Sample(name: "", date: [0, 0, 0, 0, 0]);
        var png = MiniPngReader.Read(PngWriter.Encode(grey, 120, 120, PngMetadata.For(img)));

        Assert.False(png.Text.ContainsKey("Title"));
        Assert.False(png.Text.ContainsKey("Creation Time"));
        var tiff = Tiff.Parse(png.Chunks.Single(c => c.Type == "eXIf").Data);
        Assert.Equal([ExifBuilder.TagMake, ExifBuilder.TagModel, ExifBuilder.TagSoftware], tiff.Ifd0.Keys);
        Assert.Null(tiff.Exif);
    }

    [Fact]
    public void Encode_NonLatin1TextBecomesQuestionMark()
    {
        var (_, grey) = Sample();
        var meta = new PngMetadata(1, "café ☃", null, "x");
        var png = MiniPngReader.Read(PngWriter.Encode(grey, 120, 120, meta));
        Assert.Equal("café ?", png.Text["Title"]);
    }

    [Fact]
    public void Encode_ExifBeforeIdatWithCorrectOffsets()
    {
        var (img, grey) = Sample();
        var png = MiniPngReader.Read(PngWriter.Encode(grey, 120, 120, PngMetadata.For(img)));

        var types = png.Chunks.Select(c => c.Type).ToList();
        Assert.True(types.IndexOf("eXIf") < types.IndexOf("IDAT"));

        var tiff = Tiff.Parse(png.Chunks.Single(c => c.Type == "eXIf").Data);
        Assert.Equal(
            [ExifBuilder.TagImageDescription, ExifBuilder.TagMake, ExifBuilder.TagModel, ExifBuilder.TagSoftware, ExifBuilder.TagDateTime, ExifBuilder.TagExifIfd],
            tiff.Ifd0.Keys);
        Assert.Equal("MY CAT", tiff.Ifd0[ExifBuilder.TagImageDescription]);
        Assert.Equal("CASIO", tiff.Ifd0[ExifBuilder.TagMake]);
        Assert.Equal("WQV-1", tiff.Ifd0[ExifBuilder.TagModel]);
        Assert.Equal("2024:12:30 13:50:00", tiff.Ifd0[ExifBuilder.TagDateTime]);

        Assert.NotNull(tiff.Exif);
        Assert.Equal([ExifBuilder.TagDateTimeOriginal, ExifBuilder.TagDateTimeDigitized], tiff.Exif.Keys);
        Assert.Equal("2024:12:30 13:50:00", tiff.Exif[ExifBuilder.TagDateTimeOriginal]);
        Assert.Equal("2024:12:30 13:50:00", tiff.Exif[ExifBuilder.TagDateTimeDigitized]);
    }

    [Fact]
    public void Exif_ShortAsciiValuesAreInline()
    {
        var meta = new PngMetadata(1, "AB", null, "S");
        var tiff = ExifBuilder.Build(meta);
        // IFD0 (undated): ImageDescription, Make, Model, Software.
        Assert.Equal(4, BinaryPrimitives.ReadUInt16BigEndian(tiff.AsSpan(8)));
        var first = tiff.AsSpan(10, 12);
        Assert.Equal(ExifBuilder.TagImageDescription, BinaryPrimitives.ReadUInt16BigEndian(first));
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32BigEndian(first[4..]));
        Assert.Equal("AB\0\0"u8.ToArray(), first[8..12].ToArray());
        Assert.Equal("AB", Tiff.Parse(tiff).Ifd0[ExifBuilder.TagImageDescription]);
    }

    [Fact]
    public void Encode_Scale4IsNearestNeighbour()
    {
        var (img, grey) = Sample();
        var png = MiniPngReader.Read(PngWriter.Encode(grey, 120, 120, PngMetadata.For(img), scale: 4));
        Assert.Equal(480, png.Width);
        Assert.Equal(480, png.Height);
        for (var y = 0; y < 480; y++)
        {
            for (var x = 0; x < 480; x++)
            {
                Assert.Equal(grey[y / 4 * 120 + x / 4], png.Pixels[y * 480 + x]);
            }
        }
    }

    [Fact]
    public void Export_WritesFileDatedToCaptureTime()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wqvtest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (img, _) = Sample();
            var path = ImageExporter.Export(img, dir, new ExportOptions { Sidecars = true });
            Assert.Equal("012_20241230-1350_MY_CAT.png", Path.GetFileName(path));
            Assert.Equal(new DateTime(2024, 12, 30, 13, 50, 0), File.GetLastWriteTime(path));
            Assert.True(File.Exists(Path.ChangeExtension(path, ".json")));
            Assert.Contains("\"stamp\": \"20241230-1350\"", File.ReadAllText(Path.ChangeExtension(path, ".json")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Independent big-endian TIFF reader for the eXIf assertions.</summary>
    private sealed record Tiff(Dictionary<ushort, string> Ifd0, Dictionary<ushort, string>? Exif)
    {
        public static Tiff Parse(byte[] t)
        {
            Assert.Equal(new byte[] { 0x4D, 0x4D, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08 }, t[..8]);
            var ifd0 = ReadIfd(t, 8, out var exifPtr);
            var exif = exifPtr is { } p ? ReadIfd(t, p, out _) : null;
            return new Tiff(ifd0, exif);
        }

        private static Dictionary<ushort, string> ReadIfd(byte[] t, int offset, out int? exifPtr)
        {
            exifPtr = null;
            var result = new Dictionary<ushort, string>();
            var n = BinaryPrimitives.ReadUInt16BigEndian(t.AsSpan(offset));
            ushort last = 0;
            for (var i = 0; i < n; i++)
            {
                var e = t.AsSpan(offset + 2 + i * 12, 12);
                var tag = BinaryPrimitives.ReadUInt16BigEndian(e);
                Assert.True(tag > last, "IFD entries must be sorted by tag");
                last = tag;
                var type = BinaryPrimitives.ReadUInt16BigEndian(e[2..]);
                var count = (int)BinaryPrimitives.ReadUInt32BigEndian(e[4..]);
                if (tag == ExifBuilder.TagExifIfd)
                {
                    Assert.Equal(4, type);
                    exifPtr = (int)BinaryPrimitives.ReadUInt32BigEndian(e[8..]);
                    result[tag] = exifPtr.ToString()!;
                    continue;
                }
                Assert.Equal(2, type);
                var value = count <= 4 ? e.Slice(8, count).ToArray() : t.AsSpan((int)BinaryPrimitives.ReadUInt32BigEndian(e[8..]), count).ToArray();
                Assert.Equal(0, value[^1]);
                result[tag] = Encoding.Latin1.GetString(value, 0, count - 1);
            }
            Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(t.AsSpan(offset + 2 + n * 12)));
            return result;
        }
    }
}
