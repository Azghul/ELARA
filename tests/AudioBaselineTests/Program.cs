using ELARA;
using System.Text.Json;

const int sampleRate = 48_000;
var testDirectory = Path.Combine(Path.GetTempPath(), "ELARA-AudioBaselineTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testDirectory);

try
{
    AssertEqual((short)32767, WavUtility.MixToMono(32767, 32767), "positive full-scale mix");
    AssertEqual(short.MinValue, WavUtility.MixToMono(short.MinValue, short.MinValue), "negative full-scale mix");
    AssertEqual((short)-1, WavUtility.MixToMono(32767, short.MinValue), "symmetric opposite-polarity mix");

    var (systemOffset, microphoneOffset) = WavUtility.CalculateStartOffsets(
        systemFirstQpcPosition: 1_010_000,
        microphoneFirstQpcPosition: 1_000_000,
        sampleRate);
    AssertEqual(48L, systemOffset, "QPC system offset");
    AssertEqual(0L, microphoneOffset, "QPC microphone offset");

    var sourcePcm = Path.Combine(testDirectory, "source.pcm");
    WritePcm(sourcePcm, -32768, -1234, 0, 1234, 32767);
    var monoWav = Path.Combine(testDirectory, "mono.wav");
    WavUtility.WriteMonoWavFromPcm(sourcePcm, monoWav, sampleRate, bitsPerSample: 16, isMicrophoneTrack: true);
    AssertWave(monoWav, expectedChannels: 1, expectedSamples: new short[] { -32768, -1234, 0, 1234, 32767 });

    var systemPcm = Path.Combine(testDirectory, "system.pcm");
    var microphonePcm = Path.Combine(testDirectory, "microphone.pcm");
    WritePcm(systemPcm, 1000, 2000, 3000);
    WritePcm(microphonePcm, 3000, 4000, 5000);
    var bothWav = Path.Combine(testDirectory, "both.wav");
    WavUtility.WriteMonoMixWavFromMonoPcm(
        systemPcm,
        microphonePcm,
        bothWav,
        sampleRate,
        bitsPerSample: 16,
        systemFirstQpcPosition: 2_000_000,
        microphoneFirstQpcPosition: 2_000_000);
    AssertWave(bothWav, expectedChannels: 1, expectedSamples: new short[] { 2000, 3000, 4000 });

    // ------------------------------------------------------------------
    // Both-mode unity mix: a source without any signal no longer halves
    // the active source (no artificial gain either).
    // ------------------------------------------------------------------
    var silentSystemPcm = Path.Combine(testDirectory, "silent-system.pcm");
    var activeMicrophonePcm = Path.Combine(testDirectory, "active-microphone.pcm");
    WritePcm(silentSystemPcm, 0, 0, 0);
    WritePcm(activeMicrophonePcm, 3000, 4000, 5000);
    var bothUnityWav = Path.Combine(testDirectory, "both-unity.wav");
    WavUtility.WriteMonoMixWavFromMonoPcm(
        silentSystemPcm,
        activeMicrophonePcm,
        bothUnityWav,
        sampleRate,
        bitsPerSample: 16,
        systemFirstQpcPosition: null,
        microphoneFirstQpcPosition: null);
    AssertWave(bothUnityWav, expectedChannels: 1, expectedSamples: new short[] { 3000, 4000, 5000 });

    var silentMicrophonePcm = Path.Combine(testDirectory, "silent-microphone.pcm");
    var activeSystemPcm = Path.Combine(testDirectory, "active-system.pcm");
    WritePcm(silentMicrophonePcm, 0, 0);
    WritePcm(activeSystemPcm, -2500, 7500);
    var bothSystemUnityWav = Path.Combine(testDirectory, "both-system-unity.wav");
    WavUtility.WriteMonoMixWavFromMonoPcm(
        activeSystemPcm,
        silentMicrophonePcm,
        bothSystemUnityWav,
        sampleRate,
        bitsPerSample: 16,
        systemFirstQpcPosition: null,
        microphoneFirstQpcPosition: null);
    AssertWave(bothSystemUnityWav, expectedChannels: 1, expectedSamples: new short[] { -2500, 7500 });

    AssertEqual(true, WavUtility.PcmFileHasSignal(systemPcm), "signal check finds signal");
    AssertEqual(false, WavUtility.PcmFileHasSignal(silentSystemPcm), "signal check detects silence");
    AssertEqual(false, WavUtility.PcmFileHasSignal(Path.Combine(testDirectory, "missing.pcm")), "signal check handles missing file");

    var gains = BothMixProfile.SelectMixGains(true, true);
    AssertEqual(BothMixProfile.HalfGain, gains.SystemGain, "both signals: system gain 50 percent");
    AssertEqual(BothMixProfile.HalfGain, gains.MicrophoneGain, "both signals: microphone gain 50 percent");
    (gains.SystemGain, gains.MicrophoneGain) = BothMixProfile.SelectMixGains(true, false);
    AssertEqual(BothMixProfile.UnityGain, gains.SystemGain, "silent microphone: system at unity");
    AssertEqual(BothMixProfile.SilenceGain, gains.MicrophoneGain, "silent microphone: microphone muted");
    (gains.SystemGain, gains.MicrophoneGain) = BothMixProfile.SelectMixGains(false, true);
    AssertEqual(BothMixProfile.SilenceGain, gains.SystemGain, "silent system: system muted");
    AssertEqual(BothMixProfile.UnityGain, gains.MicrophoneGain, "silent system: microphone at unity");
    AssertEqual((short)4000, BothMixProfile.MixFrame(0, 4000, BothMixProfile.SilenceGain, BothMixProfile.UnityGain), "unity passthrough microphone");
    AssertEqual((short)-2500, BothMixProfile.MixFrame(-2500, 0, BothMixProfile.UnityGain, BothMixProfile.SilenceGain), "unity passthrough system");
    AssertEqual("System50Percent+Microphone50Percent", BothMixProfile.DescribeMixMode(true, true), "mix mode description 50/50");
    AssertEqual("MicrophoneUnity", BothMixProfile.DescribeMixMode(false, true), "mix mode description microphone unity");

    // ------------------------------------------------------------------
    // Version display: reads the version dynamically, strips any
    // build suffix and four-part assembly versions.
    // ------------------------------------------------------------------
    AssertEqual("1.1.0", VersionText.ParseDisplayVersion("1.1.0"), "display version");
    AssertEqual("1.1.0", VersionText.ParseDisplayVersion("1.1.0+abcdef"), "display version strips build suffix");
    AssertEqual("1.1.0", VersionText.ParseDisplayVersion("1.1.0.0"), "display version strips fourth component");

    // ------------------------------------------------------------------
    // Save As path handling: extension matches the chosen format, file
    // names are adopted, and rescue never overwrites an existing file.
    // ------------------------------------------------------------------
    var asDir = Path.Combine(testDirectory, "saveas");
    Directory.CreateDirectory(asDir);
    var asMp3 = Path.Combine(asDir, "meeting");
    var asMp3Path = OutputPathUtility.EnsureExtensionMatches(asMp3, OutputFormat.Mp3);
    AssertEqual(asMp3 + ".mp3", asMp3Path, "MP3 filter appends .mp3");
    AssertEqual(asMp3 + ".wav", OutputPathUtility.EnsureExtensionMatches(asMp3, OutputFormat.Wav), "WAV filter appends .wav");
    var typedWav = OutputPathUtility.EnsureExtensionMatches(asMp3Path, OutputFormat.Wav);
    AssertEqual(asMp3 + ".wav", typedWav, "WAV filter replaces .mp3 extension");
    AssertEqual(
        asMp3 + ".mp3",
        OutputPathUtility.EnsureExtensionMatches(asMp3Path, OutputFormat.Mp3),
        "MP3 filter keeps .mp3 extension");

    var rescueDir = Path.Combine(testDirectory, "rescue");
    Directory.CreateDirectory(rescueDir);
    var existingWav = Path.Combine(rescueDir, "meeting.wav");
    File.WriteAllText(existingWav, "keep");
    AssertEqual(
        Path.Combine(rescueDir, "meeting (2).wav"),
        OutputPathUtility.ResolveRescueWavPath(Path.Combine(rescueDir, "meeting.mp3")),
        "WAV rescue never overwrites an existing WAV");
    var existingMp3 = Path.Combine(rescueDir, "meeting.mp3");
    File.WriteAllText(existingMp3, "keep");
    AssertEqual(
        Path.Combine(rescueDir, "meeting (2).mp3"),
        OutputPathUtility.ResolveRescueMp3Path(Path.Combine(rescueDir, "meeting.wav")),
        "MP3 rescue never overwrites an existing MP3");

    // ------------------------------------------------------------------
    // Endpoint selection (A-E): explicit ids stay exact, a missing device
    // never falls back to a default, defaults use Capture/Render + the
    // multimedia role and never the communications role.
    // ------------------------------------------------------------------
    var explicitPlan = CaptureEndpointPlanner.Plan(EDataFlow.Capture, "selected-microphone-endpoint-id");
    AssertEqual(CaptureEndpointSelectionMode.ExplicitDevice, explicitPlan.Mode, "explicit device id plans explicit selection");
    AssertEqual("selected-microphone-endpoint-id", explicitPlan.DeviceId, "explicit device id stays exactly the same");
    AssertEqual(EDataFlow.Capture, explicitPlan.DataFlow, "explicit microphone plan uses capture flow");

    var missingPlan = CaptureEndpointPlanner.Plan(EDataFlow.Capture, "gone-device-id");
    AssertEqual(true, missingPlan.IsExplicitDevice, "missing explicit device keeps explicit mode (no default fallback)");
    AssertEqual(
        "The selected microphone is no longer available. Right-click the app and choose a different microphone.",
        CaptureEndpointText.UnavailableDeviceMessage(EDataFlow.Capture),
        "missing microphone reports an error instead of switching");
    AssertEqual(
        "The selected playback device is no longer available. Right-click the app and choose a different system audio device.",
        CaptureEndpointText.UnavailableDeviceMessage(EDataFlow.Render),
        "missing playback device reports an error instead of switching");

    var defaultPlan = CaptureEndpointPlanner.Plan(EDataFlow.Capture, null);
    AssertEqual(CaptureEndpointSelectionMode.WindowsDefault, defaultPlan.Mode, "null id plans windows default");
    var whitespacePlan = CaptureEndpointPlanner.Plan(EDataFlow.Capture, "   ");
    AssertEqual(CaptureEndpointSelectionMode.WindowsDefault, whitespacePlan.Mode, "whitespace id plans windows default");
    AssertEqual(ERole.Multimedia, defaultPlan.DefaultRole, "default microphone uses the multimedia role");
    AssertEqual(ERole.Multimedia, CaptureEndpointPlanner.Plan(EDataFlow.Render, null).DefaultRole, "default playback uses the multimedia role");
    AssertEqual(EDataFlow.Render, CaptureEndpointPlanner.Plan(EDataFlow.Render, null).DataFlow, "system audio uses render flow");
    AssertEqual(false, defaultPlan.DefaultRole == ERole.Communications, "communications role is never used");

    // ------------------------------------------------------------------
    // Endpoint quality classification: neutral names, format-based
    // verdicts. The transport alone is never a quality verdict.
    // ------------------------------------------------------------------
    AssertEqual(false, EndpointQualityClassifier.IsTelephonyStyleEndpoint("Microphone", "USB Conference Microphone"), "plain USB source: no telephony warning");
    AssertEqual(false, EndpointQualityClassifier.IsTelephonyStyleEndpoint("Microphone", "Studio USB Microphone"), "studio USB source: no telephony warning");
    AssertEqual(false, EndpointQualityClassifier.IsTelephonyStyleEndpoint("Microphone", "USB Audio Interface"), "audio interface: no telephony warning");
    AssertEqual(false, EndpointQualityClassifier.IsTelephonyStyleEndpoint("Microphone", "Laptop Microphone"), "laptop microphone: no telephony warning");
    AssertEqual(false, EndpointQualityClassifier.IsTelephonyStyleEndpoint("Microphone", "Webcam Microphone"), "webcam microphone: no telephony warning");
    AssertEqual(true, EndpointQualityClassifier.IsTelephonyStyleEndpoint("Microphone", "Bluetooth Headset Hands-Free AG Audio"), "hands-free AG audio: telephony warning");
    AssertEqual(true, EndpointQualityClassifier.IsTelephonyStyleEndpoint("Headset", "Any Device Name"), "headset form factor: telephony warning");
    AssertEqual(true, EndpointQualityClassifier.IsTelephonyStyleEndpoint("Handset", null), "handset form factor: telephony warning");
    AssertEqual(true, EndpointQualityClassifier.IsTelephonyStyleEndpoint(null, "Wireless USB Audio (Chat)"), "chat hint: telephony warning");
    AssertEqual(true, EndpointQualityClassifier.IsTelephonyStyleEndpoint(null, "Telephony Device"), "telephony hint: telephony warning");
    AssertEqual(false, EndpointQualityClassifier.IsTelephonyStyleEndpoint(null, null), "no data: no telephony warning");

    AssertEqual(true, EndpointQualityClassifier.BuildLowBandwidthWarning(8000)?.Contains("Low-bandwidth") == true, "8 kHz warns low-bandwidth");
    AssertEqual(true, EndpointQualityClassifier.BuildLowBandwidthWarning(16000)?.Contains("Low-bandwidth") == true, "16 kHz warns low-bandwidth");
    AssertEqual(true, EndpointQualityClassifier.BuildLowBandwidthWarning(32000)?.Contains("below 44.1") == true, "32 kHz gives a hint");
    AssertEqual(true, EndpointQualityClassifier.BuildLowBandwidthWarning(44100) is null, "44.1 kHz warns nothing");
    AssertEqual(true, EndpointQualityClassifier.BuildLowBandwidthWarning(48000) is null, "48 kHz warns nothing");
    AssertEqual(true, EndpointQualityClassifier.BuildLowBandwidthWarning(96000) is null, "96 kHz warns nothing");
    AssertEqual(true, EndpointQualityClassifier.BuildLowBandwidthWarning(0) is null, "unknown rate warns nothing");

    var telephonyReport = new EndpointDiagnosticsReport(
        IsAvailable: true, IsWindowsDefault: false, PreferredDeviceId: "id", EndpointId: "ep",
        FriendlyName: "Bluetooth Headset Hands-Free AG Audio", FormFactor: null, DeviceState: "Active", DataFlow: "Capture",
        RoleDescription: "Explicit device selection", Transport: AudioEndpointTransport.Bluetooth,
        MixSampleRate: 16000, MixChannels: 1, MixBitsPerSample: 16, MixFormatDescription: "16-bit PCM",
        MixChannelMask: null, Error: null);
    AssertEqual(true, telephonyReport.BuildQualityWarning()?.Contains("telephony") == true, "telephony endpoint reports a warning");

    var usbReport = new EndpointDiagnosticsReport(
        IsAvailable: true, IsWindowsDefault: false, PreferredDeviceId: "id", EndpointId: "ep",
        FriendlyName: "USB Conference Microphone", FormFactor: "Microphone", DeviceState: "Active", DataFlow: "Capture",
        RoleDescription: "Explicit device selection", Transport: AudioEndpointTransport.Usb,
        MixSampleRate: 48000, MixChannels: 2, MixBitsPerSample: 32, MixFormatDescription: "32-bit float",
        MixChannelMask: null, Error: null);
    AssertEqual(true, usbReport.BuildQualityWarning() is null, "48 kHz USB source: no quality warning");

    var bluetoothReport = new EndpointDiagnosticsReport(
        IsAvailable: true, IsWindowsDefault: false, PreferredDeviceId: "id", EndpointId: "ep",
        FriendlyName: "Bluetooth Stereo Microphone", FormFactor: null, DeviceState: "Active", DataFlow: "Capture",
        RoleDescription: "Explicit device selection", Transport: AudioEndpointTransport.Bluetooth,
        MixSampleRate: 48000, MixChannels: 1, MixBitsPerSample: 16, MixFormatDescription: "16-bit PCM",
        MixChannelMask: null, Error: null);
    AssertEqual(true, bluetoothReport.BuildQualityWarning() is null, "48 kHz bluetooth source without telephony hints: no blanket warning");

    // ------------------------------------------------------------------
    // Format descriptions: PCM16, float32, channel counts.
    // ------------------------------------------------------------------
    AssertEqual("16-bit PCM", AudioFormatText.ForTagAndBits(1, 16), "PCM16 tag description");
    AssertEqual("32-bit float", AudioFormatText.ForTagAndBits(3, 32), "float32 tag description");
    AssertEqual("extensible", AudioFormatText.ForTagAndBits(0xFFFE, 16), "extensible tag description");
    AssertEqual("24-bit PCM", AudioFormatText.ForExtensibleSubFormat(AudioFormatText.PcmSubFormat, 24), "extensible PCM24 sub-format");
    AssertEqual("32-bit float", AudioFormatText.ForExtensibleSubFormat(AudioFormatText.IeeeFloatSubFormat, 32), "extensible float sub-format");

    AssertEqual(
        "16-bit PCM · 48000 Hz · 1 channel · 16-bit",
        new CapturedStreamFormat(1, 48000, 1, 16).Description,
        "mono PCM16 capture format description");
    AssertEqual(
        "32-bit float · 48000 Hz · 2 channels · 32-bit",
        new CapturedStreamFormat(3, 48000, 2, 32).Description,
        "stereo float capture format description");
    AssertEqual(
        "extensible · 16000 Hz · 6 channels · 16-bit",
        new CapturedStreamFormat(0xFFFE, 16000, 6, 16).Description,
        "multichannel capture format description");

    // ------------------------------------------------------------------
    // Output profile: single source of truth for MP3/WAV parameters.
    // ------------------------------------------------------------------
    AssertEqual(48000, RecordingOutputProfile.SampleRate, "output sample rate is 48 kHz");
    AssertEqual(1, RecordingOutputProfile.Channels, "output is mono");
    AssertEqual(16, RecordingOutputProfile.BitsPerSample, "output is PCM16");
    AssertEqual(128, RecordingOutputProfile.Mp3BitRateKbps, "MP3 target is 128 kbit/s");
    AssertEqual(true, RecordingOutputProfile.Mp3ShortDescription.Contains("128 kbps"), "MP3 description carries the bitrate");
    AssertEqual(true, RecordingOutputProfile.WavShortDescription.Contains("PCM16"), "WAV description carries PCM16");

    // ------------------------------------------------------------------
    // Settings: output format defaults and round-trips. Legacy settings
    // files that still carry the removed processing key load unchanged;
    // ELARA no longer configures any microphone processing.
    // ------------------------------------------------------------------
    AssertEqual(
        OutputFormat.Mp3.ToString(),
        new AppSettings().ResolveOutputFormat().ToString(),
        "settings default output format is MP3");

    var legacyJson = "{\"OutputFormat\":\"Wav\"}";
    var legacySettings = JsonSerializer.Deserialize<AppSettings>(legacyJson) ?? new AppSettings();
    AssertEqual(
        OutputFormat.Wav.ToString(),
        legacySettings.ResolveOutputFormat().ToString(),
        "settings OutputFormat Wav resolves");

    var legacyProcessingJson = "{\"OutputFormat\":\"Mp3\",\"MicrophoneProcessingMode\":\"WindowsNoiseSuppression\"}";
    var legacyProcessingSettings = JsonSerializer.Deserialize<AppSettings>(legacyProcessingJson) ?? new AppSettings();
    AssertEqual(
        OutputFormat.Mp3.ToString(),
        legacyProcessingSettings.ResolveOutputFormat().ToString(),
        "settings with the legacy processing key still load");

    Console.WriteLine("Audio baseline tests passed.");
}
finally
{
    Directory.Delete(testDirectory, recursive: true);
}

static void WritePcm(string path, params short[] samples)
{
    using var writer = new BinaryWriter(File.Create(path));
    foreach (var sample in samples)
    {
        writer.Write(sample);
    }
}

static void AssertWave(string path, short expectedChannels, short[] expectedSamples)
{
    using var reader = new BinaryReader(File.OpenRead(path));
    AssertEqual("RIFF", new string(reader.ReadChars(4)), "RIFF marker");
    reader.ReadInt32();
    AssertEqual("WAVE", new string(reader.ReadChars(4)), "WAVE marker");
    AssertEqual("fmt ", new string(reader.ReadChars(4)), "format marker");
    AssertEqual(16, reader.ReadInt32(), "PCM format chunk size");
    AssertEqual((short)1, reader.ReadInt16(), "PCM format tag");
    AssertEqual(expectedChannels, reader.ReadInt16(), "channel count");
    AssertEqual(sampleRate, reader.ReadInt32(), "sample rate");
    reader.BaseStream.Position = 36;
    AssertEqual("data", new string(reader.ReadChars(4)), "data marker");
    AssertEqual(expectedSamples.Length * sizeof(short), reader.ReadInt32(), "data length");

    foreach (var expectedSample in expectedSamples)
    {
        AssertEqual(expectedSample, reader.ReadInt16(), "PCM sample");
    }

    AssertEqual(reader.BaseStream.Length, reader.BaseStream.Position, "WAV end position");
}

static void AssertEqual<T>(T expected, T actual, string description)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{description}: expected '{expected}', actual '{actual}'.");
    }
}