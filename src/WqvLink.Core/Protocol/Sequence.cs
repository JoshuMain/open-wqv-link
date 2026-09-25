namespace WqvLink.Core.Protocol;

/// <summary>
/// Data-loop control bytes- These are real IrLAP sequence numbers
/// <list type="bullet">
/// <item>We send RR (receiver ready) supervisory frames: <c>Nr</c> in bits 7–5, P/F in bit 4,
/// and <c>0001</c> in bits 3–0. <c>Nr</c> is the number of the next I-frame we expect.</item>
/// <item>The watch replies with I-frames: <c>Nr</c> in bits 7–5 (here always 2, so 0x40),
/// P/F in bit 4, <c>Ns</c> in bits 3–1 and 0 in bit 0.</item>
/// </list>
/// Packet <c>i</c> is acknowledged with Nr = (i+1) mod 8 and arrives with Ns = (i+1) mod 8.
/// AI summary of doc
/// </summary>
public static class Sequence
{
    /// <summary>RR poll for packet <paramref name="i"/>: 31, 51, 71, 91, B1, D1, F1, 11, …</summary>
    public static byte Get(int i) => (byte)((((i + 1) & 7) << 5) | 0x11);

    /// <summary>Expected I-frame reply for packet <paramref name="i"/>: 42, 44, …, 4E, 40, …</summary>
    public static byte Ret(int i) => (byte)(0x40 | (((i + 1) & 7) << 1));

    /// <summary>Nr to use for the close frame after <paramref name="packets"/> packets.</summary>
    public static int CloseNr(int packets) => (packets + 1) & 7;

    /// <summary>
    /// Close I-frame: Nr in bits 7–5, P bit set, Ns = 2 (bits 3–1). Nr = 2 gives 0x54,
    /// the value in Gröber's capture.
    /// </summary>
    public static byte CloseCtrl(int nr) => (byte)(((nr & 7) << 5) | 0x14);
}
