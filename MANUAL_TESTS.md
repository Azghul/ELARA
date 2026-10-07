# Manual Windows test plan (audio endpoints)

ELARA is vendor-agnostic: this plan applies to any Windows capture endpoint.
The goal is to verify endpoint selection, diagnostics, RAW/shared handling and
long-session stability with real devices.

For each of the sources below (A-E), repeat the same checklist:

1. Select the source in ELARA (context menu → Microphone).
2. Check the diagnostics panel **before** recording:
   - `Mic:` shows the expected friendly name
   - `Transport:` matches the physical connection (USB / Bluetooth / Unknown)
   - `Input:` shows the native/mix format (sample rate, channel count, PCM or
     32-bit float)
   - `Capture:` shows `48 kHz · Mono · RAW (requested)`
   - `Output:` shows the chosen format profile
   - Hover the panel for the endpoint ID, device state, data flow and channel mask
3. Start a recording, choose the file name/format in the Save As dialog
   (MP3 mono 48 kHz 128 kbps or WAV PCM16 mono 48 kHz).
4. Record 30–60 seconds of speech.
5. During the recording the panel shows the **actual** values:
   - `Capture:` must show `RAW` for USB/internal sources; `Shared (Windows
     processing may be active)` is acceptable but must be logged
   - no dropouts in the level meter
6. Stop and verify:
   - the file plays back without dropouts or pitch shifts
   - the log contains `SelectedEndpointMatchesResolved=True`, the
     `NativeMixFormat`, the `RequestedMicrophoneCaptureFormat`, the
     `InitializedMicrophoneCaptureFormat` and `RawActivated`
   - no quality warnings appear for high-rate endpoints
7. Repeat with a device that was unplugged before starting: the recording must
   refuse to start (selected device unavailable) and must never silently use
   another device.

## Sources

- **A. USB microphone** — expect transport USB, full-rate input (44.1/48 kHz),
  RAW capture, no quality warnings.
- **B. USB speakerphone / conference microphone** — expect transport USB,
  possibly stereo input that is mixed to mono, RAW capture, no quality warnings
  when the rate is ≥ 44.1 kHz.
- **C. Wireless/USB-dongle device** — Windows typically reports the dongle as a
  USB audio endpoint; ELARA must display what Windows reports (never reclassify
  a USB dongle as Bluetooth).
- **D. Bluetooth microphone/headset (if available)** — may expose a telephony /
  hands-free style endpoint; the panel should show the neutral telephony
  warning and, if the capture format is 8/16 kHz, the low-bandwidth warning.
  Higher-rate Bluetooth endpoints must not produce a blanket transport warning.
- **E. Internal laptop/webcam microphone** — expect transport from Windows,
  RAW capture, no quality warnings at 44.1/48 kHz.

## Regression checks

- Both mode: system audio and microphone align (QPC), mono mix, and if one
  track is entirely silent the other is used at unity (not halved).
- MP3 encoding failure still rescues to WAV; WAV files above the RIFF size
  limit rescue to MP3.
- Canceling the Save As dialog never starts a recording.
- Selector dropdowns stay anchored to their controls; version display reads
  the assembly version.
