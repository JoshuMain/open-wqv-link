using WqvLink.Core.Protocol;

namespace WqvLink.Core.Tests;

public sealed class FramingTests
{
    [Fact]
    public void Build_HelloMatchesGroeberVector()
    {
        Assert.Equal(new byte[] { 0xC0, 0xFF, 0xB3, 0x01, 0xB2, 0xC1 }, Framing.Build(0xFF, 0xB3));
    }

    [Fact]
    public void Build_ConnectMatchesRealTrace()
    {
        // From samples/wqvlink.log: "> c0 ff 93 0c 24 3b a8 02 02 a7 c1"
        var frame = Framing.Build(0xFF, 0x93, [0x0C, 0x24, 0x3B, 0xA8, 0x02]);
        Assert.Equal("c0 ff 93 0c 24 3b a8 02 02 a7 c1", Hex.Format(frame));
    }

    [Fact]
    public void Checksum_IsSixteenBitSum()
    {
        Assert.Equal(0x01B2, Checksum.Compute([0xFF, 0xB3]));
        Assert.Equal(0xFE01, Checksum.Compute(Enumerable.Repeat((byte)0xFF, 255).ToArray()));
        Assert.Equal(0x0000, Checksum.Compute(Enumerable.Repeat((byte)0xFF, 257).Append((byte)0x01).ToArray()));
    }

    [Fact]
    public void Escape_RoundTripsEveryByteValue()
    {
        for (var v = 0; v < 256; v++)
        {
            var b = (byte)v;
            var escaped = Framing.Escape([b]);
            if (b is 0xC0 or 0xC1 or 0x7D)
            {
                Assert.Equal(new byte[] { 0x7D, (byte)(b ^ 0x20) }, escaped);
            }
            else
            {
                Assert.Equal(new[] { b }, escaped);
            }
            Assert.True(Framing.TryUnescape(escaped, out var back));
            Assert.Equal(new[] { b }, back);
        }
    }

    [Fact]
    public void BuildThenParse_RoundTripsEveryByteValueInData()
    {
        var data = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        var frame = Framing.Build(0x02, 0x42, data);
        Assert.DoesNotContain((byte)0xC0, frame[1..^1]);
        Assert.DoesNotContain((byte)0xC1, frame[1..^1]);

        var parsed = Assert.Single(new FrameParser().Feed(frame));
        Assert.Equal(new Frame(0x02, 0x42, data), parsed);
    }

    [Fact]
    public void Unescape_DanglingEscapeFails()
    {
        Assert.False(Framing.TryUnescape([0x01, 0x7D], out _));
    }

    [Fact]
    public void Frame_EqualityComparesDataContent()
    {
        var a = new Frame(1, 2, new byte[] { 3, 4 });
        var b = new Frame(1, 2, new byte[] { 3, 4 });
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, new Frame(1, 2, new byte[] { 3, 5 }));
    }
}
