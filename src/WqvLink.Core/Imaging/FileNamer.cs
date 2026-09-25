using System.Globalization;
using System.Text;

namespace WqvLink.Core.Imaging;

/// <summary>
/// Builds file names from a template. Tokens:
/// <c>{index}</c> 001, <c>{stamp}</c> 20241230-1350 (or raw hex when undated), <c>{date}</c> 2024-12-30,
/// <c>{time}</c> 1350, <c>{name}</c> the photo's name or "untitled".
/// </summary>
public static class FileNamer
{
    public const string DefaultTemplate = "{index}_{stamp}_{name}";

    //Presets offered in Options, with a description of each
    public static IReadOnlyList<(string Template, string Label)> Presets { get; } =
    [
        (DefaultTemplate, "001_20241230-1350_name (default)"),
        ("{date}_{time}_{index}", "2024-12-30_1350_001"),
        ("{name}_{index}", "name_001"),
        ("WQV_{index}", "WQV_001"),
    ];

    public static string Render(string template, WqvImage img)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            template = DefaultTemplate;
        }
        var t = img.Taken;
        var result = template
            .Replace("{index}", img.Index.ToString("000", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{stamp}", img.Stamp, StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", t?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "undated", StringComparison.OrdinalIgnoreCase)
            .Replace("{time}", t?.ToString("HHmm", CultureInfo.InvariantCulture) ?? "0000", StringComparison.OrdinalIgnoreCase)
            .Replace("{name}", img.SafeName, StringComparison.OrdinalIgnoreCase);
        return Sanitise(result);
    }

    //replaces characters that aren't allowed in file names on any OS (Learnt this the hard way, it's not pretty)
    public static string Sanitise(string name)
    {
        var bad = new HashSet<char>(Path.GetInvalidFileNameChars()) { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(bad.Contains(c) || char.IsControl(c) ? '_' : c);
        }
        var s = sb.ToString().Trim().TrimEnd('.');
        return s.Length == 0 ? "photo" : s;
    }
}
