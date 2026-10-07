# Changelog

# 1.1.0

- Endpoint diagnostics (read-only, before recording starts): friendly name,
  endpoint ID, device state, data flow, selection kind (explicit device or
  Windows default multimedia role), transport when Windows reports it
  reliably (e.g. USB), form factor, native/mix format with sample rate,
  channel count, bits per sample, PCM/IEEE-float payload, WAVEFORMATEXTENSIBLE
  sub-format and channel mask
- Exact microphone endpoint handling: an explicit device selection is used
  verbatim or fails with a clear error; a saved device that is missing at
  startup is kept as the selection (shown as unavailable) instead of silently
  falling back to the Windows default; endpoint identity is validated at
  capture start (SelectedEndpointMatchesResolved) and a mismatch aborts the
  recording
- RAW capture baseline: the microphone stream always requests RAW Windows
  stream options and falls back to the normal shared mode when rejected
  (logged with WindowsProcessingMayBeActive=True); recording never aborts in
  that case
- Removed the active noise-suppression control: ELARA no longer requests the
  speech capture category and no longer changes any audio effect state;
  device effect discovery is strictly read-only
- Read-only quality hints derived from the actual format: low-bandwidth
  warnings for 8/16 kHz capture formats and a mild hint below 44.1 kHz;
  an unambiguous telephony profile name (e.g. "Hands-Free AG Audio", HFP/HSP)
  is only surfaced together with a reduced-bandwidth format — device-type
  words, transport and form factor alone never warn, so high-quality
  speakerphones and headsets stay clean; a small "Quality: OK" note marks
  full-bandwidth inputs
- Simplified, user-friendly diagnostics panel (device name, connection when
  reliably known, signal rate/channels, capture mode, output profile,
  quality note); technical details (endpoint ID, float/PCM payload, form
  factor, channel mask, transport detection, RAW requested/activated) stay
  in the log and the tooltip
- Unified capture pipeline: 48 kHz mono PCM16 temp files, at most one
  WASAPI shared-mode conversion (AUTOCONVERTPCM + default-quality SRC),
  exactly one MP3 encoding pass
- MP3 output: mono, 48 kHz, 128 kbit/s CBR, defined once in a shared profile
  (RecordingOutputProfile) used by the encoder, capture service, UI and tests
- WAV output: PCM16, 48 kHz, mono, with a size guard that rescues to MP3
  instead of writing a broken header
- Long-session robustness: capture counters (data discontinuities, timestamp
  errors, silent packets) are tracked and logged; the Both-mode mix uses the
  one active source at unity when the other track carries no signal
- Full diagnostics of selected/resolved endpoints and requested/initialized
  capture formats are written to the session log

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
