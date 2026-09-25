using WqvLink.Core.Imaging;
using WqvLink.Core.Tests.TestData;
using static WqvLink.Core.Protocol.WqvConstants;

namespace WqvLink.Core.Tests;

public sealed class ImageDecoderTests
{
    [Fact]
    public void ToGrey8_LowNibbleIsLeftPixel()
    {
        var rec = FixtureFactory.Record("", [0, 1, 1, 0, 0], (x, _) => x % 2 == 0 ? 0x0 : 0xF);
        // Byte 0 must be 0xF0: left (low) = 0 = white, right (high) = 15 = black.
        Assert.Equal(0xF0, rec[NameLength + DateLength]);

        var img = ImageDecoder.DecodeRecord(rec, 1);
        var grey = ImageDecoder.ToGrey8(img);
        Assert.Equal(255, grey[0]);
        Assert.Equal(0, grey[1]);
    }

    [Fact]
    public void ToGrey8_ValueMappingAndOptions()
    {
        var rec = FixtureFactory.Record("", [0, 1, 1, 0, 0], (x, _) => x % 2 == 0 ? 3 : 10);
        var img = ImageDecoder.DecodeRecord(rec, 1);

        var normal = ImageDecoder.ToGrey8(img);
        Assert.Equal(255 - 17 * 3, normal[0]);
        Assert.Equal(255 - 17 * 10, normal[1]);

        var swapped = ImageDecoder.ToGrey8(img, new DecodeOptions(SwapNibbles: true));
        Assert.Equal(255 - 17 * 10, swapped[0]);
        Assert.Equal(255 - 17 * 3, swapped[1]);

        var inverted = ImageDecoder.ToGrey8(img, new DecodeOptions(Invert: true));
        Assert.Equal(17 * 3, inverted[0]);
    }

    [Fact]
    public void ToGrey8_IsRowMajor14400()
    {
        var rec = FixtureFactory.Record("", [0, 1, 1, 0, 0], (x, y) => y == 1 && x == 0 ? 15 : 0);
        var grey = ImageDecoder.ToGrey8(ImageDecoder.DecodeRecord(rec, 1));
        Assert.Equal(14400, grey.Length);
        Assert.Equal(0, grey[120]);
        Assert.Equal(255, grey[0]);
    }

    [Fact]
    public void DecodeDate_OrderIsYearMonthDayHourMinute()
    {
        var img = ImageDecoder.DecodeRecord(FixtureFactory.Record("", [24, 12, 30, 13, 50]), 1);
        Assert.Equal(new DateTime(2024, 12, 30, 13, 50, 0), img.Taken);
        Assert.Equal("20241230-1350", img.Stamp);
        Assert.Equal(new byte[] { 24, 12, 30, 13, 50 }, img.DateBytes);
    }

    [Fact]
    public void DecodeDate_ResetWatchDateIsKept()
    {
        var img = ImageDecoder.DecodeRecord(FixtureFactory.Record("", [0, 1, 1, 0, 0]), 1);
        Assert.Equal(new DateTime(2000, 1, 1, 0, 0, 0), img.Taken);
    }

    [Theory]
    [InlineData(0, 0, 1, 0, 0)]     // month 0
    [InlineData(0, 13, 1, 0, 0)]    // month 13
    [InlineData(1, 2, 29, 0, 0)]    // 2001 is not a leap year
    [InlineData(0, 1, 1, 24, 0)]    // hour 24
    [InlineData(0, 1, 1, 0, 60)]    // minute 60
    [InlineData(0xFF, 0xFF, 0xFF, 0xFF, 0xFF)]
    public void DecodeDate_InvalidGivesNullAndRawStamp(byte y, byte mo, byte d, byte h, byte mi)
    {
        var img = ImageDecoder.DecodeRecord(FixtureFactory.Record("", [y, mo, d, h, mi]), 1);
        Assert.Null(img.Taken);
        Assert.Equal($"raw{y:x2}{mo:x2}{d:x2}{h:x2}{mi:x2}", img.Stamp);
    }

    [Fact]
    public void DecodeName_NulPaddedIsEmpty()
    {
        var rec = FixtureFactory.Record("", [0, 1, 1, 0, 0]);
        Array.Fill(rec, (byte)0, 0, NameLength);
        var img = ImageDecoder.DecodeRecord(rec, 1);
        Assert.Equal("", img.Name);
        Assert.Equal("untitled", img.SafeName);
    }

    [Fact]
    public void DecodeName_TrimsSpacesAndNulsMixed()
    {
        var rec = FixtureFactory.Record("  MY CAT", [0, 1, 1, 0, 0]);
        rec[10] = 0;
        rec[23] = 0;
        var img = ImageDecoder.DecodeRecord(rec, 7);
        Assert.Equal("MY CAT", img.Name);
        Assert.Equal("MY_CAT", img.SafeName);
        Assert.Equal("007_20000101-0000_MY_CAT", img.BaseFileName);
    }

    [Fact]
    public void DecodeDump_ReportsTrailingBytes()
    {
        var images = ImageDecoder.DecodeDump(FixtureFactory.Dump(3, trailing: 17), out var trailing);
        Assert.Equal(3, images.Count);
        Assert.Equal(17, trailing);
        Assert.Equal([1, 2, 3], images.Select(i => i.Index));
        Assert.Equal("IMG2", images[1].Name);
    }

    [Fact]
    public void DecodeDump_EmptyInput()
    {
        Assert.Empty(ImageDecoder.DecodeDump([], out var trailing));
        Assert.Equal(0, trailing);
    }
}
