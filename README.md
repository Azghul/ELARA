# ELARA

Easy Local Audio Recording App

A lightweight Windows desktop recorder for microphone and system audio.

## Features

- Recording modes: `Both` (system audio left, microphone right), `Mic`, `System`
- Selectable microphone (Windows default + all capture devices)
- Selectable playback device captured via WASAPI loopback (Windows default + all playback devices)
- Output formats: MP3 (~160 kbit/s) and PCM WAV
- Both mode records stereo (left = system audio, right = microphone)
- Visible device, mode and format selectors in the main window
- Configurable recording location
- System tray integration (hide to tray, restore, exit)
- Persistent settings (devices, format, output location)
- MP3-to-WAV rescue if MP3 encoding fails
- Raw PCM data is preserved in critical failure cases
- Self-contained win-x64 release (no .NET installation required on the target PC)

## Origins

ELARA was originally based on SimpleAudioRecorder by SickPuppyCoding.
The project has since diverged substantially through changes to the UI,
device selection, output formats, configuration, tray integration,
output handling and recording-safety behaviour.

Original copyright/license notices remain preserved.

- Current project: https://github.com/Azghul/ELARA
- Original project: https://github.com/SickPuppyCoding/SimpleAudioRecorder

## Status

Version 1.0.0

## Third-party libraries

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for licenses of the bundled libraries
and the native LAME encoder.

## Build

The project (`ELARA.csproj`) targets `net10.0-windows` and builds on Linux thanks to
`EnableWindowsTargeting`:

```bash
dotnet build ELARA.csproj
```

## Publish (Windows x64)

Self-contained release (no .NET installation required on the target PC):

```bash
./scripts/publish-win-x64.sh
```

This creates `artifacts/ELARA-<version>-win-x64.zip` containing the EXE and all required
DLLs (including the native LAME encoder and the license files). The script also prints
the publish output so the contained files can be verified.

## Logs

The app writes verbose session logs to a per-user log folder. It prefers the normal
Windows app-data location and falls back to another writable folder automatically if needed.

## Settings

Your last selections are remembered across restarts:

- selected microphone
- selected playback (system audio) device
- output format (MP3/WAV)
- recording output location

Settings are stored as JSON in the per-user app data folder
(`%LOCALAPPDATA%\ELARA\settings.json`). Settings from ELARA 0.8.0 are migrated
automatically on first start; the legacy file is never deleted.

New recordings are saved to `Documents\ELARA` by default. Recordings made with
ELARA 0.8.0 in the old default folder (`Documents\Simple Audio Recorder`) are not
moved and remain untouched.

If a saved device or output folder no longer exists at startup, the app falls back
to the Windows default location and logs a warning. If a device selected in the UI
disappears before you start a recording, the recording is **not** started and an
error is shown instead of silently switching devices.

## Data safety

- Temporary raw PCM files are only deleted after the final MP3/WAV file was written successfully.
- If MP3 encoding fails, the recording is automatically saved as WAV and the UI tells you so.
- If saving fails completely, the raw audio is kept and its path is shown in the error message and written to the log.
- In Both mode a single silent track (for example no system audio playing) is saved as silence.
