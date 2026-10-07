# ELARA

Easy Local Audio Recording App

A lightweight Windows desktop recorder for microphone and system audio.

## Features

- Recording modes: `Both` (system audio + microphone mono mix), `Mic`, `System`
- Selectable microphone (Windows default + all capture devices)
- Selectable playback device captured via WASAPI loopback (Windows default + all playback devices)
- ELARA records the exact Windows microphone endpoint selected by the user and shows endpoint and format diagnostics to help identify low-bandwidth, telephony or hands-free capture profiles
- Output formats: MP3 (mono, 48 kHz, 128 kbit/s CBR, single encode pass) and PCM WAV — chosen in the Save As dialog when a recording starts (a default timestamped file name is offered)
- All output is 48 kHz mono; Both mixes system audio and microphone 50/50, and uses the one active source at unity when the other carries no signal at all
- Pre-recording diagnostics panel: selected input device, used audio endpoint, endpoint state, data flow, transport (only when Windows reports it reliably, e.g. USB), form factor, sample rate, channel count, input bit depth/format (PCM vs IEEE float, incl. WAVEFORMATEXTENSIBLE sub-format and channel mask) and the actual output parameters; during a recording it shows the capture formats the audio clients really initialized
- Read-only quality hints derived from the actual format (low-bandwidth 8/16 kHz warning, mild hint below 44.1 kHz); device-type words, transport and form factor alone never warn — a small "Quality: OK" note marks full-bandwidth inputs; ELARA never switches, blocks or reconfigures anything
- Microphone capture requests Windows RAW stream options and falls back to the normal shared mode when RAW is unavailable (logged with `WindowsProcessingMayBeActive=True`)
- No automatic gain, loudness maximization, noise suppression, echo cancellation or nonlinear saturation is applied or requested by ELARA; device effect discovery is read-only
- Visible mode and device selectors in the main window
- Configurable recording location
- System tray integration (hide to tray, restore, exit)
- Application icon and version display (v1.1.0)
- Persistent settings (devices, last used format, output location)
- MP3-to-WAV rescue if MP3 encoding fails, and WAV-to-MP3 rescue if a recording exceeds the WAV (RIFF) size limit
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

Version 1.1.0

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

Two distribution variants are built (both self-contained release hygiene applies:
no PDBs, no logs, no settings, x64 LAME only):

```bash
./scripts/publish-win-x64.sh
```

This creates:

- `artifacts/ELARA-<version>-win-x64-portable.zip` — self-contained, includes the
  .NET runtime; no separate .NET installation required on the target PC.
- `artifacts/ELARA-<version>-win-x64-runtime-required.zip` — framework-dependent;
  requires Microsoft .NET 10 Desktop Runtime x64 to be installed. Smaller
  download, same application (no trimming is applied to keep the build stable).

Both ZIPs contain the EXE/DLLs, the native LAME encoder, the app icon and the
license files. The script validates each variant, prints the publish output and
reports the ZIP sizes and SHA256 checksums.

## Windows downloads

- **Portable** (`ELARA-<version>-win-x64-portable.zip`)
  No separate .NET installation required.
- **Runtime-required** (`ELARA-<version>-win-x64-runtime-required.zip`)
  Requires Microsoft .NET 10 Desktop Runtime x64.

## Logs

The app writes verbose session logs to a per-user log folder. It prefers the normal
Windows app-data location and falls back to another writable folder automatically if needed.

## Settings

Your last selections are remembered across restarts:

- selected microphone
- selected playback (system audio) device
- last used output format (used to preselect the Save As filter)
- recording output location

Settings are stored as JSON in the per-user app data folder
(`%LOCALAPPDATA%\ELARA\settings.json`). Settings from ELARA 0.8.0 are migrated
automatically on first start; the legacy file is never deleted. ELARA no longer
configures any microphone processing: the microphone stream always requests RAW
Windows stream options with a safe shared-mode fallback, and device effect
discovery is read-only.

New recordings are saved to `Documents\ELARA` by default. Recordings made with
ELARA 0.8.0 in the old default folder (`Documents\Simple Audio Recorder`) are not
moved and remain untouched.

If a saved device or output folder no longer exists at startup, the app falls back
to the Windows default **output location** and logs a warning. A saved audio device
that is missing at startup is kept as the selection (shown as "unavailable") and a
recording is **not** started until the device returns or another device is chosen —
ELARA never silently records a different device than the one you selected. If a
device selected in the UI disappears before you start a recording, the recording is
also not started and an error is shown instead of silently switching devices.

## Data safety

- Temporary raw PCM files are only deleted after the final MP3/WAV file was written successfully.
- If MP3 encoding fails, the recording is automatically saved as WAV and the UI tells you so.
- If WAV encoding fails (for example a multi-hour recording exceeding the WAV/RIFF size limit), the recording is automatically saved as MP3 and the UI tells you so.
- If saving fails completely, the raw audio is kept and its path is shown in the error message and written to the log.
- In Both mode a single silent track is no longer halved: the source that carries signal is used at unity, a fully silent session is saved as silence.
