# Building from source

## Requirements

- **.NET SDK 8 (and maybe newer?)** (<https://dotnet.microsoft.com/download>). The app targets `net8.0`
- IDEs: Visual Studio 2022 or JetBrains Rider with the AvaloniaRider plugin / VS Code with the C# Dev Kit
- Optional, only to rebuild the Pico firmware: `arduino-cli` (the build script installs the board package for you) or Arduino IDE 2.

### Extra setup per OS

| OS | What you need |
|---|---|
| Windows | Nothing extra |
| macOS | Nothing extra to build. See [Publishing](#publishing-a-release) for running unsigned builds |
| Linux | Avalonia's runtime libraries, e.g. on Debian/Ubuntu: `sudo apt install libx11-6 libice6 libsm6 libfontconfig1`. For serial access, add yourself to the `dialout` group: `sudo usermod -aG dialout $USER`, then log out and back in |

## Build, test, run

```bash
dotnet build WqvLink.sln
dotnet test WqvLink.sln
dotnet run --project src/WqvLink.App
```

In Visual Studio, open `WqvLink.sln`, make sure **WqvLink.App** is the startup project, and press F5.

The build treats warnings as errors, so it has to stay warning-free.

### Running a subset of tests

```bash
dotnet test --filter "FullyQualifiedName~Framing"      # just the framing tests
```

The tests need no hardware. `FakeWatchTransport` simulates a WQV-1, including dropped replies, IR echo and corrupted checksums, so protocol changes can be tested fully offline! Although nothing beats a real test.

### The CLI

```bash
dotnet run --project src/WqvLink.Cli -- list-ports
dotnet run --project src/WqvLink.Cli -- selftest
dotnet run --project src/WqvLink.Cli -- -p COM10 download
```

## Project layout

| Folder | Contents |
|---|---|
| `src/WqvLink.Core` | Protocol, serial transport, image decoding, PNG/BMP writing, flashing, diagnostics. No UI; this is the part that talks to the watch |
| `src/WqvLink.App` | The Avalonia desktop app (views and view models) |
| `src/WqvLink.Cli` | The `wqvlink` command-line tool |
| `tests/WqvLink.Core.Tests` | xUnit tests. No hardware needed; a simulated watch covers the protocol |
| `firmware/` | The Pico Sketch, prebuilt `.uf2` file plus the build scripts |
| `docs/` | These guides |

## Working with real hardware

1. Wire the Pico and IrDA 3 Click as in [the wiring guide](wiring.md). The two most common faults are **RST not at 3.3 V** and **TX/RX not crossed over**.
2. Flash the bridge firmware from the app (**Microcontroller -> Set up / flash Pico firmware...**), drag `firmware/dist/wqv_bridge-rp2040.uf2` onto the `RPI-RP2` drive or use the arduino IDE to flash the project.
3. Check the hardware before debugging any code:
   ```bash
   dotnet run --project src/WqvLink.Cli -- -p COM10 sniff    # point a TV remote at the Click; bytes should appear
   dotnet run --project src/WqvLink.Cli -- -p COM10 ping     # watch in IR -> COM -> PC
   ```
4. Every session writes a byte-level `session.log` into its output folder. Attach it to any bug report about the protocol.

**Privacy:** real dumps contain your photos. Keep them in `samples/`. Never commit them or use them as test fixtures. Tests use synthetic data only.

Once your hardware is confirmed working, you can now test / debug!

## Rebuilding the firmware

```powershell
./firmware/build.ps1          # Windows (or `pwsh ./firmware/build.ps1` on macOS/Linux if arduino-cli is on a PATH)
```

This installs the pinned Raspberry Pi Pico board package if needed, compiles the sketches for RP2040, copies the `.uf2` files into `firmware/dist/` and prints the SHA-256 hashes. The app embeds whatever is in `dist/`.

## Publishing a release

Self-contained, single-file builds (no .NET install needed by users):

```bash
dotnet publish src/WqvLink.App -c Release -r <RID> --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

| Platform | `<RID>` |
|---|---|
| Windows x64 | `win-x64` |
| Linux x64 | `linux-x64` |
| macOS Intel | `osx-x64` |
| macOS Apple Silicon | `osx-arm64` |

Output goes to `src/WqvLink.App/bin/Release/net8.0/<RID>/publish/`.

**Notes:**
- `IncludeNativeLibrariesForSelfExtract` is needed for a genuinely single file. Without it, Avalonia's native libraries sit next to the executable.
- **Don't enable trimming** (`PublishTrimmed`). Avalonia uses reflection, and trimmed builds can break at runtime in ways the build doesn't catch.
- **Linux:** mark the output executable (`chmod +x WqvLink`).
- **macOS:** builds aren't signed or notarised, so users must right-click -> **Open** the first time, or run `xattr -dr com.apple.quarantine <path>`.

**Release checklist:**
1. Update `<Version>` in `Directory.Build.props` and add an entry to `CHANGELOG.md`.
2. Make sure `dotnet test` passes and the firmware hashes match.
3. Tag the commit `vX.Y.Z` and push the tag.
4. Smoke-test at least the Windows build against real hardware before announcing it.

## Troubleshooting the build

| Symptom | Fix |
|---|---|
| `A compatible .NET SDK was not found` | Install an SDK which is 8 or newer. Check `global.json` hasn't been changed to pin an exact version |
| Build fails on a warning | Intentional. Fix the warning. Suppress it only with a comment explaining why |
| `file is locked by another process` during build | The app or CLI is still running. Close it |
| Avalonia XAML previewer is blank | Build the solution once. The previewer needs compiled output |
| Serial port "access denied" | Another program (Arduino Serial Monitor, a second app instance) has the port open |

## Contributing

- **Branches:** work on a branch and open a pull request against `main`.
- **Tests:** new behaviour needs tests. Protocol and decoding changes need a test against `FakeWatchTransport` or a synthetic fixture.
- **Protocol changes need evidence.** Any change to protocol constants, field order or decoding must include the `session.log`, or a trimmed excerpt, from real hardware that justifies it. One and a bit of the WQV-1 field orders on older reverse-engineering pages turned out to be wrong, so we verify rather than trust.
- **Style:** run `dotnet format` before committing. 
- **Commits:** keep them small, with an imperative subject line (for example "Add RP2350 bootloader detection").
