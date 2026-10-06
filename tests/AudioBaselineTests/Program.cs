using ELARA;

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
