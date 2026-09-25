using WqvLink.Core.Protocol;

namespace WqvLink.Core.Diagnostics;

public static class SelfTest
{
    public static bool Run()
    {
        if (!Framing.Build(0xFF, 0xB3).AsSpan().SequenceEqual(new byte[] { 0xC0, 0xFF, 0xB3, 0x01, 0xB2, 0xC1 }))
        {
            return false;
        }
        byte[] payload = [0x05, 0xC0, 0x7D, 0xC1, 0x00];
        var frames = new FrameParser().Feed([0xFF, .. Framing.Build(0x02, 0x42, payload)]);
        return frames.Count == 1 && frames[0].Data.Span.SequenceEqual(payload);
    }
}
