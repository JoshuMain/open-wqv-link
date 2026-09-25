using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace WqvLink.App.Services;

/// <summary>Turns decoded grey pixels into Avalonia bitmaps. The UI never re-decodes</summary>
public static class BitmapFactory
{
    public static WriteableBitmap FromGrey8(byte[] grey, int width, int height)
    {
        var bmp = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var fb = bmp.Lock();
        var row = new byte[width * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var g = grey[y * width + x];
                row[x * 4] = g;
                row[x * 4 + 1] = g;
                row[x * 4 + 2] = g;
                row[x * 4 + 3] = 255;
            }
            Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, row.Length);
        }
        return bmp;
    }
}

/// <summary>Firmware bundled into the app as embedded resources.</summary>
public static class FirmwareCatalog
{
    public const string BridgeResource = "firmware/wqv_bridge-rp2040.uf2";

    /// <summary>The WQV bridge firmware for the Pico (RP2040), or null if it wasn't bundled.</summary>
    public static byte[]? LoadBridge()
    {
        using var stream = typeof(FirmwareCatalog).Assembly.GetManifestResourceStream(BridgeResource);
        if (stream is null)
        {
            return null;
        }
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
