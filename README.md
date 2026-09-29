# SimpleAudioRecorder

Simple Audio Recorder is a Windows desktop executable for capturing:

- `Both`: system audio on the left channel and microphone audio on the right channel
- `Mic`: a selectable microphone only (mono)
- `System`: a selectable playback device using WASAPI loopback (mono)

Output is saved as **MP3 (~160 kbit/s, 48 kHz)** or **PCM WAV** — selectable in the context menu. If MP3 encoding fails, the recording is automatically rescued as WAV.

Recordings are saved to `Documents\Simple Audio Recorder` and the app falls back to another writable local folder if Windows blocks that location.

The main window is intentionally compact:

- Click the mode label (`Both`, `Mic`, or `System`) to cycle capture sources
- Click the main button to start or stop recording
- Right-click anywhere in the app for `About App`, `View Audio Files`, `Microphone`, `System Audio`, `Format`, and `Exit`
- Use the `Microphone` submenu to choose a specific input device instead of the Windows default
- Use the `System Audio` submenu to choose which playback device is captured via WASAPI loopback instead of the Windows default
- Use the `Format` submenu to switch between `MP3` and `WAV`
- `About App` includes `Open Log Files` for verbose diagnostic logs

## Settings

Your last selections are remembered across restarts:

- selected microphone
- selected playback (system audio) device
- output format (MP3/WAV)

Settings are stored as JSON in the per-user app data folder (`...\SickPuppyCoding\SimpleAudioRecorder\settings.json`). If a saved device no longer exists at startup, the app falls back to the Windows default and logs a warning. If a device selected in the menu disappears before you start a recording, the recording is **not** started and an error is shown instead of silently switching devices.

## Data safety

- Temporary raw PCM files are only deleted after the final MP3/WAV file was written successfully.
- If MP3 encoding fails, the recording is automatically saved as WAV and the UI tells you so.
- If saving fails completely, the raw audio is kept and its path is shown in the error message and written to the log.

## Logs

The app writes verbose session logs to a per-user log folder. It prefers the normal Windows app-data location and falls back to another writable folder automatically if needed.

## Build (Linux or Windows)

The project targets `net10.0-windows` and builds on Linux thanks to `EnableWindowsTargeting`:

```bash
dotnet build
```

## Publish (Windows x64)

Self-contained release (no .NET installation required on the target PC):

```bash
./scripts/publish-win-x64.sh
```

This creates `artifacts/SimpleAudioRecorder-<version>-win-x64.zip` containing the EXE and all required DLLs (including the native LAME libraries). It also prints the publish output so the contained files can be verified.

## Third-party libraries and licenses

| Component | License | Notes |
|---|---|---|
| [NAudio.Lame](https://www.nuget.org/packages/NAudio.Lame/) | MIT | MP3 encoder wrapper (Copyright (c) Corey Murtagh) |
| [NAudio.Core](https://www.nuget.org/packages/NAudio.Core/) | MIT | Pulled in transitively by NAudio.Lame |
| LAME `libmp3lame.64.dll` / `libmp3lame.32.dll` | LGPL | Unmodified native LAME encoder libraries bundled with NAudio.Lame; they are copied to the application folder by the package. Source: [lame.sourceforge.io](https://lame.sourceforge.io) |

This project is maintained by [Azghul](https://github.com/Azghul/SimpleAudioRecorder) and is based on the original [SimpleAudioRecorder by SickPuppyCoding](https://github.com/SickPuppyCoding/SimpleAudioRecorder). The original MIT license and copyright notices are preserved in [LICENSE](LICENSE).
