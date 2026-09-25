using System.Globalization;
using WqvLink.Core.Imaging;
using WqvLink.Core.Protocol;

namespace WqvLink.Core.Storage;

/// <summary>
/// Download sessions on disk -> <c>{root}/yyyyMMdd-HHmmss/</c> holding <c>dump.bin</c>,
/// <c>session.log</c> and one PNG (plus optional JSON sidecar) per image
/// </summary>
public sealed class SessionStore(string root)
{
    public const string DumpFileName = "dump.bin";
    public const string LogFileName = "session.log";

    public string Root { get; } = root;

    //<c>&lt;Pictures&gt;/WQV-1</c> - falling back to the user profile
    public static string DefaultRoot
    {
        get
        {
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrEmpty(pictures))
            {
                pictures = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures");
            }
            return Path.Combine(pictures, "WQV-1");
        }
    }

    //Creates a new, unique session folder
    public string CreateSessionFolder(DateTime now)
    {
        var name = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(Root, name);
        for (var n = 2; Directory.Exists(path); n++)
        {
            path = Path.Combine(Root, $"{name}-{n}");
        }
        Directory.CreateDirectory(path);
        return path;
    }

    //Writes <c>dump.bin</c> (records + extra) and exports every image, rweturns to is the PNG paths
    public static IReadOnlyList<string> SaveDownload(string folder, DownloadResult result, ExportOptions export)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, DumpFileName), [.. result.Raw, .. result.Extra]);
        var images = ImageDecoder.DecodeDump(result.Raw, out _);
        return ExportAll(images, folder, export);
    }

    //exports each image into the folder
    public static IReadOnlyList<string> ExportAll(IEnumerable<WqvImage> images, string folder, ExportOptions export) =>
        ImageExporter.ExportAll(images, folder, export);


    //Session folders under the root, newest first (only those holding a dump.bin)
    public IReadOnlyList<string> ListSessions()
    {
        if (!Directory.Exists(Root))
        {
            return [];
        }
        return Directory.GetDirectories(Root)
            .Where(d => File.Exists(Path.Combine(d, DumpFileName)))
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .ToList();
    }

    //Decodes a session's dump.bin (or any raw dump file)
    public static IReadOnlyList<WqvImage> LoadDump(string dumpOrFolder)
    {
        var path = Directory.Exists(dumpOrFolder) ? Path.Combine(dumpOrFolder, DumpFileName) : dumpOrFolder;
        return ImageDecoder.DecodeDump(File.ReadAllBytes(path), out _);
    }
}
