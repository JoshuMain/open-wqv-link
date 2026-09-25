using System.Text;
using static WqvLink.Core.Protocol.WqvConstants;

namespace WqvLink.Core.Imaging;

//splits a raw dump into 7229-byte records and renders them to 8-bit grey
public static class ImageDecoder
{
    //decodes every whole record
    public static IReadOnlyList<WqvImage> DecodeDump(ReadOnlySpan<byte> raw, out int trailingBytes)
    {
        var count = raw.Length / RecordSize;
        trailingBytes = raw.Length % RecordSize;
        var images = new List<WqvImage>(count);
        for (var k = 0; k < count; k++)
        {
            images.Add(DecodeRecord(raw.Slice(k * RecordSize, RecordSize), k + 1));
        }
        return images;
    }

    //decodes a single record
    public static WqvImage DecodeRecord(ReadOnlySpan<byte> record, int index)
    {
        if (record.Length < RecordSize)
        {
            throw new ArgumentException($"record must be {RecordSize} bytes, got {record.Length}", nameof(record));
        }

        var name = DecodeName(record[..NameLength]);
        var date = record.Slice(NameLength, DateLength).ToArray();
        var pixels = record.Slice(NameLength + DateLength, PixelBytes).ToArray();
        return new WqvImage(index, name, DecodeDate(date), date, pixels);
    }

    //ASCII name with spaces and NULs trimmed from both ends. Non-ASCII becomes '?'
    public static string DecodeName(ReadOnlySpan<byte> field)
    {
        var sb = new StringBuilder(field.Length);
        foreach (var b in field)
        {
            sb.Append(b < 0x80 ? (char)b : '?');
        }
        return sb.ToString().Trim(' ', '\0');
    }

    /// <summary>
    /// Date bytes are year−2000, month (1–12), day, HOUR, MINUTE 
    /// VERIFIED on a real watch - Grober's page has hour and minute swapped,it returns null for an impossible date
    /// </summary>
    public static DateTime? DecodeDate(ReadOnlySpan<byte> d)
    {
        if (d.Length < DateLength)
        {
            return null;
        }
        int year = 2000 + d[0], month = d[1], day = d[2], hour = d[3], minute = d[4];
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month) || hour > 23 || minute > 59)
        {
            return null;
        }
        return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Local);
    }

    /// <summary>
    /// Renders to 14400 row-major grey bytes. The LOW nibble is the left pixel, and
    /// 0 is white and 15 black, so <c>grey = 255 − 17·v</c>.
    /// </summary>
    public static byte[] ToGrey8(WqvImage img, DecodeOptions? opts = null)
    {
        opts ??= DecodeOptions.Default;
        var grey = new byte[ImageSize * ImageSize];
        var px = img.Pixels4bpp;
        for (var j = 0; j < px.Length && 2 * j + 1 < grey.Length; j++)
        {
            int left = px[j] & 0x0F, right = px[j] >> 4;
            if (opts.SwapNibbles)
            {
                (left, right) = (right, left);
            }
            grey[2 * j] = Shade(left, opts.Invert);
            grey[2 * j + 1] = Shade(right, opts.Invert);
        }
        return grey;
    }

    private static byte Shade(int v, bool invert) => (byte)(invert ? 17 * v : 255 - 17 * v);
}
