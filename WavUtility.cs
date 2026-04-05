namespace SimpleAudioRecorder;

internal static class WavUtility
{
    public static void WriteMonoWavFromPcm(
        string pcmPath,
        string wavPath,
        int sampleRate,
        short bitsPerSample)
    {
        using var input = new FileStream(pcmPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(wavPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(output);

        WriteWaveHeader(writer, channels: 1, sampleRate, bitsPerSample, input.Length);
        input.CopyTo(output);
        FinalizeWaveHeader(writer, channels: 1, sampleRate, bitsPerSample, input.Length);
    }

    public static void WriteStereoWavFromMonoPcm(
        string leftPcmPath,
        string rightPcmPath,
        string wavPath,
        int sampleRate,
        short bitsPerSample)
    {
        using var left = new BinaryReader(new FileStream(leftPcmPath, FileMode.Open, FileAccess.Read, FileShare.Read));
        using var right = new BinaryReader(new FileStream(rightPcmPath, FileMode.Open, FileAccess.Read, FileShare.Read));
        using var output = new FileStream(wavPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(output);

        var leftFrames = left.BaseStream.Length / 2;
        var rightFrames = right.BaseStream.Length / 2;
        var totalFrames = Math.Max(leftFrames, rightFrames);
        var dataLength = totalFrames * 4;

        WriteWaveHeader(writer, channels: 2, sampleRate, bitsPerSample, dataLength);

        for (long index = 0; index < totalFrames; index++)
        {
            var leftSample = index < leftFrames ? left.ReadInt16() : (short)0;
            var rightSample = index < rightFrames ? right.ReadInt16() : (short)0;
            writer.Write(leftSample);
            writer.Write(rightSample);
        }

        FinalizeWaveHeader(writer, channels: 2, sampleRate, bitsPerSample, dataLength);
    }

    private static void WriteWaveHeader(
        BinaryWriter writer,
        short channels,
        int sampleRate,
        short bitsPerSample,
        long dataLength)
    {
        writer.Write("RIFF"u8.ToArray());
        writer.Write((int)(36 + dataLength));
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * (bitsPerSample / 8));
        writer.Write((short)(channels * (bitsPerSample / 8)));
        writer.Write(bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write((int)dataLength);
    }

    private static void FinalizeWaveHeader(
        BinaryWriter writer,
        short channels,
        int sampleRate,
        short bitsPerSample,
        long dataLength)
    {
        writer.Flush();
        writer.Seek(4, SeekOrigin.Begin);
        writer.Write((int)(36 + dataLength));
        writer.Seek(22, SeekOrigin.Begin);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * (bitsPerSample / 8));
        writer.Write((short)(channels * (bitsPerSample / 8)));
        writer.Write(bitsPerSample);
        writer.Seek(40, SeekOrigin.Begin);
        writer.Write((int)dataLength);
        writer.Flush();
    }
}
