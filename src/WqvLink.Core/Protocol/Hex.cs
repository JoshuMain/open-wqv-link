namespace WqvLink.Core.Protocol;

/// <summary>Lower-case, space-separated hex</summary>
public static class Hex
{
    public static string Format(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return "";
        }
        var chars = new char[data.Length * 3 - 1];
        for (var i = 0; i < data.Length; i++)
        {
            var b = data[i];
            chars[i * 3] = Digit(b >> 4);
            chars[i * 3 + 1] = Digit(b & 0xF);
            if (i < data.Length - 1)
            {
                chars[i * 3 + 2] = ' ';
            }
        }
        return new string(chars);
    }

    private static char Digit(int v) => (char)(v < 10 ? '0' + v : 'a' + v - 10);
}
