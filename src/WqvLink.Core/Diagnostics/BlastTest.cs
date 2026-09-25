using WqvLink.Core.Transport;

namespace WqvLink.Core.Diagnostics;

/// <summary>Streams 0x00 bytes so the IR LED can be seen through a phone camera</summary>
public static class BlastTest
{
    public const string Instructions =
        "View the IR window with a phone's front camera. You should see a faint purple flicker.";

    public static async Task<DiagResult> RunAsync(ITransport t, TimeSpan duration, CancellationToken ct)
    {
        var chunk = new byte[512];
        var end = DateTime.UtcNow + duration;
        long sent = 0;
        try
        {
            while (DateTime.UtcNow < end)
            {
                await t.WriteAsync(chunk, ct).ConfigureAwait(false);
                sent += chunk.Length;
            }
        }
        catch (OperationCanceledException)
        {
            return new DiagResult(DiagVerdict.Done, $"stopped after {sent} bytes");
        }
        return new DiagResult(DiagVerdict.Done, $"sent {sent} bytes over {duration.TotalSeconds:0.#} s",
            "Some phone cameras filter IR, so not seeing it isn't conclusive.");
    }
}
