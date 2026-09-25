using System.Globalization;
using System.Reflection;

namespace WqvLink.Core.Imaging;

/// <summary>Metadata written into the tEXt and eXIf chunks of an exported PNG</summary>
public sealed record PngMetadata(int Index, string Name, DateTime? Taken, string Software)
{
    public const string Make = "CASIO";
    public const string Model = "WQV-1";

    //Open WQV Link {version}" from the Core assembly's informational version
    public static string DefaultSoftware { get; } = "Open WQV Link " + CoreVersion();

    public static PngMetadata For(WqvImage img) => new(img.Index, img.Name, img.Taken, DefaultSoftware);

    //tEXt keyword/value pairs, in the order they are written
    public IEnumerable<KeyValuePair<string, string>> TextEntries()
    {
        if (Name.Length > 0)
        {
            yield return new("Title", Name);
        }
        yield return new("Description", $"{Make} {Model} wrist camera, image {Index}");
        yield return new("Source", $"{Make} {Model}");
        yield return new("Software", Software);
        if (Taken is { } t)
        {
            yield return new("Creation Time", t.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
        }
    }

    private static string CoreVersion()
    {
        var v = typeof(PngMetadata).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plus = v.IndexOf('+');
        return plus >= 0 ? v[..plus] : v;
    }
}
