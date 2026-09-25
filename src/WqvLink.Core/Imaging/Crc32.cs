namespace WqvLink.Core.Imaging;

/// <summary>CRC-32 (IEEE, reflected polynomial 0xEDB88320) as used by PNG chunks</summary>
public static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    //CRC of data with the standard init and final XOR
    public static uint Compute(ReadOnlySpan<byte> data) => Finish(Update(Start, data));

    //Initial register value
    public const uint Start = 0xFFFFFFFF;

    //Feeds more bytes into a running register
    public static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        return crc;
    }

    //Applies the final XOR
    public static uint Finish(uint crc) => crc ^ 0xFFFFFFFF;

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }
            table[n] = c;
        }
        return table;
    }
}
