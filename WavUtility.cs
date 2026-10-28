using System.Buffers;
using System.Diagnostics;

namespace ELARA;

internal static class WavUtility
{
    /// <summary>Largest PCM data length representable in a standard RIFF/WAV header.</summary>
    private const long MaximumWavDataLengthBytes = int.MaxValue - 8;
    private const int CopyBufferBytes = 1024 * 1024;
    private const int SignalScanBufferBytes = 64 * 1024;

    public static SaveTimings WriteMonoWavFromPcm(
        string pcmPath,
        string wavPath,
        int sampleRate,
        short bitsPerSample,
        bool isMicrophoneTrack = false,
        SaveProgressReporter? reporter = null)
    {
        var timings = new SaveTimings();
        using var input = new FileStream(
            pcmPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferBytes,
            FileOptions.SequentialScan);
        var dataLength = input.Length;
        if (dataLength > MaximumWavDataLengthBytes)
        {
            // Never overflow the RIFF header for multi-hour recordings; the caller
            // rescues the recording as MP3 instead of writing a broken WAV.
            throw new InvalidOperationException(
                $"The recording is too long for the WAV (RIFF) format ({dataLength} bytes of PCM data). Use the MP3 output format for such long sessions.");
        }

        using var output = new FileStream(
            wavPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            CopyBufferBytes,
            FileOptions.SequentialScan);
        using var writer = new BinaryWriter(output);

        WriteWaveHeader(writer, channels: 1, sampleRate, bitsPerSample, dataLength);

        // The temporary file already holds canonical PCM16 mono samples, so the
        // bytes can be copied verbatim. No per-sample ReadInt16/Write(short).
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferBytes);
        var writeStopwatch = new Stopwatch();
        try
        {
            reporter?.Report(SaveStage.Encoding, 0, "Writing WAV audio…");
            long bytesCopied = 0;
            int bytesRead;
            while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                writeStopwatch.Start();
                output.Write(buffer, 0, bytesRead);
                writeStopwatch.Stop();
                bytesCopied += bytesRead;
                if (dataLength > 0)
                {
                    reporter?.Report(
                        SaveStage.Encoding,
                        (int)Math.Min(99, bytesCopied * 100 / dataLength),
                        "Writing WAV audio…");
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        timings.WavWriting = writeStopwatch.Elapsed;
        FinalizeWaveHeader(writer, channels: 1, sampleRate, bitsPerSample, dataLength);
        AppLogger.Info(
            $"Mono WAV written without gain or loudness processing. File={wavPath}; Track={(isMicrophoneTrack ? "Microphone" : "System")}");
        return timings;
    }

    public static SaveTimings WriteMonoMixWavFromMonoPcm(
        string systemPcmPath,
        string microphonePcmPath,
        string wavPath,
        int sampleRate,
        short bitsPerSample,
        long? systemFirstQpcPosition,
        long? microphoneFirstQpcPosition,
        SaveProgressReporter? reporter = null,
        bool? systemHasSignal = null,
        bool? microphoneHasSignal = null)
    {
        var systemSignal = systemHasSignal ?? PcmFileHasSignal(systemPcmPath);
        var microphoneSignal = microphoneHasSignal ?? PcmFileHasSignal(microphonePcmPath);
        var plan = BothPcmMixer.CreatePlan(
            systemPcmPath,
            microphonePcmPath,
            sampleRate,
            systemFirstQpcPosition,
            microphoneFirstQpcPosition,
            systemSignal,
            microphoneSignal);
        var dataLength = plan.TotalFrames * sizeof(short);
        if (dataLength > MaximumWavDataLengthBytes)
        {
            throw new InvalidOperationException(
                $"The recording is too long for the WAV (RIFF) format ({dataLength} bytes of PCM data). Use the MP3 output format for such long sessions.");
        }

        var timings = new SaveTimings();
        using var mixer = new BothPcmMixer(plan, systemPcmPath, microphonePcmPath);
        using var output = new FileStream(
            wavPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            CopyBufferBytes,
            FileOptions.SequentialScan);
        using var writer = new BinaryWriter(output);

        WriteWaveHeader(writer, channels: 1, sampleRate, bitsPerSample, dataLength);

        var buffer = ArrayPool<byte>.Shared.Rent(BothPcmMixer.FramesPerBlock * sizeof(short));
        var mixStopwatch = new Stopwatch();
        var writeStopwatch = new Stopwatch();
        try
        {
            reporter?.Report(SaveStage.Mixing, 0, "Mixing system audio and microphone…");
            while (true)
            {
                mixStopwatch.Start();
                var byteCount = mixer.ReadBlock(buffer);
                mixStopwatch.Stop();
                if (byteCount == 0)
                {
                    break;
                }

                writeStopwatch.Start();
                output.Write(buffer, 0, byteCount);
                writeStopwatch.Stop();

                if (plan.TotalFrames > 0)
                {
                    reporter?.Report(
                        SaveStage.Mixing,
                        (int)Math.Min(99, mixer.FramesProcessed * 100 / plan.TotalFrames),
                        "Mixing system audio and microphone…");
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        timings.Mixing = mixStopwatch.Elapsed;
        timings.WavWriting = writeStopwatch.Elapsed;
        FinalizeWaveHeader(writer, channels: 1, sampleRate, bitsPerSample, dataLength);
        AppLogger.Info(
            $"Mono-mix WAV written without gain or loudness processing. File={wavPath}; Mix={BothMixProfile.DescribeMixMode(systemSignal, microphoneSignal)}; SystemStartOffsetFrames={plan.SystemStartOffsetFrames}; MicrophoneStartOffsetFrames={plan.MicrophoneStartOffsetFrames}");
        return timings;
    }

    /// <summary>
    /// Streaming signal check for a raw PCM16 file: true when it contains at
    /// least one non-zero sample. The file is scanned in blocks; a complete
    /// little-endian sample is non-silent when either of its bytes is non-zero.
    /// </summary>
    internal static bool PcmFileHasSignal(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        using var input = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            SignalScanBufferBytes,
            FileOptions.SequentialScan);
        var buffer = ArrayPool<byte>.Shared.Rent(SignalScanBufferBytes);
        var hasCarryByte = false;
        byte carryByte = 0;
        try
        {
            int bytesRead;
            while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                var index = 0;
                if (hasCarryByte)
                {
                    // Complete the sample that straddled the previous block.
                    if ((carryByte | buffer[0]) != 0)
                    {
                        return true;
                    }

                    hasCarryByte = false;
                    index = 1;
                }

                for (; index + 1 < bytesRead; index += sizeof(short))
                {
                    if (buffer[index] != 0 || buffer[index + 1] != 0)
                    {
                        return true;
                    }
                }

                if (index < bytesRead)
                {
                    carryByte = buffer[index];
                    hasCarryByte = true;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
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
