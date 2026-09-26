# Changelog

All notable changes to Open WQV Link are listed here, newest first.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[Semantic Versioning](https://semver.org/). The version number lives in `Directory.Build.props`.

<!--
How to use:
- Add each change under "Unreleased" as you make it, in one of these sections:
  Added / Changed / Fixed / Removed / Security.
- When you publish a release, rename "Unreleased" to the version and date, e.g. "## [0.4.0] - 2026-10-12",
  update <Version> in Directory.Build.props to match, and start a new empty "Unreleased" section.
-->

## [Unreleased]

## [0.4.0] - 2026-09-25

First public version of the C# app!

### Added
- Desktop app (Windows and Linux builds tested - macOS untested) with a photo gallery, a photo viewer and export.
- **Get photos from watch** with get-ready instructions, progress, time remaining, cancel and retry.
- **Pico setup**: finds the Pico's port, flashes the bundled WQV bridge firmware (no Arduino IDE
  needed), and a guided hardware test (send via phone camera, receive via TV remote, optional watch ping).
- **Tests** menu: loopback, blast, echo, sniff and ping.
- Options: save location, PNG or BMP, 1×/2×/4×/8× size, file-name templates, file dates set to when the
  photo was taken, name and date stored inside PNGs (EXIF), optional JSON sidecars.
- Each download is saved in its own dated folder, with the raw `dump.bin` and a byte-level `session.log`.
- `wqvlink` command-line tool with the same commands as the original Python script.
- Automated tests, including a simulated watch, so the protocol can be tested without hardware.
- Documentation: wiring, setup, using the app, troubleshooting, command line, how it works, building.
- App icon: a pixel-sharp wrist-camera watch, on the window title bars and the .exe.

### Fixed
- Protocol details corrected from real-watch testing: date bytes are hour then minute, the low nibble is
  the left pixel, the header carries the photo count, and packets carry 128 bytes.
- A lost reply at the very end of a download no longer throws away the photos already received.
- Improved stability of tests.