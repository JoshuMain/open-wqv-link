namespace WqvLink.Core.Protocol;

/// <summary>WQV-1 frame checksum - This is NOT the IrLAP CRC</summary>
public static class Checksum
{
    //Sum of the addr, ctrl and data bytes, truncated to 16 bits
    public static ushort Compute(ReadOnlySpan<byte> body)
    {
        uint sum = 0;
        foreach (var b in body)
        {
            sum += b;
        }
        return (ushort)sum;
    }
}
