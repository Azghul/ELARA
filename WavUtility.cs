namespace ELARA;

internal static class WavUtility
{
    public static void WriteMonoWavFromPcm(
        string pcmPath,
        string wavPath,
        int sampleRate,
        short bitsPerSample,
        bool isMicrophoneTrack = false)
    {
        using var input = new BinaryReader(new FileStream(pcmPath, FileMode.Open, FileAccess.Read, FileShare.Read));
        using var output = new FileStream(wavPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(output);

        var inputLength = input.BaseStream.Length;
        WriteWaveHeader(writer, channels: 1, sampleRate, bitsPerSample, inputLength);

        while (input.BaseStream.Position < input.BaseStream.Length)
        {
            writer.Write(input.ReadInt16());
        }

        FinalizeWaveHeader(writer, channels: 1, sampleRate, bitsPerSample, inputLength);
        AppLogger.Info(
            $"Mono WAV written without gain or loudness processing. File={wavPath}; Track={(isMicrophoneTrack ? "Microphone" : "System")}");
    }

    public static void WriteMonoMixWavFromMonoPcm(
        string systemPcmPath,
        string microphonePcmPath,
        string wavPath,
        int sampleRate,
        short bitsPerSample,
        long? systemFirstQpcPosition,
        long? microphoneFirstQpcPosition)
    {
        using var system = new BinaryReader(new FileStream(systemPcmPath, FileMode.Open, FileAccess.Read, FileShare.Read));
        using var microphone = new BinaryReader(new FileStream(microphonePcmPath, FileMode.Open, FileAccess.Read, FileShare.Read));
        using var output = new FileStream(wavPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(output);

        var systemFrames = system.BaseStream.Length / 2;
        var microphoneFrames = microphone.BaseStream.Length / 2;
        var (systemStartOffset, microphoneStartOffset) = CalculateStartOffsets(
            systemFirstQpcPosition,
            microphoneFirstQpcPosition,
            sampleRate);
        var totalFrames = Math.Max(systemStartOffset + systemFrames, microphoneStartOffset + microphoneFrames);
        var dataLength = totalFrames * 2;

        WriteWaveHeader(writer, channels: 1, sampleRate, bitsPerSample, dataLength);

        for (long index = 0; index < totalFrames; index++)
        {
            var systemIndex = index - systemStartOffset;
            var microphoneIndex = index - microphoneStartOffset;
            var systemSample = systemIndex >= 0 && systemIndex < systemFrames ? system.ReadInt16() : (short)0;
            var microphoneSample = microphoneIndex >= 0 && microphoneIndex < microphoneFrames ? microphone.ReadInt16() : (short)0;
            writer.Write(MixToMono(systemSample, microphoneSample));
        }

        FinalizeWaveHeader(writer, channels: 1, sampleRate, bitsPerSample, dataLength);
        AppLogger.Info(
            $"Mono-mix WAV written without gain or loudness processing. File={wavPath}; Mix=System50Percent+Microphone50Percent; SystemStartOffsetFrames={systemStartOffset}; MicrophoneStartOffsetFrames={microphoneStartOffset}");
    }

    internal static (long SystemFrames, long MicrophoneFrames) CalculateStartOffsets(
        long? systemFirstQpcPosition,
        long? microphoneFirstQpcPosition,
        int sampleRate)
    {
        if (systemFirstQpcPosition is null || microphoneFirstQpcPosition is null)
        {
            AppLogger.Warn("Capture start timestamps were unavailable; mono-mix tracks will start without timestamp correction.");
            return (0, 0);
        }

        // WASAPI reports QPC packet timestamps in 100-nanosecond units.
        var delta100Nanoseconds = systemFirstQpcPosition.Value - microphoneFirstQpcPosition.Value;
        var deltaFrames = (long)Math.Round(
            delta100Nanoseconds * (double)sampleRate / 10_000_000D,
            MidpointRounding.AwayFromZero);

        return deltaFrames >= 0
            ? (deltaFrames, 0)
            : (0, -deltaFrames);
    }

    internal static short MixToMono(short systemSample, short microphoneSample)
    {
        var sum = (int)systemSample + microphoneSample;
        return (short)Math.Round(sum / 2D, MidpointRounding.AwayFromZero);
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
