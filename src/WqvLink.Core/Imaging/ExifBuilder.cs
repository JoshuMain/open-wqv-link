using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace WqvLink.Core.Imaging;

/// <summary>
/// Builds the big-endian TIFF structure for a PNG eXIf chunk
/// Offsets are measured from byte 0 of the returned array (the TIFF header)
/// </summary>
public static class ExifBuilder
{
    public const ushort TagImageDescription = 0x010E;
    public const ushort TagMake = 0x010F;
    public const ushort TagModel = 0x0110;
    public const ushort TagSoftware = 0x0131;
    public const ushort TagDateTime = 0x0132;
    public const ushort TagExifIfd = 0x8769;
    public const ushort TagDateTimeOriginal = 0x9003;
    public const ushort TagDateTimeDigitized = 0x9004;

    private const ushort TypeAscii = 2;
    private const ushort TypeLong = 4;
    private const int HeaderSize = 8;
    private const int EntrySize = 12;

    private sealed record Entry(ushort Tag, ushort Type, uint Count, byte[] Value);

    public static byte[] Build(PngMetadata meta)
    {
        var date = meta.Taken?.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture);

        var ifd0 = new List<Entry>();
        if (meta.Name.Length > 0)
        {
            ifd0.Add(Ascii(TagImageDescription, meta.Name));
        }
        ifd0.Add(Ascii(TagMake, PngMetadata.Make));
        ifd0.Add(Ascii(TagModel, PngMetadata.Model));
        ifd0.Add(Ascii(TagSoftware, meta.Software));

        List<Entry>? exif = null;
        if (date is not null)
        {
            ifd0.Add(Ascii(TagDateTime, date));
            ifd0.Add(new Entry(TagExifIfd, TypeLong, 1, new byte[4])); // offset patched below
            exif = [Ascii(TagDateTimeOriginal, date), Ascii(TagDateTimeDigitized, date)];
        }

        // Layout: header | IFD0 | Exif IFD | data area.
        var ifd0Offset = HeaderSize;
        var exifOffset = ifd0Offset + IfdSize(ifd0.Count);
        var dataOffset = exifOffset + (exif is null ? 0 : IfdSize(exif.Count));
        if (exif is not null)
        {
            BinaryPrimitives.WriteUInt32BigEndian(ifd0[^1].Value, (uint)exifOffset);
        }

        var data = new List<byte>();
        var ifd0Bytes = WriteIfd(ifd0, dataOffset, data);
        var exifBytes = exif is null ? [] : WriteIfd(exif, dataOffset, data);

        var output = new List<byte>(dataOffset + data.Count);
        output.AddRange([0x4D, 0x4D, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08]);
        output.AddRange(ifd0Bytes);
        output.AddRange(exifBytes);
        output.AddRange(data);
        return output.ToArray();
    }

    private static int IfdSize(int entries) => 2 + entries * EntrySize + 4;

    //writes one IFD. Values over 4 bytes are appended to the data
    private static byte[] WriteIfd(List<Entry> entries, int dataOffset, List<byte> data)
    {
        entries.Sort((a, b) => a.Tag.CompareTo(b.Tag));
        var ifd = new byte[IfdSize(entries.Count)];
        BinaryPrimitives.WriteUInt16BigEndian(ifd, (ushort)entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var slot = ifd.AsSpan(2 + i * EntrySize, EntrySize);
            BinaryPrimitives.WriteUInt16BigEndian(slot, e.Tag);
            BinaryPrimitives.WriteUInt16BigEndian(slot[2..], e.Type);
            BinaryPrimitives.WriteUInt32BigEndian(slot[4..], e.Count);
            if (e.Value.Length <= 4)
            {
                e.Value.CopyTo(slot[8..]); // inline, left-aligned
            }
            else
            {
                BinaryPrimitives.WriteUInt32BigEndian(slot[8..], (uint)(dataOffset + data.Count));
                data.AddRange(e.Value);
                if (data.Count % 2 == 1)
                {
                    data.Add(0); // TIFF values start on word boundaries
                }
            }
        }
        //Next-IFD offset stays 0
        return ifd;
    }

    private static Entry Ascii(ushort tag, string value)
    {
        var bytes = Latin1.Encode(value + "\0");
        return new Entry(tag, TypeAscii, (uint)bytes.Length, bytes);
    }
}

//Latin-1 encoding that replaces anything outside it with '?'
internal static class Latin1
{
    private static readonly Encoding Encoding =
        Encoding.GetEncoding("ISO-8859-1", new EncoderReplacementFallback("?"), DecoderFallback.ReplacementFallback);

    public static byte[] Encode(string s) => Encoding.GetBytes(s);

    public static string Decode(ReadOnlySpan<byte> b) => Encoding.GetString(b);
}
