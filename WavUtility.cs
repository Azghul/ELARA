namespace ELARA;

internal static class WavUtility
{
    internal static readonly GainProfile SystemGainProfile = new(
        TargetPeakLevel: 0.92F,
        TargetActiveRms: 0.18F,
        MaximumGain: 3.0F,
        ActivityThreshold: 0.015F);

    internal static readonly GainProfile MicrophoneGainProfile = new(
        TargetPeakLevel: 0.96F,
        TargetActiveRms: 0.24F,
        MaximumGain: 8.0F,
        ActivityThreshold: 0.008F);

    public static void WriteMonoWavFromPcm(
        string pcmPath,
        string wavPath,
        int sampleRate,
        short bitsPerSample,
        bool isMicrophoneTrack = false)
    {
        var gainProfile = isMicrophoneTrack ? MicrophoneGainProfile : SystemGainProfile;
        var analysis = AnalyzePcm(pcmPath, gainProfile);
        var gain = CalculateGain(analysis, gainProfile);
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
        AppLogger.Info(
            $"Mono WAV written. File={wavPath}; Track={(isMicrophoneTrack ? "Microphone" : "System")}; Gain={gain:0.00}x; Peak={analysis.PeakNormalized:0.000}; ActiveRms={analysis.ActiveRmsNormalized:0.000}; ActiveSamples={analysis.ActiveSampleCount}");
    }

    public static void WriteStereoWavFromMonoPcm(
        string leftPcmPath,
        string rightPcmPath,
        string wavPath,
        int sampleRate,
        short bitsPerSample)
    {
        var leftAnalysis = AnalyzePcm(leftPcmPath, SystemGainProfile);
        var rightAnalysis = AnalyzePcm(rightPcmPath, MicrophoneGainProfile);
        var leftGain = CalculateGain(leftAnalysis, SystemGainProfile);
        var rightGain = CalculateGain(rightAnalysis, MicrophoneGainProfile);

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
        AppLogger.Info(
            $"Stereo WAV written. File={wavPath}; LeftGain={leftGain:0.00}x; RightGain={rightGain:0.00}x; LeftPeak={leftAnalysis.PeakNormalized:0.000}; RightPeak={rightAnalysis.PeakNormalized:0.000}; LeftActiveRms={leftAnalysis.ActiveRmsNormalized:0.000}; RightActiveRms={rightAnalysis.ActiveRmsNormalized:0.000}");
    }

    internal static PcmAnalysis AnalyzePcm(string pcmPath, GainProfile gainProfile)
    {
        using var reader = new BinaryReader(new FileStream(pcmPath, FileMode.Open, FileAccess.Read, FileShare.Read));

        short peak = 0;
        double activeSumSquares = 0D;
        long activeSampleCount = 0;
        var threshold = (short)Math.Clamp(
            MathF.Round(short.MaxValue * gainProfile.ActivityThreshold),
            1F,
            short.MaxValue);

        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var sample = reader.ReadInt16();
            var amplitude = sample == short.MinValue ? short.MaxValue : (short)Math.Abs(sample);
            if (amplitude > peak)
            {
                peak = amplitude;
            }

            if (amplitude >= threshold)
            {
                var normalized = sample / (double)short.MaxValue;
                activeSumSquares += normalized * normalized;
                activeSampleCount++;
            }
        }

        var peakNormalized = peak / (float)short.MaxValue;
        var activeRmsNormalized = activeSampleCount > 0
            ? (float)Math.Sqrt(activeSumSquares / activeSampleCount)
            : 0F;

        return new PcmAnalysis(peakNormalized, activeRmsNormalized, activeSampleCount);
    }

    internal static float CalculateGain(PcmAnalysis analysis, GainProfile gainProfile)
    {
        if (analysis.PeakNormalized <= 0F)
        {
            return 1F;
        }

        var peakGain = gainProfile.TargetPeakLevel / analysis.PeakNormalized;
        var rmsGain = analysis.ActiveRmsNormalized > 0F
            ? gainProfile.TargetActiveRms / analysis.ActiveRmsNormalized
            : peakGain;

        var desiredGain = Math.Max(peakGain, rmsGain);
        return Math.Clamp(desiredGain, 1F, gainProfile.MaximumGain);
    }

    internal static short ApplyGain(short sample, float gain)
    {
        if (gain <= 1.001F)
        {
            return sample;
        }

        var normalized = sample / (float)short.MaxValue;
        var boosted = normalized * gain;

        // Gentle saturation keeps boosted mic tracks from sounding harsh when they hit the new gain ceiling.
        var shaped = MathF.Tanh(boosted) / MathF.Tanh(1.4F);
        var scaled = Math.Clamp(shaped, -1F, 1F) * short.MaxValue;
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

    internal readonly record struct GainProfile(
        float TargetPeakLevel,
        float TargetActiveRms,
        float MaximumGain,
        float ActivityThreshold);

    internal readonly record struct PcmAnalysis(
        float PeakNormalized,
        float ActiveRmsNormalized,
        long ActiveSampleCount);
}
