# Third-party notices

Open WQV Link's own code is covered by the licence in [LICENSE](LICENSE). It is built on, and distributes, the
following third-party software. Each remains under its own licence.

## Desktop app and command-line tool

These are downloaded from NuGet when you build, and are included in published (self-contained) releases.

| Component | Licence | Source |
|---|---|---|
| Avalonia UI (Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent) | MIT | <https://github.com/AvaloniaUI/Avalonia> |
| SkiaSharp and HarfBuzzSharp (used by Avalonia for drawing and text) | MIT | <https://github.com/mono/SkiaSharp> |
| Inter font (via Avalonia.Fonts.Inter) | SIL Open Font License 1.1 | <https://github.com/rsms/inter> |
| CommunityToolkit.Mvvm | MIT | <https://github.com/CommunityToolkit/dotnet> |
| System.IO.Ports | MIT | <https://github.com/dotnet/runtime> |
| .NET runtime (in self-contained releases) | MIT | <https://github.com/dotnet/runtime> |

The test project also uses xUnit (Apache 2.0) and Microsoft.NET.Test.Sdk (MIT). These are only used to
run the tests and are not distributed with the app.

## Pico firmware (`firmware/dist/wqv_bridge-rp2040.uf2`)

The prebuilt firmware is compiled from `firmware/wqv_bridge/wqv_bridge.ino` and statically includes:

| Component | Licence | Source |
|---|---|---|
| Arduino-Pico core by Earle F. Philhower, III (version 6.1.1) | LGPL 2.1 | <https://github.com/earlephilhower/arduino-pico> |
| Raspberry Pi Pico SDK | BSD 3-Clause | <https://github.com/raspberrypi/pico-sdk> |
| TinyUSB | MIT | <https://github.com/hathach/tinyusb> |

**LGPL note:** the complete source of the sketch and the script used to build it (`firmware/build.ps1`)
are in this repository. You can rebuild the firmware, including with a modified Arduino-Pico core, and
flash it with the app's **Flash a .uf2 file…** option.

## Protocol documentation

The watch protocol was implemented from public documentation by Marcus Gröber
(<https://www.mgroeber.de/wqvprot.html>) and Kees Jongenburger
(<https://wqv-wristcam.sourceforge.net/protocol/protocol.html>), plus testing on a real watch. No code
from those pages is included.

## Trademarks

Casio and WQV are trademarks of Casio Computer Co., Ltd. This project is not affiliated with or endorsed
by Casio. Raspberry Pi and Pico are trademarks of Raspberry Pi Ltd. MikroElektronika and Click are
trademarks of MikroElektronika d.o.o.
