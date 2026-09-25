using System.Buffers.Binary;
using WqvLink.Core.Firmware;
using WqvLink.Core.Imaging;
using WqvLink.Core.Tests.TestData;

namespace WqvLink.Core.Tests;

public sealed class ExportAndFirmwareTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wqvtest-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static WqvImage Img(string name = "MY CAT", byte[]? date = null, int index = 3) =>
        ImageDecoder.DecodeRecord(FixtureFactory.Record(name, date ?? [24, 12, 30, 13, 50]), index);

    [Theory]
    [InlineData("{index}_{stamp}_{name}", "003_20241230-1350_MY_CAT")]
    [InlineData("{date}_{time}_{index}", "2024-12-30_1350_003")]
    [InlineData("WQV_{index}", "WQV_003")]
    [InlineData("a/b:c*{name}", "a_b_c_MY_CAT")]
    [InlineData("", "003_20241230-1350_MY_CAT")]
    public void FileNamer_RendersTokens(string template, string expected)
    {
        Assert.Equal(expected, FileNamer.Render(template, Img()));
    }

    [Fact]
    public void FileNamer_UndatedImage()
    {
        Assert.Equal("undated_0000_untitled", FileNamer.Render("{date}_{time}_{name}", Img("", [0, 0, 0, 0, 0])));
    }

    [Fact]
    public void Bmp_HeaderAndPixelsBottomUp()
    {
        var img = Img();
        var grey = ImageDecoder.ToGrey8(img);
        var bmp = BmpWriter.Encode(grey, 120, 120);

        Assert.Equal((byte)'B', bmp[0]);
        Assert.Equal((byte)'M', bmp[1]);
        Assert.Equal(bmp.Length, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(2)));
        var offset = BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(10));
        Assert.Equal(120, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(18)));
        Assert.Equal(120, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(22)));
        Assert.Equal(8, BinaryPrimitives.ReadInt16LittleEndian(bmp.AsSpan(28)));
        // Row 0 of the image is the last row in the file (stride 120 is already a multiple of 4).
        Assert.Equal(grey.AsSpan(0, 120).ToArray(), bmp.AsSpan(offset + 119 * 120, 120).ToArray());
        Assert.Equal(grey.AsSpan(119 * 120, 120).ToArray(), bmp.AsSpan(offset, 120).ToArray());
    }

    [Fact]
    public void Bmp_ScaledRowsArePadded()
    {
        var bmp = BmpWriter.Encode(new byte[3 * 1], 3, 1, scale: 1);
        var offset = BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(10));
        Assert.Equal(offset + 4, bmp.Length); // 3 pixels padded to 4 bytes
    }

    [Fact]
    public void Export_HonoursFormatTemplateAndDateOption()
    {
        var img = Img();
        var bmpPath = ImageExporter.Export(img, _dir, new ExportOptions { Format = ImageFormat.Bmp, FileNameTemplate = "WQV_{index}" });
        Assert.Equal("WQV_003.bmp", Path.GetFileName(bmpPath));
        Assert.Equal(new DateTime(2024, 12, 30, 13, 50, 0), File.GetLastWriteTime(bmpPath));

        var pngPath = ImageExporter.Export(img, _dir, new ExportOptions { SetFileDates = false, EmbedMetadata = false });
        Assert.NotEqual(new DateTime(2024, 12, 30, 13, 50, 0), File.GetLastWriteTime(pngPath));
        var png = MiniPngReader.Read(File.ReadAllBytes(pngPath));
        Assert.DoesNotContain(png.Chunks, c => c.Type is "tEXt" or "eXIf");
    }

    [Fact]
    public void ExportAll_SuffixesClashesWithinBatchButOverwritesOnReexport()
    {
        var images = new[] { Img(index: 1), Img(index: 2) };
        var opts = new ExportOptions { FileNameTemplate = "{name}" };
        var first = ImageExporter.ExportAll(images, _dir, opts);
        Assert.Equal(["MY_CAT.png", "MY_CAT-2.png"], first.Select(Path.GetFileName));

        ImageExporter.ExportAll(images, _dir, opts);
        Assert.Equal(2, Directory.GetFiles(_dir).Length);
    }

    private static byte[] Uf2Block()
    {
        var b = new byte[512];
        BinaryPrimitives.WriteUInt32LittleEndian(b, Uf2.MagicStart0);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), Uf2.MagicStart1);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(508), Uf2.MagicEnd);
        return b;
    }

    [Fact]
    public void Uf2_GoodFilePasses()
    {
        Assert.Null(Uf2.Validate([.. Uf2Block(), .. Uf2Block()]));
    }

    [Fact]
    public void Uf2_BadSizeOrMagicFails()
    {
        Assert.NotNull(Uf2.Validate(Uf2Block().AsSpan(0, 500)));
        Assert.NotNull(Uf2.Validate([]));
        var bad = Uf2Block();
        bad[509] ^= 1;
        Assert.Contains("block 1", Uf2.Validate([.. Uf2Block(), .. bad]));
    }

    [Fact]
    public void Uf2_BundledFirmwareIsValid()
    {
        var dist = Path.Combine(FixtureFactory.RepoRoot(), "firmware", "dist");
        var files = Directory.GetFiles(dist, "*.uf2");
        Assert.NotEmpty(files);
        Assert.All(files, f => Assert.Null(Uf2.Validate(File.ReadAllBytes(f))));
    }

    [Fact]
    public async Task CopyAsync_WritesFirmwareUf2()
    {
        Directory.CreateDirectory(_dir);
        byte[] data = [.. Uf2Block(), .. Uf2Block()];
        var reports = new List<double>();
        await PicoFlasher.CopyAsync(data, _dir, new SyncProgress<double>(reports.Add), CancellationToken.None);
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(_dir, "firmware.uf2")));
        Assert.Equal(100.0, reports[^1]);
    }
}
