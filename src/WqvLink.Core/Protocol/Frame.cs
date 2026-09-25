namespace WqvLink.Core.Protocol;

/// <summary>A decoded, checksum-valid frame. Equality compares the data bytes, not the buffer!</summary>
public sealed record Frame(byte Addr, byte Ctrl, ReadOnlyMemory<byte> Data)
{
    public bool Equals(Frame? other) =>
        other is not null && Addr == other.Addr && Ctrl == other.Ctrl && Data.Span.SequenceEqual(other.Data.Span);

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Addr);
        h.Add(Ctrl);
        h.AddBytes(Data.Span);
        return h.ToHashCode();
    }

    public override string ToString()
    {
        var span = Data.Span;
        var hex = Hex.Format(span[..Math.Min(16, span.Length)]);
        var more = span.Length > 16 ? " ..." : "";
        return $"[{Addr:X2} {Ctrl:X2}] {hex}{more} ({span.Length} B)";
    }
}
