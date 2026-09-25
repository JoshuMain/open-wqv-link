using System.IO.Ports;
using Microsoft.Win32;
using WqvLink.Core.Protocol;

namespace WqvLink.Core.Transport;

/// <summary>A serial port and whether it looks like the Pico bridge.</summary>
public sealed record PortInfo(string Name, bool IsPico, string Description)
{
    public override string ToString() => IsPico ? $"{Name}  Pico (recommended)" : Name;
}

/// <summary>
/// Lists serial ports and flags Raspberry Pi USB devices (VID 2E8A) - never opens a port
/// </summary>
public static class PortDiscovery
{
    public static IReadOnlyList<PortInfo> ListPorts()
    {
        var names = SafeGetPortNames();
        HashSet<string> picos;
        if (OperatingSystem.IsWindows())
        {
            picos = WindowsPicoPorts();
        }
        else if (OperatingSystem.IsLinux())
        {
            picos = names.Where(LinuxIsPico).ToHashSet(StringComparer.Ordinal);
        }
        else if (OperatingSystem.IsMacOS())
        {
            // No cheap VID lookup without IOKit
            names = names.Where(n => !n.StartsWith("/dev/tty.", StringComparison.Ordinal)).ToArray();
            picos = names.Where(n => n.StartsWith("/dev/cu.usbmodem", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
        }
        else
        {
            picos = [];
        }

        return names
            .Select(n => new PortInfo(n, picos.Contains(n), picos.Contains(n) ? "Pico (recommended)" : "Serial port"))
            .OrderByDescending(p => p.IsPico)
            .ThenBy(p => p.Name, NaturalComparer.Instance)
            .ToList();
    }

    //The single Pico port, or null if there are none or several
    public static PortInfo? AutoSelect(IReadOnlyList<PortInfo> ports)
    {
        var picos = ports.Where(p => p.IsPico).ToList();
        return picos.Count == 1 ? picos[0] : null;
    }

    private static string[] SafeGetPortNames()
    {
        try
        {
            return SerialPort.GetPortNames().Distinct(StringComparer.Ordinal).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return [];
        }
    }

    /// <summary>
    /// Reads the <c>HKLM\SYSTEM\CurrentControlSet\Enum\USB\VID_2E8A*\*\Device Parameters\PortName</c>.
    /// Entries remain for unplugged devices, so callers intersect with the live port list.
    /// </summary>
    private static HashSet<string> WindowsPicoPorts()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows())
        {
            return result;
        }
        try
        {
            using var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
            if (usb is null)
            {
                return result;
            }
            foreach (var device in usb.GetSubKeyNames().Where(k => k.StartsWith("VID_" + WqvConstants.PicoUsbVendorId, StringComparison.OrdinalIgnoreCase)))
            {
                using var deviceKey = usb.OpenSubKey(device);
                if (deviceKey is null)
                {
                    continue;
                }
                foreach (var instance in deviceKey.GetSubKeyNames())
                {
                    using var parameters = deviceKey.OpenSubKey(instance + @"\Device Parameters");
                    if (parameters?.GetValue("PortName") is string port)
                    {
                        result.Add(port);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Registry not readable so fall back to plain names.
        }
        return result;
    }

    /// <summary>Linux: <c>/sys/class/tty/ttyACM0/device/../idVendor</c>.</summary>
    private static bool LinuxIsPico(string devPath)
    {
        try
        {
            var name = Path.GetFileName(devPath);
            var vendorFile = Path.Combine("/sys/class/tty", name, "device", "..", "idVendor");
            return File.Exists(vendorFile)
                && string.Equals(File.ReadAllText(vendorFile).Trim(), WqvConstants.PicoUsbVendorId, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Sorts COM2 before COM10.</summary>
    private sealed class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (x is null || y is null)
            {
                return string.CompareOrdinal(x, y);
            }
            var (px, nx) = Split(x);
            var (py, ny) = Split(y);
            var c = string.Compare(px, py, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : nx != ny ? nx.CompareTo(ny) : string.CompareOrdinal(x, y);
        }

        private static (string Prefix, long Number) Split(string s)
        {
            var i = s.Length;
            while (i > 0 && char.IsAsciiDigit(s[i - 1]))
            {
                i--;
            }
            return i < s.Length && long.TryParse(s.AsSpan(i), out var n) ? (s[..i], n) : (s, -1);
        }
    }
}
