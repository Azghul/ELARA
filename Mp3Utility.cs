using System.Buffers;
using System.Diagnostics;
using NAudio.Lame;
using NAudio.Wave;

namespace ELARA;

internal static class Mp3Utility
{
    private const int MonoChunkBytes = 64 * 1024;
    private const int SourceStreamBufferBytes = 64 * 1024;

    public static SaveTimings WriteMonoMp3FromPcm(
        string pcmPath,
        string mp3Path,
        int sampleRate,
        bool isMicrophoneTrack = false,
        SaveProgressReporter? reporter = null)
    {
        var timings = new SaveTimings();
        using var input = new FileStream(
            pcmPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            SourceStreamBufferBytes,
            FileOptions.SequentialScan);
        using var output = new FileStream(
            mp3Path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            SourceStreamBufferBytes,
            FileOptions.SequentialScan);
        using var encoder = new LameMP3FileWriter(output, new WaveFormat(sampleRate, 16, 1), RecordingOutputProfile.Mp3BitRateKbps);

        var dataLength = input.Length;
        var buffer = new byte[MonoChunkBytes];
        var encodeStopwatch = new Stopwatch();
        reporter?.Report(SaveStage.Encoding, 0, "Encoding MP3 audio…");
        long bytesProcessed = 0;
        int bytesRead;
        while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            var usableBytes = bytesRead - (bytesRead & 1);
            if (usableBytes > 0)
            {
                encodeStopwatch.Start();
                encoder.Write(buffer, 0, usableBytes);
                encodeStopwatch.Stop();
            }

            bytesProcessed += bytesRead;
            if (dataLength > 0)
            {
                reporter?.Report(
                    SaveStage.Encoding,
                    (int)Math.Min(99, bytesProcessed * 100 / dataLength),
                    "Encoding MP3 audio…");
            }
        }

        timings.Mp3Encoding = encodeStopwatch.Elapsed;
        AppLogger.Info(
            $"Mono MP3 written without gain or loudness processing. File={mp3Path}; Track={(isMicrophoneTrack ? "Microphone" : "System")}; Profile={RecordingOutputProfile.Mp3ShortDescription}");
        return timings;
    }

    public static SaveTimings WriteMonoMixMp3FromMonoPcm(
        string systemPcmPath,
        string microphonePcmPath,
        string mp3Path,
        int sampleRate,
        long? systemFirstQpcPosition,
        long? microphoneFirstQpcPosition,
        SaveProgressReporter? reporter = null,
        bool? systemHasSignal = null,
        bool? microphoneHasSignal = null)
    {
        var systemSignal = systemHasSignal ?? WavUtility.PcmFileHasSignal(systemPcmPath);
        var microphoneSignal = microphoneHasSignal ?? WavUtility.PcmFileHasSignal(microphonePcmPath);
        var plan = BothPcmMixer.CreatePlan(
            systemPcmPath,
            microphonePcmPath,
            sampleRate,
            systemFirstQpcPosition,
            microphoneFirstQpcPosition,
            systemSignal,
            microphoneSignal);

        var timings = new SaveTimings();
        using var mixer = new BothPcmMixer(plan, systemPcmPath, microphonePcmPath);
        using var output = new FileStream(
            mp3Path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            SourceStreamBufferBytes,
            FileOptions.SequentialScan);
        using var encoder = new LameMP3FileWriter(output, new WaveFormat(sampleRate, 16, 1), RecordingOutputProfile.Mp3BitRateKbps);

        var buffer = ArrayPool<byte>.Shared.Rent(BothPcmMixer.FramesPerBlock * sizeof(short));
        var mixStopwatch = new Stopwatch();
        var encodeStopwatch = new Stopwatch();
        try
        {
            reporter?.Report(SaveStage.Encoding, 0, "Mixing and encoding MP3 audio…");
            while (true)
            {
                mixStopwatch.Start();
                var byteCount = mixer.ReadBlock(buffer);
                mixStopwatch.Stop();
                if (byteCount == 0)
                {
                    break;
                }

                // The mixed PCM block goes straight into the existing LAME writer:
                // same 48 kHz / mono / 128 kbps profile, no resampling or DSP.
                encodeStopwatch.Start();
                encoder.Write(buffer, 0, byteCount);
                encodeStopwatch.Stop();

                if (plan.TotalFrames > 0)
                {
                    reporter?.Report(
                        SaveStage.Encoding,
                        (int)Math.Min(99, mixer.FramesProcessed * 100 / plan.TotalFrames),
                        "Mixing and encoding MP3 audio…");
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        timings.Mixing = mixStopwatch.Elapsed;
        timings.Mp3Encoding = encodeStopwatch.Elapsed;
        AppLogger.Info(
            $"Mono-mix MP3 written without gain or loudness processing. File={mp3Path}; Profile={RecordingOutputProfile.Mp3ShortDescription}; Mix={BothMixProfile.DescribeMixMode(systemSignal, microphoneSignal)}; SystemStartOffsetFrames={plan.SystemStartOffsetFrames}; MicrophoneStartOffsetFrames={plan.MicrophoneStartOffsetFrames}");
        return timings;
    }
}
