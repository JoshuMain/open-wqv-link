using WqvLink.Core.Protocol;

namespace WqvLink.Core.Tests;

public sealed class SequenceTests
{
    private static readonly byte[] ExpectedGet =
        [0x31, 0x51, 0x71, 0x91, 0xB1, 0xD1, 0xF1, 0x11, 0x31, 0x51, 0x71, 0x91, 0xB1, 0xD1, 0xF1, 0x11];

    private static readonly byte[] ExpectedRet =
        [0x42, 0x44, 0x46, 0x48, 0x4A, 0x4C, 0x4E, 0x40, 0x42, 0x44, 0x46, 0x48, 0x4A, 0x4C, 0x4E, 0x40];

    [Fact]
    public void GetAndRet_MatchSpecTableForFirstSixteenPackets()
    {
        for (var i = 0; i < 16; i++)
        {
            Assert.Equal(ExpectedGet[i], Sequence.Get(i));
            Assert.Equal(ExpectedRet[i], Sequence.Ret(i));
        }
    }

    [Theory]
    [InlineData(2, 0x54)]
    [InlineData(0, 0x14)]
    [InlineData(1, 0x34)]
    [InlineData(7, 0xF4)]
    public void CloseCtrl_PutsNrInTopBits(int nr, byte expected)
    {
        Assert.Equal(expected, Sequence.CloseCtrl(nr));
    }

    [Fact]
    public void CloseNr_For29Images()
    {
        // 29 × 7229 bytes / 128 per packet = 1638 packets (rounded up).
        var packets = (29 * 7229 + 127) / 128;
        Assert.Equal((packets + 1) & 7, Sequence.CloseNr(packets));
    }
}
