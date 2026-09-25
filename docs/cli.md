# Command line (`wqvlink`)

Most of what the app does is also available from the command line, which is handy for scripting and debugging. 

```bash
dotnet run --project src/WqvLink.Cli -- <command> [options]
```

Or run the built `wqvlink.exe` from `src/WqvLink.Cli/bin/Debug/net8.0/`.

## Options for every command

| Option | Meaning |
|---|---|
| `-p`, `--port PORT` | Serial port, e.g. `COM10` or `/dev/ttyACM0`. Leave it out to use the only Pico found |
| `-v`, `--verbose` | Print every byte sent and received |
| `--log FILE` | Where to write the byte trace |
| `--fake-dump FILE` | Pretend to be a watch holding the photos in `FILE`. No hardware needed |

## Commands

| Command | What it does |
|---|---|
| `list-ports` | List serial ports; the Pico is marked |
| `loopback` | Checks USB -> Pico -> back. Unplug the Click and connect GP0 to GP1 first |
| `blast [--seconds N]` | Flash the IR LED for N seconds (default 10). Watch with a phone camera |
| `echo` | Send frames at a mirror or white card and count reflections |
| `sniff` | Print everything the Click receives. Ctrl+C to stop |
| `ping` | Handshake with the watch, show its clock, disconnect |
| `download [--out ROOT] [--expect N] [--scale 1\|2\|4\|8] [--swap-nibbles] [--invert] [--no-sidecars]` | Download all photos into `ROOT/yyyyMMdd-HHmmss/` |
| `decode FILE [--out DIR] ...` | Turn a `dump.bin` into images, with no watch needed |
| `selftest` | Quick internal check |

## Examples

```bash
wqvlink list-ports
wqvlink -p COM10 ping
wqvlink -p COM10 download --out D:\WQV
wqvlink decode D:\WQV\20260925-181502\dump.bin --out D:\WQV\big --scale 4
wqvlink --fake-dump dump.bin download --out test-output
```