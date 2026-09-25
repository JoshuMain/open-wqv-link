namespace WqvLink.Core.Protocol;

/// <summary>
/// Every protocol constant in one place
/// </summary>
public static class WqvConstants
{
    /// <summary>Beginning of frame.</summary>
    public const byte Bof = 0xC0;
    /// <summary>End of frame.</summary>
    public const byte Eof = 0xC1;
    /// <summary>Control escape</summary>
    public const byte Esc = 0x7D;
    /// <summary>XOR applied to an escaped byte.</summary>
    public const byte EscapeXor = 0x20;
    /// <summary>Minimum unescaped frame body</summary>
    public const int MinFrameBody = 4;

    //hardware
    /// <summary>IR side is fixed at 115200 8N1 by the Click's 1.8432 MHz oscillator</summary>
    public const int BaudRate = 115200;
    /// <summary>Raspberry Pi USB vendor ID, used to auto-detect the Pico</summary>
    public const string PicoUsbVendorId = "2E8A";

    //connection setup
    /// <summary>Broadcast address used before the watch is assigned one.</summary>
    public const byte BroadcastAddr = 0xFF;
    /// <summary>Hello (IrLAP XID-like) sent until the watch answers.</summary>
    public const byte CtrlHello = 0xB3;
    /// <summary>Watch's answer to hello, carrying 4 clock bytes.</summary>
    public const byte CtrlHelloReply = 0xA3;
    /// <summary>Connect request: clock bytes + assigned address.</summary>
    public const byte CtrlConnect = 0x93;
    /// <summary>Watch's connect acknowledgement (also the disconnect acknowledgement.</summary>
    public const byte CtrlUa = 0x63;
    /// <summary>Receiver-ready poll (RR, Nr = 0).</summary>
    public const byte CtrlReady = 0x11;
    /// <summary>Watch's reply.</summary>
    public const byte CtrlReadyReply = 0x01;
    /// <summary>Address offered to the watch (VERIFIED works).</summary>
    public const byte DefaultAssignedAddr = 0x02;
    /// <summary>Number of watch clock bytes in the hello reply (hh mm ss 1/256 s).</summary>
    public const int ClockBytes = 4;

    //download all images
    /// <summary>Request "all images" (sent with data 01).</summary>
    public const byte CtrlRequestAll = 0x10;
    /// <summary>Data byte for <see cref="CtrlRequestAll"/>.</summary>
    public const byte RequestAllData = 0x01;
    /// <summary>Watch acknowledges the request.</summary>
    public const byte CtrlRequestAllReply = 0x21;
    /// <summary>Watch's header reply to the next 11 poll</summary>
    public const byte CtrlHeaderReply = 0x20;
    /// <summary>Minimum length of the header reply data.</summary>
    public const int HeaderLength = 5;
    /// <summary>Start data transfer</summary>
    public const byte CtrlStartData = 0x32;
    /// <summary>Data byte / and the close frame.</summary>
    public const byte StartDataData = 0x06;
    /// <summary>Watch acknowledges start of data.</summary>
    public const byte CtrlStartDataReply = 0x41;
    /// <summary>Every data packet begins with this byte, which is stripped.</summary>
    public const byte DataPacketPrefix = 0x05;
    /// <summary>Image data bytes per packet (VERIFIED).</summary>
    public const int PacketPayload = 128;
    /// <summary>Close fallback control byte, used if the computed one fails.</summary>
    public const byte CtrlCloseFallback = 0x54;
    /// <summary>Disconnect (IrLAP DISC).</summary>
    public const byte CtrlDisconnect = 0x53;

    //image record
    /// <summary>Bytes per image record (0x1C3D).</summary>
    public const int RecordSize = 0x1C3D;
    /// <summary>Name field length (ASCII).</summary>
    public const int NameLength = 24;
    /// <summary>Date field length: year-2000, month, day, hour, minute.</summary>
    public const int DateLength = 5;
    /// <summary>Image width and height in pixels.</summary>
    public const int ImageSize = 120;
    /// <summary>Packed 4bpp pixel bytes (two pixels per byte).</summary>
    public const int PixelBytes = ImageSize * ImageSize / 2;

    //timeouts and retries
    /// <summary>Hello retry interval.</summary>
    public static readonly TimeSpan HelloTimeout = TimeSpan.FromMilliseconds(200);
    /// <summary>Hello tries (150 × 200 ms = 30 s).</summary>
    public const int HelloTries = 150;
    /// <summary>Control exchange timeout.</summary>
    public static readonly TimeSpan ControlTimeout = TimeSpan.FromMilliseconds(500);
    /// <summary>Control exchange tries.</summary>
    public const int ControlTries = 5;
    /// <summary>Data <c>get</c> timeout.</summary>
    public static readonly TimeSpan DataTimeout = TimeSpan.FromMilliseconds(1000);
    /// <summary>Data <c>get</c> tries.</summary>
    public const int DataTries = 8;
    /// <summary>Tries for each disconnect step.</summary>
    public const int DisconnectTries = 3;
}
