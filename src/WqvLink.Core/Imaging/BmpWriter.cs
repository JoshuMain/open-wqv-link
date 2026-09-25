using System.Buffers.Binary;

namespace WqvLink.Core.Imaging;

/// <summary>8-bit greyscale BMP</summary>
public static class BmpWriter
{
    public static byte[] Encode(byte[] grey8, int width, int height, int scale = 1)
    {
        var (pixels, w, h) = PngWriter.Upscale(grey8, width, height, scale);
        var stride = (w + 3) & ~3;
        const int headerSize = 14 + 40 + 256 * 4;
        var bmp = new byte[headerSize + stride * h];
        var s = bmp.AsSpan();

        // BITMAPFILEHEADER
        s[0] = (byte)'B';
        s[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(s[2..], bmp.Length);
        BinaryPrimitives.WriteInt32LittleEndian(s[10..], headerSize);

        // BITMAPINFOHEADER
        BinaryPrimitives.WriteInt32LittleEndian(s[14..], 40);
        BinaryPrimitives.WriteInt32LittleEndian(s[18..], w);
        BinaryPrimitives.WriteInt32LittleEndian(s[22..], h); // positive = bottom-up
        BinaryPrimitives.WriteInt16LittleEndian(s[26..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(s[28..], 8);
        BinaryPrimitives.WriteInt32LittleEndian(s[34..], stride * h);
        BinaryPrimitives.WriteInt32LittleEndian(s[38..], 2835); // 72 dpi
        BinaryPrimitives.WriteInt32LittleEndian(s[42..], 2835);
        BinaryPrimitives.WriteInt32LittleEndian(s[46..], 256);

        for (var i = 0; i < 256; i++)
        {
            var p = 54 + i * 4;
            bmp[p] = bmp[p + 1] = bmp[p + 2] = (byte)i;
        }
        for (var y = 0; y < h; y++)
        {
            pixels.AsSpan(y * w, w).CopyTo(s[(headerSize + (h - 1 - y) * stride)..]);
        }
        return bmp;
    }
}
