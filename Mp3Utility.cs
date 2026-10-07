using NAudio.Lame;
using NAudio.Wave;

namespace ELARA;

internal static class Mp3Utility
{
    private const int MonoChunkBytes = 64 * 1024;
    private const int MixFramesPerChunk = 8192;

    public static void WriteMonoMp3FromPcm(
        string pcmPath,
        string mp3Path,
        int sampleRate,
        bool isMicrophoneTrack = false)
    {
        using var input = new BinaryReader(new FileStream(pcmPath, FileMode.Open, FileAccess.Read, FileShare.Read));
        using var output = new FileStream(mp3Path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var encoder = new LameMP3FileWriter(output, new WaveFormat(sampleRate, 16, 1), RecordingOutputProfile.Mp3BitRateKbps);

        var buffer = new byte[MonoChunkBytes];
        int bytesRead;
        while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            var usableBytes = bytesRead - (bytesRead & 1);
            if (usableBytes > 0)
            {
                encoder.Write(buffer, 0, usableBytes);
            }
        }

        AppLogger.Info(
            $"Mono MP3 written without gain or loudness processing. File={mp3Path}; Track={(isMicrophoneTrack ? "Microphone" : "System")}; Profile={RecordingOutputProfile.Mp3ShortDescription}");
    }

    public static void WriteMonoMixMp3FromMonoPcm(
        string systemPcmPath,
        string microphonePcmPath,
        string mp3Path,
        int sampleRate,
        long? systemFirstQpcPosition,
        long? microphoneFirstQpcPosition)
    {
        var systemHasSignal = WavUtility.PcmFileHasSignal(systemPcmPath);
        var microphoneHasSignal = WavUtility.PcmFileHasSignal(microphonePcmPath);
        var (systemGain, microphoneGain) = BothMixProfile.SelectMixGains(systemHasSignal, microphoneHasSignal);

        using var system = new BinaryReader(new FileStream(systemPcmPath, FileMode.Open, FileAccess.Read, FileShare.Read));
        using var microphone = new BinaryReader(new FileStream(microphonePcmPath, FileMode.Open, FileAccess.Read, FileShare.Read));
        using var output = new FileStream(mp3Path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var encoder = new LameMP3FileWriter(output, new WaveFormat(sampleRate, 16, 1), RecordingOutputProfile.Mp3BitRateKbps);

        var systemFrames = system.BaseStream.Length / 2;
        var microphoneFrames = microphone.BaseStream.Length / 2;
        var (systemStartOffset, microphoneStartOffset) = WavUtility.CalculateStartOffsets(
            systemFirstQpcPosition,
            microphoneFirstQpcPosition,
            sampleRate);
        var totalFrames = Math.Max(systemStartOffset + systemFrames, microphoneStartOffset + microphoneFrames);

        var buffer = new byte[MixFramesPerChunk * 2];
        for (var frameBase = 0L; frameBase < totalFrames; frameBase += MixFramesPerChunk)
        {
            var frameCount = (int)Math.Min(MixFramesPerChunk, totalFrames - frameBase);
            var offset = 0;
            for (var index = 0; index < frameCount; index++)
            {
                var frame = frameBase + index;
                var systemIndex = frame - systemStartOffset;
                var microphoneIndex = frame - microphoneStartOffset;
                var systemSample = systemIndex >= 0 && systemIndex < systemFrames ? system.ReadInt16() : (short)0;
                var microphoneSample = microphoneIndex >= 0 && microphoneIndex < microphoneFrames ? microphone.ReadInt16() : (short)0;
                var mixedSample = BothMixProfile.MixFrame(systemSample, microphoneSample, systemGain, microphoneGain);
                buffer[offset++] = (byte)mixedSample;
                buffer[offset++] = (byte)(mixedSample >> 8);
            }

            encoder.Write(buffer, 0, offset);
        }

        AppLogger.Info(
            $"Mono-mix MP3 written without gain or loudness processing. File={mp3Path}; Profile={RecordingOutputProfile.Mp3ShortDescription}; Mix={BothMixProfile.DescribeMixMode(systemHasSignal, microphoneHasSignal)}; SystemStartOffsetFrames={systemStartOffset}; MicrophoneStartOffsetFrames={microphoneStartOffset}");
    }
}
