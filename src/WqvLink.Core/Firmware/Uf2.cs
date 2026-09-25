using System.Buffers.Binary;

namespace WqvLink.Core.Firmware;

/// <summary>UF2 image checks before flashing</summary>
public static class Uf2
{
    public const int BlockSize = 512;
    public const uint MagicStart0 = 0x0A324655;
    public const uint MagicStart1 = 0x9E5D5157;
    public const uint MagicEnd = 0x0AB16F30;

    //UF2 family ID for the RP2040 (original Pico)
    public const uint FamilyRp2040 = 0xE48BFF56;

    private const uint FlagFamilyIdPresent = 0x00002000;

    //The family id of the first block if it declares one
    public static uint? FamilyId(ReadOnlySpan<byte> data)
    {
        if (data.Length < BlockSize)
        {
            return null;
        }
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        return (flags & FlagFamilyIdPresent) != 0 ? BinaryPrimitives.ReadUInt32LittleEndian(data[28..]) : null;
    }

    //Returns null if data is a well-formed UF2, otherwise the reason it isn't  
    public static string? Validate(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0 || data.Length % BlockSize != 0)
        {
            return $"size {data.Length} is not a multiple of {BlockSize}";
        }
        for (var off = 0; off < data.Length; off += BlockSize)
        {
            var block = data.Slice(off, BlockSize);
            if (BinaryPrimitives.ReadUInt32LittleEndian(block) != MagicStart0
                || BinaryPrimitives.ReadUInt32LittleEndian(block[4..]) != MagicStart1
                || BinaryPrimitives.ReadUInt32LittleEndian(block[508..]) != MagicEnd)
            {
                return $"bad magic number in block {off / BlockSize}";
            }
        }
        return null;
    }
}
