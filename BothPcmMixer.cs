using System.Buffers.Binary;

namespace ELARA;

/// <summary>Static, precomputed Both-mode mix parameters for one recording.</summary>
internal readonly record struct BothMixPlan(
    long SystemFrames,
    long MicrophoneFrames,
    long SystemStartOffsetFrames,
    long MicrophoneStartOffsetFrames,
    long TotalFrames,
    float SystemGain,
    float MicrophoneGain);

/// <summary>
/// Block-wise PCM16 reader/mixer for Both mode. It only changes the I/O
/// strategy (block reads instead of one BinaryReader.ReadInt16 per frame).
/// Every frame is still mixed through <see cref="BothMixProfile.MixFrame"/>,
/// so QPC alignment, gain selection, rounding and sample values stay identical.
/// </summary>
internal sealed class BothPcmMixer : IDisposable
{
    public const int FramesPerBlock = 32768;
    private const int SourceStreamBufferBytes = 64 * 1024;

    private readonly BothMixPlan plan;
    private readonly FileStream system;
    private readonly FileStream microphone;
    private readonly byte[] systemBuffer = new byte[FramesPerBlock * sizeof(short)];
    private readonly byte[] microphoneBuffer = new byte[FramesPerBlock * sizeof(short)];

    public BothPcmMixer(BothMixPlan plan, string systemPcmPath, string microphonePcmPath)
    {
        this.plan = plan;
        system = new FileStream(
            systemPcmPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            SourceStreamBufferBytes,
            FileOptions.SequentialScan);
        microphone = new FileStream(
            microphonePcmPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            SourceStreamBufferBytes,
            FileOptions.SequentialScan);
    }

    public long TotalFrames => plan.TotalFrames;

    public long FramesProcessed { get; private set; }

    public static BothMixPlan CreatePlan(
        string systemPcmPath,
        string microphonePcmPath,
        int sampleRate,
        long? systemFirstQpcPosition,
        long? microphoneFirstQpcPosition,
        bool systemHasSignal,
        bool microphoneHasSignal)
    {
        var systemFrames = new FileInfo(systemPcmPath).Length / sizeof(short);
        var microphoneFrames = new FileInfo(microphonePcmPath).Length / sizeof(short);
        var (systemStartOffset, microphoneStartOffset) = WavUtility.CalculateStartOffsets(
            systemFirstQpcPosition,
            microphoneFirstQpcPosition,
            sampleRate);
        var totalFrames = Math.Max(systemStartOffset + systemFrames, microphoneStartOffset + microphoneFrames);
        var (systemGain, microphoneGain) = BothMixProfile.SelectMixGains(systemHasSignal, microphoneHasSignal);

        return new BothMixPlan(
            systemFrames,
            microphoneFrames,
            systemStartOffset,
            microphoneStartOffset,
            totalFrames,
            systemGain,
            microphoneGain);
    }

    /// <summary>
    /// Fills <paramref name="output"/> with the next mixed PCM16 block and
    /// returns the number of bytes written (0 at the end of the recording).
    /// </summary>
    public int ReadBlock(byte[] output)
    {
        var remainingFrames = plan.TotalFrames - FramesProcessed;
        if (remainingFrames <= 0)
        {
            return 0;
        }

        var frameCount = (int)Math.Min(FramesPerBlock, remainingFrames);
        var byteCount = frameCount * sizeof(short);
        if (output.Length < byteCount)
        {
            throw new ArgumentException($"The output buffer must hold at least {byteCount} bytes.", nameof(output));
        }

        var frameBase = FramesProcessed;
        Array.Clear(systemBuffer, 0, byteCount);
        Array.Clear(microphoneBuffer, 0, byteCount);

        ReadSourceBlock(
            system,
            frameBase,
            frameCount,
            plan.SystemStartOffsetFrames,
            plan.SystemFrames,
            systemBuffer);
        ReadSourceBlock(
            microphone,
            frameBase,
            frameCount,
            plan.MicrophoneStartOffsetFrames,
            plan.MicrophoneFrames,
            microphoneBuffer);

        for (var index = 0; index < frameCount; index++)
        {
            var byteOffset = index * sizeof(short);
            var systemSample = BinaryPrimitives.ReadInt16LittleEndian(systemBuffer.AsSpan(byteOffset, sizeof(short)));
            var microphoneSample = BinaryPrimitives.ReadInt16LittleEndian(microphoneBuffer.AsSpan(byteOffset, sizeof(short)));
            var mixedSample = BothMixProfile.MixFrame(
                systemSample,
                microphoneSample,
                plan.SystemGain,
                plan.MicrophoneGain);
            BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(byteOffset, sizeof(short)), mixedSample);
        }

        FramesProcessed += frameCount;
        return byteCount;
    }

    private static void ReadSourceBlock(
        FileStream stream,
        long frameBase,
        int frameCount,
        long startOffsetFrames,
        long sourceFrames,
        byte[] buffer)
    {
        var validStart = Math.Max(frameBase, startOffsetFrames);
        var validEnd = Math.Min(frameBase + frameCount, startOffsetFrames + sourceFrames);
        if (validEnd <= validStart)
        {
            return;
        }

        var sourceByteOffset = (validStart - startOffsetFrames) * sizeof(short);
        var destinationByteOffset = (int)((validStart - frameBase) * sizeof(short));
        var byteCount = (int)((validEnd - validStart) * sizeof(short));

        if (stream.Position != sourceByteOffset)
        {
            stream.Position = sourceByteOffset;
        }

        stream.ReadExactly(buffer, destinationByteOffset, byteCount);
    }

    public void Dispose()
    {
        system.Dispose();
        microphone.Dispose();
    }
}
