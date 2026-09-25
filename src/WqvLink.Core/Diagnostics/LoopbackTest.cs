using WqvLink.Core.Logging;
using WqvLink.Core.Transport;

namespace WqvLink.Core.Diagnostics;

/// <summary>USB → firmware → UART → back, with GP0 jumpered to GP1</summary>
public static class LoopbackTest
{
    public const string Instructions = "Unplug the Click and jumper GP0 to GP1.";

    public static async Task<DiagResult> RunAsync(ITransport t, CancellationToken ct, ByteTraceLog? log = null)
    {
        var pattern = Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray();
        t.DiscardInput();
        log?.Sent(pattern);
        await t.WriteAsync(pattern, ct).ConfigureAwait(false);
        var got = await t.ReadForAsync(TimeSpan.FromMilliseconds(500), ct).ConfigureAwait(false);
        log?.Received(got);

        if (got.AsSpan().SequenceEqual(pattern))
        {
            return new DiagResult(DiagVerdict.Pass, $"{got.Length} bytes looped back intact - USB, firmware and UART are good");
        }
        var bad = got.Zip(pattern).Count(p => p.First != p.Second);
        return new DiagResult(DiagVerdict.Fail, $"sent {pattern.Length}, got {got.Length}, {bad} mismatched",
            "Check the GP0-GP1 jumper, that the Pico runs wqv_bridge, and the port.");
    }
}
