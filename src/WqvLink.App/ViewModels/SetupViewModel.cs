using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WqvLink.App.Services;
using WqvLink.Core.Firmware;
using WqvLink.Core.Storage;
using WqvLink.Core.Transport;

namespace WqvLink.App.ViewModels;

/// <summary>Microcontroller setup: find the Pico, save its port, flash firmware.</summary>
public sealed partial class SetupViewModel : ObservableObject, IDisposable
{
    private readonly Settings _settings;

    [ObservableProperty]
    private PortInfo? _selectedPort;

    [ObservableProperty]
    private string _detectText = "";

    [ObservableProperty]
    private string _savedPortText = "";

    [ObservableProperty]
    private double _flashPercent;

    [ObservableProperty]
    private string _flashMessage = "";

    [ObservableProperty]
    private bool _isBusy;

    //True once firmware has been flashed and the Pico came back
    [ObservableProperty]
    private bool _flashSucceeded;

    public SetupViewModel(Settings settings)
    {
        _settings = settings;
        Check = new HardwareCheckViewModel(settings);
        Firmware = FirmwareCatalog.LoadBridge();
        FirmwareText = Firmware is null
            ? "No firmware is bundled with this build. Use \"Flash a .uf2 file…\"."
            : $"WQV bridge firmware for Raspberry Pi Pico (RP2040), {Firmware.Length / 1024} KB.";
        Refresh();
    }

    public ObservableCollection<PortInfo> Ports { get; } = [];

    //The guided "Test it works" check system
    public HardwareCheckViewModel Check { get; }

    public byte[]? Firmware { get; }

    public string FirmwareText { get; }

    public bool HasFirmware => Firmware is not null;

    //True if anything was saved, so the main window can refresh
    public bool Changed { get; private set; }

    public void Refresh()
    {
        var ports = PortDiscovery.ListPorts();
        Ports.Clear();
        foreach (var p in ports)
        {
            Ports.Add(p);
        }
        SelectedPort = ports.FirstOrDefault(p => string.Equals(p.Name, _settings.LastPort, StringComparison.OrdinalIgnoreCase))
            ?? PortDiscovery.AutoSelect(ports)
            ?? ports.FirstOrDefault();

        var drive = PicoFlasher.FindBootDrives().FirstOrDefault();
        var picos = ports.Where(p => p.IsPico).ToList();
        DetectText = drive is not null
            ? $"A Pico is in bootloader mode (drive {drive}). Press Flash to install the firmware."
            : picos.Count switch
            {
                0 => "No Pico found. Plug it in with a USB data cable (some cables are charge-only), then press Refresh.",
                1 => $"Pico found on {picos[0].Name}.",
                _ => $"{picos.Count} Picos found: {string.Join(", ", picos.Select(p => p.Name))}. Pick one below.",
            };
        SavedPortText = _settings.LastPort is { } saved ? $"Saved port: {saved}" : "No port saved yet.";
    }

    //Saves the selected port. false if none is selected
    public bool SavePort()
    {
        if (SelectedPort is null)
        {
            return false;
        }
        _settings.LastPort = SelectedPort.Name;
        _settings.Save();
        Changed = true;
        SavedPortText = $"Saved port: {SelectedPort.Name}";
        return true;
    }

    //flashes <paramref name="uf2"/>, then saves the port the Pico comes back on.
    public async Task FlashAsync(byte[] uf2, CancellationToken ct)
    {
        IsBusy = true;
        FlashPercent = 0;
        try
        {
            var port = SelectedPort is { IsPico: true } sel ? sel.Name : PortDiscovery.AutoSelect(PortDiscovery.ListPorts())?.Name;
            var progress = new Progress<FlashProgress>(p =>
            {
                FlashPercent = p.Percent;
                FlashMessage = p.Message;
            });
            var newPort = await PicoFlasher.FlashAsync(uf2, port, progress, ct);
            if (newPort is not null)
            {
                _settings.LastPort = newPort;
                _settings.VerifiedPort = newPort;
                _settings.Save();
                Changed = true;
                FlashMessage = $"Done. The Pico restarted on {newPort}, and that port has been saved. Press Next to test it.";
                FlashSucceeded = true;
            }
            else
            {
                FlashMessage = "Flashed, but the Pico's serial port didn't come back. Unplug it, plug it back in, then press Refresh.";
            }
        }
        catch (OperationCanceledException)
        {
            FlashMessage = "Cancelled.";
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            FlashMessage = $"Flashing failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    public void Dispose() => Check.Dispose();
}
