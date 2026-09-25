using WqvLink.Core.Protocol;

namespace WqvLink.Core.Tests;

public sealed class FrameParserTests
{
    private static readonly byte[] Payload = [0x05, 0xC0, 0x7D, 0xC1, 0x00];

    [Fact]
    public void Feed_SelftestVectorWithPreamble()
    {
        // Same as the Python selftest(): preamble FF then an escaped data frame.
        var raw = new byte[] { 0xFF }.Concat(Framing.Build(0x02, 0x42, Payload)).ToArray();
        var f = Assert.Single(new FrameParser().Feed(raw));
        Assert.Equal(Payload, f.Data.ToArray());
    }

    [Fact]
    public void Feed_SplitAtEveryBoundary()
    {
        var a = Framing.Build(0x02, 0x42, Payload);
        var b = Framing.Build(0x02, 0x63);
        var stream = a.Concat(b).ToArray();

        for (var cut1 = 0; cut1 <= stream.Length; cut1++)
        {
            for (var cut2 = cut1; cut2 <= stream.Length; cut2++)
            {
                var p = new FrameParser();
                var frames = new List<Frame>();
                frames.AddRange(p.Feed(stream.AsSpan(0, cut1)));
                frames.AddRange(p.Feed(stream.AsSpan(cut1, cut2 - cut1)));
                frames.AddRange(p.Feed(stream.AsSpan(cut2)));
                Assert.Equal(2, frames.Count);
                Assert.Equal(new Frame(0x02, 0x42, Payload), frames[0]);
                Assert.Equal(new Frame(0x02, 0x63, Array.Empty<byte>()), frames[1]);
            }
        }
    }

    [Fact]
    public void Feed_ByteAtATime()
    {
        var p = new FrameParser();
        var frames = Framing.Build(0xFF, 0xA3, [1, 2, 3, 4]).SelectMany(b => p.Feed([b])).ToList();
        Assert.Single(frames);
    }

    [Fact]
    public void Feed_RepeatedBofsAndPreamble()
    {
        var raw = new byte[] { 0x00, 0x12, 0xC0, 0xC0, 0xC0 }.Concat(Framing.Build(0xFF, 0xB3)[1..]).ToArray();
        var f = Assert.Single(new FrameParser().Feed(raw));
        Assert.Equal(0xB3, f.Ctrl);
    }

    [Fact]
    public void Feed_RealTraceFragments()
    {
        // From samples/wqvlink.log, arriving in these exact chunks.
        var p = new FrameParser();
        var frames = new List<Frame>();
        foreach (var chunk in new[] { "c0 02 20 07", "fa", "1c", "3d 1d 01 99", "c1" })
        {
            frames.AddRange(p.Feed(Convert.FromHexString(chunk.Replace(" ", ""))));
        }
        var f = Assert.Single(frames);
        Assert.Equal(0x20, f.Ctrl);
        Assert.Equal(new byte[] { 0x07, 0xFA, 0x1C, 0x3D, 0x1D }, f.Data.ToArray());
    }

    [Fact]
    public void Feed_DanglingEscapeIsDiscarded()
    {
        var p = new FrameParser();
        var reasons = new List<string>();
        p.Discarded += reasons.Add;
        var frames = p.Feed([0xC0, 0x02, 0x63, 0x00, 0x7D, 0xC1]);
        Assert.Empty(frames);
        Assert.Contains(reasons, r => r.Contains("dangling"));
    }

    [Fact]
    public void Feed_BadChecksumDiscardedAndLaterFramesStillParsed()
    {
        var bad = Framing.Build(0x02, 0x42, Payload);
        bad[3] ^= 0x01; // corrupt a data byte that isn't an escape
        var good = Framing.Build(0x02, 0x44, [0x05, 0x01]);

        var p = new FrameParser();
        var reasons = new List<string>();
        p.Discarded += reasons.Add;
        var frames = p.Feed(bad.Concat(good).ToArray());

        var f = Assert.Single(frames);
        Assert.Equal(0x44, f.Ctrl);
        Assert.Contains(reasons, r => r.Contains("checksum"));
    }

    [Fact]
    public void Feed_ShortFrameIsDiscarded()
    {
        var p = new FrameParser();
        var reasons = new List<string>();
        p.Discarded += reasons.Add;
        Assert.Empty(p.Feed([0xC0, 0x02, 0x02, 0xC1]));
        Assert.Contains(reasons, r => r.Contains("short"));
    }

    [Fact]
    public void Feed_EofWithoutBofIsIgnored()
    {
        var p = new FrameParser();
        Assert.Empty(p.Feed([0x01, 0x02, 0xC1]));
        Assert.Single(p.Feed(Framing.Build(0xFF, 0xB3)));
    }

    [Fact]
    public void Feed_NeverThrowsOnRandomNoise()
    {
        var rng = new Random(1234);
        var p = new FrameParser();
        var noise = new byte[20000];
        rng.NextBytes(noise);
        _ = p.Feed(noise);
        Assert.Single(p.Feed(Framing.Build(0xFF, 0xB3)));
    }
}
