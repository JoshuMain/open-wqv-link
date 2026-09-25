using static WqvLink.Core.Protocol.WqvConstants;

namespace WqvLink.Core.Protocol;

/// <summary>Builds and escapes WQV-1 frames</summary>
public static class Framing
{
    //Builds a complete, escaped frame including BOF and EOF
    public static byte[] Build(byte addr, byte ctrl, ReadOnlySpan<byte> data = default)
    {
        //the checksum covers the unescaped body and is itself escaped
        var body = new byte[data.Length + 4];
        body[0] = addr;
        body[1] = ctrl;
        data.CopyTo(body.AsSpan(2));
        var ck = Protocol.Checksum.Compute(body.AsSpan(0, data.Length + 2));
        body[^2] = (byte)(ck >> 8);
        body[^1] = (byte)ck;

        var escaped = Escape(body);
        var frame = new byte[escaped.Length + 2];
        frame[0] = Bof;
        escaped.CopyTo(frame, 1);
        frame[^1] = Eof;
        return frame;
    }

    //Checksum of an unescaped addr+ctrl+data body
    public static ushort Checksum(ReadOnlySpan<byte> body) => Protocol.Checksum.Compute(body);

    //Replaces each C0, C1 or 7D with 7D, b ^ 0x20
    public static byte[] Escape(ReadOnlySpan<byte> data)
    {
        var output = new List<byte>(data.Length + 8);
        foreach (var b in data)
        {
            if (b is Bof or Eof or Esc)
            {
                output.Add(Esc);
                output.Add((byte)(b ^ EscapeXor));
            }
            else
            {
                output.Add(b);
            }
        }
        return output.ToArray();
    }

    //Reverses Escape. Returns false on a dangling escape at the end
    public static bool TryUnescape(ReadOnlySpan<byte> data, out byte[] result)
    {
        var output = new List<byte>(data.Length);
        for (var i = 0; i < data.Length; i++)
        {
            if (data[i] == Esc)
            {
                if (++i >= data.Length)
                {
                    result = [];
                    return false;
                }
                output.Add((byte)(data[i] ^ EscapeXor));
            }
            else
            {
                output.Add(data[i]);
            }
        }
        result = output.ToArray();
        return true;
    }
}
