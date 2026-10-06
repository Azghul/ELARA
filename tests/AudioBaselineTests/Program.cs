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
    // Version display (requirement D): reads 1.0.1 dynamically, strips any
    // build suffix and four-part assembly versions.
    // ------------------------------------------------------------------
    AssertEqual("1.0.1", VersionText.ParseDisplayVersion("1.0.1"), "display version");
    AssertEqual("1.0.1", VersionText.ParseDisplayVersion("1.0.1+abcdef"), "display version strips build suffix");
    AssertEqual("1.0.1", VersionText.ParseDisplayVersion("1.0.1.0"), "display version strips fourth component");

    // ------------------------------------------------------------------
    // Save As path handling (requirement B): extension matches the chosen
    // format, file names are adopted, and rescue never overwrites a WAV.
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
    var typedMixed = OutputPathUtility.EnsureExtensionMatches(asMp3 + ".bogus", OutputFormat.Mp3);
    AssertEqual(asMp3 + ".mp3", typedMixed, "unknown extension is replaced");

    var rescueMp3 = Path.Combine(asDir, "meeting.mp3");
    AssertEqual(Path.Combine(asDir, "meeting.wav"), OutputPathUtility.ResolveRescueWavPath(rescueMp3), "rescue free wav name");
    File.WriteAllText(Path.Combine(asDir, "meeting.wav"), "existing");
    AssertEqual(Path.Combine(asDir, "meeting (2).wav"), OutputPathUtility.ResolveRescueWavPath(rescueMp3), "rescue skips existing wav");
    File.WriteAllText(Path.Combine(asDir, "meeting (2).wav"), "existing");
    AssertEqual(Path.Combine(asDir, "meeting (3).wav"), OutputPathUtility.ResolveRescueWavPath(rescueMp3), "rescue increments until free");

    var defaultName = OutputPathUtility.DefaultRecordingFileName(DateTime.Now);
    AssertEqual(19, defaultName.Length, "default save-as file name length (yyyy-MM-dd HH-mm-ss)");
    AssertEqual('-', defaultName[4], "default save-as file name year separator");

    // ------------------------------------------------------------------
    // Settings (requirement A): MicrophoneProcessingMode defaults to Raw for
    // existing settings files (missing field), persists Raw and
    // WindowsNoiseSuppression, and keeps the JSON field name.
    // ------------------------------------------------------------------
    AssertEqual(
        MicrophoneProcessingMode.Raw.ToString(),
        new AppSettings().ResolveMicrophoneProcessingMode().ToString(),
        "settings default microphone processing is Raw");
    AssertEqual(
        OutputFormat.Mp3.ToString(),
        new AppSettings().ResolveOutputFormat().ToString(),
        "settings default output format is MP3");

    var legacyJson = "{\"OutputFormat\":\"Wav\"}";
    var legacySettings = JsonSerializer.Deserialize<AppSettings>(legacyJson) ?? new AppSettings();
    AssertEqual(
        MicrophoneProcessingMode.Raw.ToString(),
        legacySettings.ResolveMicrophoneProcessingMode().ToString(),
        "settings without MicrophoneProcessingMode resolve to Raw");
    AssertEqual(
        OutputFormat.Wav.ToString(),
        legacySettings.ResolveOutputFormat().ToString(),
        "settings OutputFormat Wav resolves");

    var rawJson = JsonSerializer.Serialize(new AppSettings { MicrophoneProcessing = nameof(MicrophoneProcessingMode.Raw) });
    AssertEqual(
        true,
        rawJson.Contains("\"MicrophoneProcessingMode\"") && rawJson.Contains("\"Raw\""),
        "settings JSON stores Raw under the MicrophoneProcessingMode key");

    var wnsJson = JsonSerializer.Serialize(new AppSettings { MicrophoneProcessing = nameof(MicrophoneProcessingMode.WindowsNoiseSuppression) });
    AssertEqual(
        true,
        wnsJson.Contains("\"MicrophoneProcessingMode\":\"WindowsNoiseSuppression\"")
            || (wnsJson.Contains("\"MicrophoneProcessingMode\"") && wnsJson.Contains("\"WindowsNoiseSuppression\"")),
        "settings JSON stores WindowsNoiseSuppression under the MicrophoneProcessingMode key");
    var wnsSettings = JsonSerializer.Deserialize<AppSettings>(wnsJson) ?? new AppSettings();
    AssertEqual(
        MicrophoneProcessingMode.WindowsNoiseSuppression.ToString(),
        wnsSettings.ResolveMicrophoneProcessingMode().ToString(),
        "WindowsNoiseSuppression persists through JSON round-trip");

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
    where T : IEquatable<T>
{
    if (!actual.Equals(expected))
    {
        throw new InvalidOperationException($"{description}: expected '{expected}', actual '{actual}'.");
    }
}
