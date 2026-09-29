using NAudio.Lame;
using NAudio.Wave;

namespace SimpleAudioRecorder;

internal static class Mp3Utility
{
    private const int TargetBitRateKbps = 160;
    private const int MonoChunkBytes = 64 * 1024;
    private const int StereoFramesPerChunk = 8192;

    public static void WriteMonoMp3FromPcm(
        string pcmPath,
        string mp3Path,
        int sampleRate,
        bool isMicrophoneTrack = false)
    {
        var gainProfile = isMicrophoneTrack ? WavUtility.MicrophoneGainProfile : WavUtility.SystemGainProfile;
        var analysis = WavUtility.AnalyzePcm(pcmPath, gainProfile);
        var gain = WavUtility.CalculateGain(analysis, gainProfile);

        using var input = new BinaryReader(new FileStream(pcmPath, FileMode.Open, FileAccess.Read, FileShare.Read));
        using var output = new FileStream(mp3Path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var encoder = new LameMP3FileWriter(output, new WaveFormat(sampleRate, 16, 1), TargetBitRateKbps);

        var buffer = new byte[MonoChunkBytes];
        int bytesRead;
        while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            var usableBytes = bytesRead - (bytesRead & 1);
            for (var offset = 0; offset + 1 < usableBytes; offset += 2)
            {
                var sample = (short)(buffer[offset] | (buffer[offset + 1] << 8));
                var gained = WavUtility.ApplyGain(sample, gain);
                buffer[offset] = (byte)gained;
                buffer[offset + 1] = (byte)(gained >> 8);
            }

            if (usableBytes > 0)
            {
                encoder.Write(buffer, 0, usableBytes);
            }
        }

        AppLogger.Info(
            $"Mono MP3 written. File={mp3Path}; Track={(isMicrophoneTrack ? "Microphone" : "System")}; BitRate={TargetBitRateKbps} kbps; Gain={gain:0.00}x; Peak={analysis.PeakNormalized:0.000}; ActiveRms={analysis.ActiveRmsNormalized:0.000}; ActiveSamples={analysis.ActiveSampleCount}");
    }

    public static void WriteStereoMp3FromMonoPcm(
        string leftPcmPath,
        string rightPcmPath,
        string mp3Path,
        int sampleRate)
    {
        var leftAnalysis = WavUtility.AnalyzePcm(leftPcmPath, WavUtility.SystemGainProfile);
        var rightAnalysis = WavUtility.AnalyzePcm(rightPcmPath, WavUtility.MicrophoneGainProfile);
        var leftGain = WavUtility.CalculateGain(leftAnalysis, WavUtility.SystemGainProfile);
        var rightGain = WavUtility.CalculateGain(rightAnalysis, WavUtility.MicrophoneGainProfile);

        using var left = new BinaryReader(new FileStream(leftPcmPath, FileMode.Open, FileAccess.Read, FileShare.Read));
        using var right = new BinaryReader(new FileStream(rightPcmPath, FileMode.Open, FileAccess.Read, FileShare.Read));
        using var output = new FileStream(mp3Path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var encoder = new LameMP3FileWriter(output, new WaveFormat(sampleRate, 16, 2), TargetBitRateKbps);

        var leftFrames = left.BaseStream.Length / 2;
        var rightFrames = right.BaseStream.Length / 2;
        var totalFrames = Math.Max(leftFrames, rightFrames);

        var buffer = new byte[StereoFramesPerChunk * 4];
        for (var frameBase = 0L; frameBase < totalFrames; frameBase += StereoFramesPerChunk)
        {
            var frameCount = (int)Math.Min(StereoFramesPerChunk, totalFrames - frameBase);
            var offset = 0;
            for (var index = 0; index < frameCount; index++)
            {
                var frame = frameBase + index;
                var leftSample = frame < leftFrames ? WavUtility.ApplyGain(left.ReadInt16(), leftGain) : (short)0;
                var rightSample = frame < rightFrames ? WavUtility.ApplyGain(right.ReadInt16(), rightGain) : (short)0;
                buffer[offset++] = (byte)leftSample;
                buffer[offset++] = (byte)(leftSample >> 8);
                buffer[offset++] = (byte)rightSample;
                buffer[offset++] = (byte)(rightSample >> 8);
            }

            encoder.Write(buffer, 0, offset);
        }

        AppLogger.Info(
            $"Stereo MP3 written. File={mp3Path}; BitRate={TargetBitRateKbps} kbps; LeftGain={leftGain:0.00}x; RightGain={rightGain:0.00}x; LeftPeak={leftAnalysis.PeakNormalized:0.000}; RightPeak={rightAnalysis.PeakNormalized:0.000}; LeftActiveRms={leftAnalysis.ActiveRmsNormalized:0.000}; RightActiveRms={rightAnalysis.ActiveRmsNormalized:0.000}");
    }
}