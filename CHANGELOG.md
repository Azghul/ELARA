# Changelog

# 1.0.1

- Improved audio capture/mixing quality
- Synchronized microphone/system capture
- RAW microphone baseline
- Optional Windows noise suppression
- Mono WAV/MP3 meeting mix
- Save As with filename and format selection
- Application icon
- Version display
- Selector popup positioning fix
- Microphone capture requests Windows RAW processing and falls back safely to the
  normal default stream when RAW is unavailable
- Both-mode capture uses a shared start barrier, shared stop signal and QPC-based
  start alignment before producing a 48 kHz mono mix
- Removed automatic gain, loudness maximization and nonlinear saturation from all
  WAV and MP3 output paths
- Added read-only logging for microphone APO effects without changing effect state

# 1.0.0

- The product is now ELARA (Easy Local Audio Recording App); executable, assembly
  and Windows file metadata carry the ELARA name
- Settings, logs and recordings now live in ELARA locations
  (`%LOCALAPPDATA%\ELARA`, `Documents\ELARA`); 0.8.0 settings are migrated
  automatically and legacy files are never deleted
- Recordings never overwrite existing files (unique file names)
- Recording start, stop and encoding run off the UI thread so the window stays
  responsive during slow devices and long recordings
- Closing during a recording now shows save failures instead of swallowing them
- Full license texts for all bundled third-party components are included in the
  release (NAudio.Core, NAudio.Lame, LAME 3.100)

# 0.8.0

- Redesigned desktop UI with visible mode, device and format selectors
- Selectable microphone and system audio (playback) devices with Windows default fallback
- MP3 and WAV recording
- Both mode records stereo (system audio left, microphone right)
- Configurable recording location
- System tray integration (hide to tray, restore, exit)
- Persistent settings (devices, format, output location)
- Improved recording failure and data-recovery behaviour (raw PCM preserved on critical failures, MP3-to-WAV rescue)
- Options dialog (recording location, open audio/log files)
