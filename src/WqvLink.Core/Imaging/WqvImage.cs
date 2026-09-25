namespace WqvLink.Core.Imaging;

/// <summary>
/// One decoded image record - goodluck!
/// </summary>
/// <param name="Index">1-based position in the dump.</param>
/// <param name="Name">Name with spaces and NULs trimmed - empty means untitled.</param>
/// <param name="Taken">Capture time, or null if the date bytes are not a valid date.</param>
/// <param name="DateBytes">The raw 5 date bytes. year−2000, month, day, hour, minute.</param>
/// <param name="Pixels4bpp">7200 packed pixel bytes, low nibble = left pixel.</param>
public sealed record WqvImage(int Index, string Name, DateTime? Taken, byte[] DateBytes, byte[] Pixels4bpp)
{
    /// <summary><c>yyyyMMdd-HHmm</c>, or <c>raw</c> plus the date bytes in hex when undated.</summary>
    public string Stamp => Taken is { } t
        ? t.ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture)
        : "raw" + Convert.ToHexString(DateBytes).ToLowerInvariant();

    /// <summary>Name reduced to letters and digits (others become '_'), or "untitled".</summary>
    public string SafeName
    {
        get
        {
            var chars = Name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
            var safe = new string(chars).Trim('_');
            return safe.Length == 0 ? "untitled" : safe;
        }
    }

    /// <summary>File name without extension: <c>{index:000}_{stamp}_{safeName}</c>, the default file name.</summary>
    public string BaseFileName => $"{Index:000}_{Stamp}_{SafeName}";
}

/// <summary>Decoder options; both default to off and exist for diagnostics.</summary>
public sealed record DecodeOptions(bool SwapNibbles = false, bool Invert = false)
{
    public static DecodeOptions Default { get; } = new();
}
