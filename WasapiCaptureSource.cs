using System.Buffers;
using System.Runtime.InteropServices;

namespace SimpleAudioRecorder;

internal enum WasapiCaptureKind
{
    Microphone,
    SystemLoopback,
}

internal sealed class WasapiCaptureSource : IDisposable
{
    private readonly string friendlyName;
    private readonly WasapiCaptureKind kind;
    private readonly string? preferredDeviceId;
    private readonly string tempFilePath;
    private readonly int sampleRate;
    private readonly short bitsPerSample;
    private readonly short requestedChannels;
    private readonly Action<float> reportPeak;
    private readonly ManualResetEventSlim startSignal = new(false);
    private readonly ManualResetEventSlim stopSignal = new(false);

    private Thread? workerThread;
    private Exception? startupException;
    private Exception? backgroundException;
    private string? resolvedDeviceId;
    private string? resolvedDeviceName;

    public WasapiCaptureSource(
        string friendlyName,
        WasapiCaptureKind kind,
        string? preferredDeviceId,
        string tempFilePath,
        int sampleRate,
        short bitsPerSample,
        short requestedChannels,
        Action<float> reportPeak)
    {
        this.friendlyName = friendlyName;
        this.kind = kind;
        this.preferredDeviceId = preferredDeviceId;
        this.tempFilePath = tempFilePath;
        this.sampleRate = sampleRate;
        this.bitsPerSample = bitsPerSample;
        this.requestedChannels = requestedChannels;
        this.reportPeak = reportPeak;
    }

    public void Start()
    {
        if (workerThread is not null)
        {
            throw new InvalidOperationException($"{friendlyName} capture has already been started.");
        }

        startupException = null;
        backgroundException = null;
        resolvedDeviceId = null;
        resolvedDeviceName = null;
        workerThread = new Thread(CaptureThreadProc)
        {
            IsBackground = true,
            Name = $"SimpleAudioRecorder-{friendlyName}",
        };
        AppLogger.Info($"{friendlyName} capture thread starting. PreferredDeviceId={(preferredDeviceId ?? "<default>")}; TempFile={tempFilePath}");
        workerThread.Start();

        if (!startSignal.Wait(TimeSpan.FromSeconds(8)))
        {
            throw new TimeoutException($"Timed out while starting {friendlyName} capture.");
        }

        if (startupException is not null)
        {
            throw startupException;
        }
    }

    public string? ResolvedDeviceId => resolvedDeviceId;

    public string? ResolvedDeviceName => resolvedDeviceName;

    public void Stop()
    {
        if (workerThread is null)
        {
            return;
        }

        stopSignal.Set();

        if (!workerThread.Join(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException($"Timed out while stopping {friendlyName} capture.");
        }

        workerThread = null;

        if (backgroundException is not null)
        {
            throw new InvalidOperationException(
                $"{friendlyName} capture stopped unexpectedly. {backgroundException.Message}",
                backgroundException);
        }
    }

    public void Dispose()
    {
        try
        {
            Stop();
        }
        catch
        {
        }

        startSignal.Dispose();
        stopSignal.Dispose();
    }

    private void CaptureThreadProc()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioClient? audioClient = null;
        IAudioCaptureClient? captureClient = null;
        FileStream? fileStream = null;
        BinaryWriter? writer = null;

        try
        {
            using var _ = CoreAudioInterop.EnterComScope(CoreAudioInterop.COINIT_MULTITHREADED);

            enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(
                typeof(MMDeviceEnumeratorComObject))!;
            device = ResolveDevice(enumerator);
            resolvedDeviceId = device.GetId();
            resolvedDeviceName = AudioInputDeviceCatalog.GetFriendlyName(device);
            AppLogger.Info($"{friendlyName} capture using endpoint '{resolvedDeviceName}' [{resolvedDeviceId}]");

            var audioClientGuid = typeof(IAudioClient).GUID;
            device.Activate(ref audioClientGuid, CLSCTX.All, IntPtr.Zero, out var audioClientObject);
            audioClient = (IAudioClient)audioClientObject;

            var format = WaveFormatEx.CreatePcm(sampleRate, requestedChannels, bitsPerSample);
            var flags = AudioClientStreamFlags.AutoConvertPcm
                | AudioClientStreamFlags.SourceDefaultQuality
                | AudioClientStreamFlags.NoPersist;

            if (kind == WasapiCaptureKind.SystemLoopback)
            {
                flags |= AudioClientStreamFlags.Loopback;
            }

            audioClient.Initialize(AudioClientShareMode.Shared, flags, 2_000_000, 0, ref format, IntPtr.Zero);

            var captureClientGuid = typeof(IAudioCaptureClient).GUID;
            audioClient.GetService(ref captureClientGuid, out var captureClientObject);
            captureClient = (IAudioCaptureClient)captureClientObject;

            fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            writer = new BinaryWriter(fileStream);

            audioClient.Start();
            AppLogger.Info($"{friendlyName} capture started successfully.");
            startSignal.Set();

            while (!stopSignal.IsSet)
            {
                ReadAvailablePackets(captureClient, writer, (short)format.Channels, (short)format.BitsPerSample);
                stopSignal.Wait(6);
            }

            ReadAvailablePackets(captureClient, writer, (short)format.Channels, (short)format.BitsPerSample);
            audioClient.Stop();
            AppLogger.Info($"{friendlyName} capture stopped cleanly.");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"{friendlyName} capture failed.", ex);
            if (!startSignal.IsSet)
            {
                startupException = new InvalidOperationException(
                    $"Could not start {friendlyName} capture. {ex.Message}",
                    ex);
                startSignal.Set();
            }
            else
            {
                backgroundException = ex;
            }
        }
        finally
        {
            writer?.Dispose();
            fileStream?.Dispose();

            if (audioClient is not null)
            {
                try
                {
                    audioClient.Stop();
                }
                catch
                {
                }
            }

            ReleaseComObject(captureClient);
            ReleaseComObject(audioClient);
            ReleaseComObject(device);
            ReleaseComObject(enumerator);

            if (!startSignal.IsSet)
            {
                startSignal.Set();
            }
        }
    }

    private IMMDevice ResolveDevice(IMMDeviceEnumerator enumerator)
    {
        if (kind == WasapiCaptureKind.SystemLoopback)
        {
            return enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia);
        }

        if (!string.IsNullOrWhiteSpace(preferredDeviceId))
        {
            try
            {
                return enumerator.GetDevice(preferredDeviceId);
            }
            catch (COMException ex)
            {
                throw new InvalidOperationException(
                    "The selected microphone is no longer available. Right-click the app and choose a different microphone.",
                    ex);
            }
        }

        return enumerator.GetDefaultAudioEndpoint(EDataFlow.Capture, ERole.Multimedia);
    }

    private void ReadAvailablePackets(IAudioCaptureClient captureClient, BinaryWriter writer, short channels, short bitsPerSample)
    {
        while (true)
        {
            captureClient.GetNextPacketSize(out var nextPacketSize);
            if (nextPacketSize == 0)
            {
                return;
            }

            captureClient.GetBuffer(
                out var dataPointer,
                out var frameCount,
                out var flags,
                out _,
                out _);

            try
            {
                WritePacket(writer, dataPointer, frameCount, flags, channels, bitsPerSample);
            }
            finally
            {
                captureClient.ReleaseBuffer(frameCount);
            }
        }
    }

    private void WritePacket(
        BinaryWriter writer,
        IntPtr dataPointer,
        int frameCount,
        AudioClientBufferFlags flags,
        short channels,
        short bitsPerSample)
    {
        if (bitsPerSample != 16)
        {
            throw new NotSupportedException("Only 16-bit PCM capture is supported in this build.");
        }

        if ((flags & AudioClientBufferFlags.Silent) != 0 || dataPointer == IntPtr.Zero)
        {
            WriteSilence(writer, frameCount);
            reportPeak(0F);
            return;
        }

        var bytesPerFrame = channels * (bitsPerSample / 8);
        var bytesToCopy = frameCount * bytesPerFrame;
        var buffer = ArrayPool<byte>.Shared.Rent(bytesToCopy);

        try
        {
            Marshal.Copy(dataPointer, buffer, 0, bytesToCopy);

            float peak = 0F;
            for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                var frameOffset = frameIndex * bytesPerFrame;
                short mixedSample;

                if (channels <= 1)
                {
                    mixedSample = ReadInt16(buffer, frameOffset);
                }
                else
                {
                    var left = ReadInt16(buffer, frameOffset);
                    var right = ReadInt16(buffer, frameOffset + 2);
                    mixedSample = (short)((left + right) / 2);
                }

                var level = Math.Abs(mixedSample) / 32768F;
                if (level > peak)
                {
                    peak = level;
                }

                writer.Write(mixedSample);
            }

            reportPeak(peak);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void WriteSilence(BinaryWriter writer, int frameCount)
    {
        for (var index = 0; index < frameCount; index++)
        {
            writer.Write((short)0);
        }
    }

    private static short ReadInt16(byte[] buffer, int offset)
    {
        return (short)(buffer[offset] | (buffer[offset + 1] << 8));
    }

    private static void ReleaseComObject(object? comObject)
    {
        CoreAudioInterop.ReleaseComObject(comObject);
    }
}
