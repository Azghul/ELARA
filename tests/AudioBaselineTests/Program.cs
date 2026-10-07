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
    // verdicts. Device-type words, transport and form factor alone never
    // produce a warning — only concrete quality indicators do.
    // ------------------------------------------------------------------
    AssertEqual(false, EndpointQualityClassifier.IsStrongTelephonyProfileName("USB Conference Microphone"), "plain USB speakerphone name: no telephony profile");
    AssertEqual(false, EndpointQualityClassifier.IsStrongTelephonyProfileName("Freisprechtelefon mit Echoausschaltung"), "german speakerphone name: no telephony profile");
    AssertEqual(false, EndpointQualityClassifier.IsStrongTelephonyProfileName("Studio USB Microphone"), "studio USB source: no telephony profile");
    AssertEqual(false, EndpointQualityClassifier.IsStrongTelephonyProfileName("USB Audio Interface"), "audio interface: no telephony profile");
    AssertEqual(false, EndpointQualityClassifier.IsStrongTelephonyProfileName("Laptop Microphone"), "laptop microphone: no telephony profile");
    AssertEqual(false, EndpointQualityClassifier.IsStrongTelephonyProfileName("Webcam Microphone"), "webcam microphone: no telephony profile");
    AssertEqual(false, EndpointQualityClassifier.IsStrongTelephonyProfileName("Bluetooth Stereo Microphone"), "bluetooth transport word alone: no telephony profile");
    AssertEqual(false, EndpointQualityClassifier.IsStrongTelephonyProfileName("Communications Device"), "communications word: no telephony profile");
    AssertEqual(false, EndpointQualityClassifier.IsStrongTelephonyProfileName("Chat Microphone"), "chat word: no telephony profile");
    AssertEqual(false, EndpointQualityClassifier.IsStrongTelephonyProfileName("Headset"), "headset word: no telephony profile");
    AssertEqual(false, EndpointQualityClassifier.IsStrongTelephonyProfileName(null), "no data: no telephony profile");
    AssertEqual(true, EndpointQualityClassifier.IsStrongTelephonyProfileName("Bluetooth Headset Hands-Free AG Audio"), "hands-free AG audio: telephony profile");
    AssertEqual(true, EndpointQualityClassifier.IsStrongTelephonyProfileName("Headset (Handsfree AG Audio)"), "handsfree AG audio variant: telephony profile");
    AssertEqual(true, EndpointQualityClassifier.IsStrongTelephonyProfileName("Microphone (HFP)"), "HFP acronym: telephony profile");
    AssertEqual(true, EndpointQualityClassifier.IsStrongTelephonyProfileName("Headset (HSP)"), "HSP acronym: telephony profile");
    AssertEqual(false, EndpointQualityClassifier.IsStrongTelephonyProfileName("Microphone with HFPX in name"), "HFP needs word boundaries");
    AssertEqual(false, EndpointQualityClassifier.IsStrongTelephonyProfileName("Microphone with HSPAL in name"), "HSP needs word boundaries");

    AssertEqual(true, EndpointQualityClassifier.IsFullBandwidth(44100), "44.1 kHz is full bandwidth");
    AssertEqual(true, EndpointQualityClassifier.IsFullBandwidth(48000), "48 kHz is full bandwidth");
    AssertEqual(false, EndpointQualityClassifier.IsFullBandwidth(32000), "32 kHz is not full bandwidth");
    AssertEqual(true, EndpointQualityClassifier.HasReducedBandwidthIndicator(16000), "16 kHz indicates reduced bandwidth");
    AssertEqual(true, EndpointQualityClassifier.HasReducedBandwidthIndicator(32000), "32 kHz indicates reduced bandwidth");
    AssertEqual(false, EndpointQualityClassifier.HasReducedBandwidthIndicator(48000), "48 kHz does not indicate reduced bandwidth");
    AssertEqual(false, EndpointQualityClassifier.HasReducedBandwidthIndicator(0), "unknown rate indicates nothing");

    AssertEqual(true, EndpointQualityClassifier.BuildLowBandwidthWarning(8000)?.Contains("Low-bandwidth") == true, "8 kHz warns low-bandwidth");
    AssertEqual(true, EndpointQualityClassifier.BuildLowBandwidthWarning(16000)?.Contains("(16 kHz)") == true, "16 kHz warns low-bandwidth with the rate");
    AssertEqual(true, EndpointQualityClassifier.BuildLowBandwidthWarning(32000)?.Contains("below 44.1") == true, "32 kHz gives only a mild hint");
    AssertEqual(false, EndpointQualityClassifier.BuildLowBandwidthWarning(32000)?.Contains("Low-bandwidth") == true, "32 kHz does not give the strong low-bandwidth warning");
    AssertEqual(true, EndpointQualityClassifier.BuildLowBandwidthWarning(44100) is null, "44.1 kHz warns nothing");
    AssertEqual(true, EndpointQualityClassifier.BuildLowBandwidthWarning(48000) is null, "48 kHz warns nothing");
    AssertEqual(true, EndpointQualityClassifier.BuildLowBandwidthWarning(96000) is null, "96 kHz warns nothing");
    AssertEqual(true, EndpointQualityClassifier.BuildLowBandwidthWarning(0) is null, "unknown rate warns nothing");

    var usbSpeakerphoneReport = new EndpointDiagnosticsReport(
        IsAvailable: true, IsWindowsDefault: false, PreferredDeviceId: "id", EndpointId: "ep",
        FriendlyName: "Freisprechtelefon mit Echoausschaltung (USB Conference Microphone)", FormFactor: "Speakers",
        DeviceState: "Active", DataFlow: "Capture", RoleDescription: "Explicit device selection",
        Transport: AudioEndpointTransport.Usb, MixSampleRate: 48000, MixChannels: 2, MixBitsPerSample: 32,
        MixFormatDescription: "32-bit float", MixChannelMask: null, Error: null);
    AssertEqual(true, usbSpeakerphoneReport.BuildQualityWarning() is null, "48 kHz USB speakerphone: no warning");
    AssertEqual(false, usbSpeakerphoneReport.HasTelephonyProfileName, "speakerphone name is not a telephony profile");

    var usbHeadsetReport = new EndpointDiagnosticsReport(
        IsAvailable: true, IsWindowsDefault: false, PreferredDeviceId: "id", EndpointId: "ep",
        FriendlyName: "USB Headset", FormFactor: "Headset",
        DeviceState: "Active", DataFlow: "Capture", RoleDescription: "Explicit device selection",
        Transport: AudioEndpointTransport.Usb, MixSampleRate: 48000, MixChannels: 1, MixBitsPerSample: 16,
        MixFormatDescription: "16-bit PCM", MixChannelMask: null, Error: null);
    AssertEqual(true, usbHeadsetReport.BuildQualityWarning() is null, "48 kHz headset (headset form factor): no warning");

    var bluetoothFullRateReport = new EndpointDiagnosticsReport(
        IsAvailable: true, IsWindowsDefault: false, PreferredDeviceId: "id", EndpointId: "ep",
        FriendlyName: "Bluetooth Stereo Microphone", FormFactor: "Microphone",
        DeviceState: "Active", DataFlow: "Capture", RoleDescription: "Explicit device selection",
        Transport: AudioEndpointTransport.Bluetooth, MixSampleRate: 48000, MixChannels: 1, MixBitsPerSample: 16,
        MixFormatDescription: "16-bit PCM", MixChannelMask: null, Error: null);
    AssertEqual(true, bluetoothFullRateReport.BuildQualityWarning() is null, "48 kHz bluetooth source: no blanket transport warning");

    var bluetoothHfpFullRateReport = new EndpointDiagnosticsReport(
        IsAvailable: true, IsWindowsDefault: false, PreferredDeviceId: "id", EndpointId: "ep",
        FriendlyName: "Bluetooth Headset Hands-Free AG Audio", FormFactor: "Headset",
        DeviceState: "Active", DataFlow: "Capture", RoleDescription: "Explicit device selection",
        Transport: AudioEndpointTransport.Bluetooth, MixSampleRate: 48000, MixChannels: 1, MixBitsPerSample: 16,
        MixFormatDescription: "16-bit PCM", MixChannelMask: null, Error: null);
    AssertEqual(true, bluetoothHfpFullRateReport.BuildQualityWarning() is null, "48 kHz hands-free profile name without bandwidth indicator: no warning");

    var bluetoothHfp16kReport = new EndpointDiagnosticsReport(
        IsAvailable: true, IsWindowsDefault: false, PreferredDeviceId: "id", EndpointId: "ep",
        FriendlyName: "Bluetooth Headset Hands-Free AG Audio", FormFactor: "Headset",
        DeviceState: "Active", DataFlow: "Capture", RoleDescription: "Explicit device selection",
        Transport: AudioEndpointTransport.Bluetooth, MixSampleRate: 16000, MixChannels: 1, MixBitsPerSample: 16,
        MixFormatDescription: "16-bit PCM", MixChannelMask: null, Error: null);
    var hfpWarning = bluetoothHfp16kReport.BuildQualityWarning();
    AssertEqual(true, hfpWarning?.Contains("Low-bandwidth") == true, "16 kHz hands-free endpoint: low-bandwidth warning");
    AssertEqual(true, hfpWarning?.Contains("telephony") == true, "16 kHz hands-free endpoint: telephony hint");

    var lowBandwidth16kReport = new EndpointDiagnosticsReport(
        IsAvailable: true, IsWindowsDefault: true, PreferredDeviceId: null, EndpointId: "ep",
        FriendlyName: "Some Microphone", FormFactor: "Microphone",
        DeviceState: "Active", DataFlow: "Capture", RoleDescription: "Windows default (Multimedia role)",
        Transport: AudioEndpointTransport.Usb, MixSampleRate: 16000, MixChannels: 1, MixBitsPerSample: 16,
        MixFormatDescription: "16-bit PCM", MixChannelMask: null, Error: null);
    AssertEqual(true, lowBandwidth16kReport.BuildQualityWarning()?.Contains("Low-bandwidth") == true, "16 kHz input: low-bandwidth warning without a profile name");

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

    // ------------------------------------------------------------------
    // Static UI/packaging checks: the main window is resizable, the visible
    // diagnostics area is gone from the main window, diagnostics live behind
    // Options, and the publish script produces both ZIP variants. Plain
    // source-level checks (no UI automation).
    // ------------------------------------------------------------------
    var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var mainFormSource = File.ReadAllText(Path.Combine(sourceRoot, "MainForm.cs"));
    var optionsSource = File.ReadAllText(Path.Combine(sourceRoot, "OptionsDialog.cs"));
    var diagnosticsSource = File.ReadAllText(Path.Combine(sourceRoot, "DiagnosticsDialog.cs"));
    var scriptSource = File.ReadAllText(Path.Combine(sourceRoot, "scripts", "publish-win-x64.sh"));
    var versionSource = File.ReadAllText(Path.Combine(sourceRoot, "AppVersion.cs"));

    AssertEqual(true, mainFormSource.Contains("FormBorderStyle.Sizable"), "main window uses a sizable frame");
    AssertEqual(true, mainFormSource.Contains("MaximizeBox = true"), "main window can maximize");
    AssertEqual(true, mainFormSource.Contains("MinimizeBox = true"), "main window can minimize");
    AssertEqual(true, mainFormSource.Contains("MinimumSize = SizeFromClientSize"), "main window defines a minimum size derived from the compact layout");
    AssertEqual(false, mainFormSource.Contains("MaximumSize = Size"), "main window is not capped to a fixed size");
    AssertEqual(true, mainFormSource.Contains("OnSizeChanged") && mainFormSource.Contains("LayoutCompactControls"), "main window relayouts on resize (responsive layout)");
    AssertEqual(true, mainFormSource.Contains("ClientSize.Width - selectorLeft"), "selector width grows with the window width");
    AssertEqual(true, mainFormSource.Contains("ClientSize.Width - 64"), "record button grows with the window width");
    AssertEqual(false, mainFormSource.Contains("diagnosticsLabel"), "no visible diagnostics area on the main window");
    AssertEqual(false, mainFormSource.Contains("diagnosticsFieldLabel"), "no diagnostics header on the main window");
    AssertEqual(true, mainFormSource.Contains("DiagnosticsDialog"), "main window opens the diagnostics dialog");
    AssertEqual(true, mainFormSource.Contains("SaveWindowBounds") && mainFormSource.Contains("RestoreWindowBounds"), "window bounds are persisted with screen validation");
    AssertEqual(true, optionsSource.Contains("Audio Diagnostics..."), "options dialog exposes the diagnostics dialog");
    AssertEqual(true, diagnosticsSource.Contains("Clipboard.SetText"), "diagnostics dialog can copy the report to the clipboard");
    AssertEqual(true, diagnosticsSource.Contains("Refresh"), "diagnostics dialog can refresh");
    AssertEqual(true, diagnosticsSource.Contains("AudioEndpointDiagnostics.GetMicrophone"), "diagnostics dialog reuses the shared endpoint diagnostics logic");
    AssertEqual(true, scriptSource.Contains("win-x64-portable.zip"), "publish script builds the portable ZIP");
    AssertEqual(true, scriptSource.Contains("win-x64-runtime-required.zip"), "publish script builds the runtime-required ZIP");
    AssertEqual(true, versionSource.Contains("Application.ProductVersion"), "version stays dynamic");

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