# SimpleAudioRecorder

Simple Audio Recorder is a Windows desktop executable for capturing:

- `Both`: system audio on the left channel and microphone audio on the right channel
- `Mic`: your default microphone only
- `System`: your default playback device using WASAPI loopback

The app saves `.wav` files to `Documents\Simple Audio Recorder`.

The main window is intentionally compact:

- Click the mode label (`Both`, `Mic`, or `System`) to cycle capture sources
- Click the main button to start or stop recording
- Right-click anywhere in the app for `About App`, `View Audio Files`, and `Exit`

## Build

```powershell
dotnet build
```

The executable is produced at:

`bin\Debug\net10.0-windows\SimpleAudioRecorder.exe`

## UI Previews

Generate the built-in UI previews and screenshots with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\render-ui-previews.ps1
```

This renders the important UI states in isolation and writes PNGs plus a small `manifest.json` to:

`artifacts\ui-previews`

You can also run the preview mode directly:

```powershell
dotnet run -- --preview
```

To extend the preview set, add or edit states in `UiPreviewState.CreateDefaults(...)`. The renderer in `UiPreviewRunner.cs` will pick them up automatically and write one PNG per state.
