namespace SimpleAudioRecorder;

internal static class WavUtility
{
    private const float TargetPeakLevel = 0.92F;
    private const float MaximumAutoGain = 3.0F;

    public static void WriteMonoWavFromPcm(
        string pcmPath,
        string wavPath,
        int sampleRate,
        short bitsPerSample)
    {
        var gain = CalculateGain(pcmPath);
        using var input = new BinaryReader(new FileStream(pcmPath, FileMode.Open, FileAccess.Read, FileShare.Read));
        using var output = new FileStream(wavPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(output);

        var inputLength = input.BaseStream.Length;
        WriteWaveHeader(writer, channels: 1, sampleRate, bitsPerSample, inputLength);

        while (input.BaseStream.Position < input.BaseStream.Length)
        {
            writer.Write(ApplyGain(input.ReadInt16(), gain));
        }

        FinalizeWaveHeader(writer, channels: 1, sampleRate, bitsPerSample, inputLength);
        AppLogger.Info($"Mono WAV written. File={wavPath}; Gain={gain:0.00}x");
    }

    public static void WriteStereoWavFromMonoPcm(
        string leftPcmPath,
        string rightPcmPath,
        string wavPath,
        int sampleRate,
        short bitsPerSample)
    {
        var leftGain = CalculateGain(leftPcmPath);
        var rightGain = CalculateGain(rightPcmPath);

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
            var leftSample = index < leftFrames ? ApplyGain(left.ReadInt16(), leftGain) : (short)0;
            var rightSample = index < rightFrames ? ApplyGain(right.ReadInt16(), rightGain) : (short)0;
            writer.Write(leftSample);
            writer.Write(rightSample);
        }

        FinalizeWaveHeader(writer, channels: 2, sampleRate, bitsPerSample, dataLength);
        AppLogger.Info($"Stereo WAV written. File={wavPath}; LeftGain={leftGain:0.00}x; RightGain={rightGain:0.00}x");
    }

    private static float CalculateGain(string pcmPath)
    {
        using var reader = new BinaryReader(new FileStream(pcmPath, FileMode.Open, FileAccess.Read, FileShare.Read));

        short peak = 0;
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var sample = reader.ReadInt16();
            var amplitude = sample == short.MinValue ? short.MaxValue : (short)Math.Abs(sample);
            if (amplitude > peak)
            {
                peak = amplitude;
            }
        }

        if (peak <= 0)
        {
            return 1F;
        }

        var normalizedPeak = peak / (float)short.MaxValue;
        if (normalizedPeak >= TargetPeakLevel)
        {
            return 1F;
        }

        return Math.Min(MaximumAutoGain, TargetPeakLevel / normalizedPeak);
    }

    private static short ApplyGain(short sample, float gain)
    {
        if (gain <= 1.001F)
        {
            return sample;
        }

        var scaled = sample * gain;
        return (short)Math.Clamp(MathF.Round(scaled), short.MinValue, short.MaxValue);
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
