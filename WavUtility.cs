namespace ELARA;

internal static class WavUtility
{
    /// <summary>Largest PCM data length representable in a standard RIFF/WAV header.</summary>
    private const long MaximumWavDataLengthBytes = int.MaxValue - 8;

    public static void WriteMonoWavFromPcm(
        string pcmPath,
        string wavPath,
        int sampleRate,
        short bitsPerSample,
        bool isMicrophoneTrack = false)
    {
        using var input = new BinaryReader(new FileStream(pcmPath, FileMode.Open, FileAccess.Read, FileShare.Read));
        var dataLength = input.BaseStream.Length;
        if (dataLength > MaximumWavDataLengthBytes)
        {
            // Never overflow the RIFF header for multi-hour recordings; the caller
            // rescues the recording as MP3 instead of writing a broken WAV.
            throw new InvalidOperationException(
                $"The recording is too long for the WAV (RIFF) format ({dataLength} bytes of PCM data). Use the MP3 output format for such long sessions.");
        }

        using var output = new FileStream(wavPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(output);

        WriteWaveHeader(writer, channels: 1, sampleRate, bitsPerSample, dataLength);

        while (input.BaseStream.Position < input.BaseStream.Length)
        {
            writer.Write(input.ReadInt16());
        }

        FinalizeWaveHeader(writer, channels: 1, sampleRate, bitsPerSample, dataLength);
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
        var systemHasSignal = PcmFileHasSignal(systemPcmPath);
        var microphoneHasSignal = PcmFileHasSignal(microphonePcmPath);
        var (systemGain, microphoneGain) = BothMixProfile.SelectMixGains(systemHasSignal, microphoneHasSignal);

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
        if (dataLength > MaximumWavDataLengthBytes)
        {
            throw new InvalidOperationException(
                $"The recording is too long for the WAV (RIFF) format ({dataLength} bytes of PCM data). Use the MP3 output format for such long sessions.");
        }

        WriteWaveHeader(writer, channels: 1, sampleRate, bitsPerSample, dataLength);

        for (long index = 0; index < totalFrames; index++)
        {
            var systemIndex = index - systemStartOffset;
            var microphoneIndex = index - microphoneStartOffset;
            var systemSample = systemIndex >= 0 && systemIndex < systemFrames ? system.ReadInt16() : (short)0;
            var microphoneSample = microphoneIndex >= 0 && microphoneIndex < microphoneFrames ? microphone.ReadInt16() : (short)0;
            writer.Write(BothMixProfile.MixFrame(systemSample, microphoneSample, systemGain, microphoneGain));
        }

        FinalizeWaveHeader(writer, channels: 1, sampleRate, bitsPerSample, dataLength);
        AppLogger.Info(
            $"Mono-mix WAV written without gain or loudness processing. File={wavPath}; Mix={BothMixProfile.DescribeMixMode(systemHasSignal, microphoneHasSignal)}; SystemStartOffsetFrames={systemStartOffset}; MicrophoneStartOffsetFrames={microphoneStartOffset}");
    }

    /// <summary>
    /// Streaming signal check for a raw PCM16 file: true when it contains at
    /// least one non-zero sample. Reads sequentially, so multi-hour temp files
    /// never need to be buffered in memory.
    /// </summary>
    internal static bool PcmFileHasSignal(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        using var input = new BinaryReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
        while (input.BaseStream.Position + 1 < input.BaseStream.Length)
        {
            if (input.ReadInt16() != 0)
            {
                return true;
            }
        }

        return false;
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
