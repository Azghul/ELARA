using System.Buffers;
using System.Runtime.InteropServices;

namespace ELARA;

internal enum WasapiCaptureKind
{
    Microphone,
    SystemLoopback,
}

/// <summary>
/// The audio format the WASAPI audio client actually initialized for a capture
/// source. Exposed so the UI diagnostics can show the real input format instead
/// of only the requested one (e.g. the 16-bit PCM fallback when float capture is
/// rejected by an endpoint).
/// </summary>
public readonly record struct CapturedStreamFormat(
    ushort FormatTag,
    int SampleRate,
    short Channels,
    short BitsPerSample)
{
    public string Description =>
        $"{FormatTagDescription} · {SampleRate} Hz · {Channels} channel{(Channels == 1 ? string.Empty : "s")} · {BitsPerSample}-bit";

    private string FormatTagDescription => AudioFormatText.ForTagAndBits(FormatTag, (ushort)BitsPerSample);
}

internal sealed class CaptureSessionCoordinator : IDisposable
{
    private readonly CountdownEvent preparationBarrier;
    private readonly ManualResetEventSlim startSignal = new(false);
    private readonly ManualResetEventSlim stopSignal = new(false);

    public CaptureSessionCoordinator(int sourceCount)
    {
        preparationBarrier = new CountdownEvent(sourceCount);
    }

    public bool StopRequested => stopSignal.IsSet;

    public void SignalPrepared() => preparationBarrier.Signal();

    public bool WaitUntilPrepared(TimeSpan timeout) => preparationBarrier.Wait(timeout);

    public void ReleaseStart() => startSignal.Set();

    public bool WaitForStart()
    {
        var signaled = WaitHandle.WaitAny(new[] { startSignal.WaitHandle, stopSignal.WaitHandle });
        return signaled == 0 && !stopSignal.IsSet;
    }

    public void RequestStop() => stopSignal.Set();

    public bool WaitForStop(int millisecondsTimeout) => stopSignal.Wait(millisecondsTimeout);

    public void Dispose()
    {
        preparationBarrier.Dispose();
        startSignal.Dispose();
        stopSignal.Dispose();
    }
}

internal sealed class WasapiCaptureSource : IDisposable
{
    private static readonly Guid AcousticEchoCancellationEffectId = new("6f64adbe-8211-11e2-8c70-2c27d7f001fa");
    private static readonly Guid NoiseSuppressionEffectId = new("6f64adbf-8211-11e2-8c70-2c27d7f001fa");
    private static readonly Guid AutomaticGainControlEffectId = new("6f64adc0-8211-11e2-8c70-2c27d7f001fa");
    private static readonly Guid DeepNoiseSuppressionEffectId = new("6f64add0-8211-11e2-8c70-2c27d7f001fa");

    private readonly string friendlyName;
    private readonly WasapiCaptureKind kind;
    private readonly string? preferredDeviceId;
    private readonly string tempFilePath;
    private readonly int sampleRate;
    private readonly short bitsPerSample;
    private readonly short requestedChannels;
    private readonly Action<float> reportPeak;
    private readonly CaptureSessionCoordinator coordinator;
    private readonly ManualResetEventSlim startSignal = new(false);

    private Thread? workerThread;
    private Exception? startupException;
    private Exception? backgroundException;
    private string? resolvedDeviceId;
    private string? resolvedDeviceName;
    private CapturedStreamFormat? resolvedFormat;
    private CapturedStreamFormat? requestedCaptureFormat;
    private string? nativeMixFormatDescription;
    private bool rawActivated;
    private long discontinuityCount;
    private long timestampErrorCount;
    private long silentPacketCount;

    public WasapiCaptureSource(
        string friendlyName,
        WasapiCaptureKind kind,
        string? preferredDeviceId,
        string tempFilePath,
        int sampleRate,
        short bitsPerSample,
        short requestedChannels,
        Action<float> reportPeak,
        CaptureSessionCoordinator coordinator)
    {
        this.friendlyName = friendlyName;
        this.kind = kind;
        this.preferredDeviceId = preferredDeviceId;
        this.tempFilePath = tempFilePath;
        this.sampleRate = sampleRate;
        this.bitsPerSample = bitsPerSample;
        this.requestedChannels = requestedChannels;
        this.reportPeak = reportPeak;
        this.coordinator = coordinator;
    }

    public void Prepare()
    {
        if (workerThread is not null)
        {
            throw new InvalidOperationException($"{friendlyName} capture has already been started.");
        }

        startupException = null;
        backgroundException = null;
        resolvedDeviceId = null;
        resolvedDeviceName = null;
        resolvedFormat = null;
        requestedCaptureFormat = null;
        nativeMixFormatDescription = null;
        rawActivated = false;
        discontinuityCount = 0;
        timestampErrorCount = 0;
        silentPacketCount = 0;
        FirstQpcPosition = null;
        workerThread = new Thread(CaptureThreadProc)
        {
            IsBackground = true,
            Name = $"ELARA-{friendlyName}",
        };
        AppLogger.Info($"{friendlyName} capture thread preparing. PreferredDeviceId={(preferredDeviceId ?? "<default>")}; TempFile={tempFilePath}");
        workerThread.Start();
    }

    public void EnsurePrepared()
    {
        if (startupException is not null)
        {
            throw startupException;
        }
    }

    public void WaitUntilStarted()
    {
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

    /// <summary>The format the audio client actually initialized, or null until started.</summary>
    public CapturedStreamFormat? ResolvedFormat => resolvedFormat;

    /// <summary>The primary capture format ELARA requested (32-bit float at the target rate).</summary>
    public CapturedStreamFormat? RequestedCaptureFormat => requestedCaptureFormat;

    /// <summary>Description of the endpoint's Windows mix format, read before initialization.</summary>
    public string? NativeMixFormatDescription => nativeMixFormatDescription;

    /// <summary>True when RAW stream options were accepted for this capture source.</summary>
    public bool RawActivated => rawActivated;

    public long? FirstQpcPosition { get; private set; }

    public void Stop()
    {
        if (workerThread is null)
        {
            return;
        }

        coordinator.RequestStop();

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

        // Never release the synchronization objects while the worker thread may
        // still be running (stop timed out): the thread uses them on shutdown.
        if (workerThread is null)
        {
            startSignal.Dispose();
        }
    }

    private void CaptureThreadProc()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioClient? audioClient = null;
        IAudioCaptureClient? captureClient = null;
        FileStream? fileStream = null;
        BinaryWriter? writer = null;
        var preparationSignaled = false;

        try
        {
            using var _ = CoreAudioInterop.EnterComScope(CoreAudioInterop.COINIT_MULTITHREADED);

            enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(
                typeof(MMDeviceEnumeratorComObject))!;
            device = ResolveDevice(enumerator);
            resolvedDeviceId = device.GetId();

            // The resolved endpoint must be exactly the selected one. Any mismatch
            // aborts the recording instead of silently recording another device.
            if (!string.IsNullOrWhiteSpace(preferredDeviceId)
                && !string.Equals(resolvedDeviceId, preferredDeviceId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    CaptureEndpointText.EndpointMismatchMessage(ResolveDataFlow()));
            }

            resolvedDeviceName = AudioInputDeviceCatalog.GetFriendlyName(device);
            AppLogger.Info(
                $"{friendlyName} capture using endpoint '{resolvedDeviceName}' [{resolvedDeviceId}]; DataFlow={ResolveDataFlow()}; Selection={(string.IsNullOrWhiteSpace(preferredDeviceId) ? "WindowsDefault(Multimedia)" : "Explicit")}");

            var flags = AudioClientStreamFlags.AutoConvertPcm
                | AudioClientStreamFlags.SourceDefaultQuality
                | AudioClientStreamFlags.NoPersist;

            if (kind == WasapiCaptureKind.SystemLoopback)
            {
                flags |= AudioClientStreamFlags.Loopback;
            }

            audioClient = ActivateAndInitializeAudioClient(
                device,
                flags,
                out var format,
                out rawActivated);
            resolvedFormat = new CapturedStreamFormat(
                format.FormatTag,
                (int)format.SamplesPerSec,
                (short)format.Channels,
                (short)format.BitsPerSample);
            AppLogger.Info(
                $"{friendlyName} capture format active. NativeMixFormat={nativeMixFormatDescription ?? "<unavailable>"}; RequestedCaptureFormat={requestedCaptureFormat?.Description ?? "<unavailable>"}; InitializedCaptureFormat={resolvedFormat?.Description ?? "<unavailable>"}; RawRequested={(kind == WasapiCaptureKind.Microphone)}; RawActivated={rawActivated}");

            if (kind == WasapiCaptureKind.Microphone)
            {
                AppLogger.Info(
                    rawActivated
                        ? "Microphone audio processing. RawRequested=True; RawActivated=True; Fallback=None; WindowsProcessingMayBeActive=False; EffectsChangedByApplication=False"
                        : "Microphone audio processing. RawRequested=True; RawActivated=False; Fallback=DefaultSharedMode; WindowsProcessingMayBeActive=True; EffectsChangedByApplication=False");
            }
            else
            {
                AppLogger.Info("System loopback audio processing. RawRequested=False; RawActivated=False; Mode=DefaultLoopback; EffectsChangedByApplication=False");
            }

            if (kind == WasapiCaptureKind.Microphone)
            {
                LogMicrophoneAudioEffects(audioClient);
            }

            var captureClientGuid = typeof(IAudioCaptureClient).GUID;
            audioClient.GetService(ref captureClientGuid, out var captureClientObject);
            captureClient = (IAudioCaptureClient)captureClientObject;

            fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            writer = new BinaryWriter(fileStream);

            coordinator.SignalPrepared();
            preparationSignaled = true;
            AppLogger.Info($"{friendlyName} capture prepared and waiting at the shared start barrier.");

            if (!coordinator.WaitForStart())
            {
                AppLogger.Info($"{friendlyName} capture stopped before the shared start barrier was released.");
                return;
            }

            audioClient.Start();
            AppLogger.Info($"{friendlyName} capture started successfully.");
            startSignal.Set();

            while (!coordinator.StopRequested)
            {
                ReadAvailablePackets(captureClient, writer, (short)format.Channels, (short)format.BitsPerSample);
                coordinator.WaitForStop(6);
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
            if (!preparationSignaled)
            {
                coordinator.SignalPrepared();
            }

            writer?.Dispose();
            fileStream?.Dispose();

            AppLogger.Info(
                $"{friendlyName} capture counters. DataDiscontinuities={discontinuityCount}; TimestampErrors={timestampErrorCount}; SilentPackets={silentPacketCount}");

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
        // The endpoint selection is planned by a pure, testable planner: an
        // explicit selection never degrades to a default, and the default is
        // always resolved in the multimedia role (never communications).
        var selection = CaptureEndpointPlanner.Plan(ResolveDataFlow(), preferredDeviceId);

        if (selection.IsExplicitDevice)
        {
            try
            {
                return enumerator.GetDevice(selection.DeviceId!);
            }
            catch (COMException ex)
            {
                throw new InvalidOperationException(
                    CaptureEndpointText.UnavailableDeviceMessage(selection.DataFlow),
                    ex);
            }
        }

        return enumerator.GetDefaultAudioEndpoint(selection.DataFlow, selection.DefaultRole);
    }

    private EDataFlow ResolveDataFlow()
    {
        return kind == WasapiCaptureKind.SystemLoopback ? EDataFlow.Render : EDataFlow.Capture;
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
                out var qpcPosition);

            try
            {
                if (FirstQpcPosition is null
                    && (flags & AudioClientBufferFlags.TimestampError) == 0)
                {
                    FirstQpcPosition = qpcPosition;
                    AppLogger.Info($"{friendlyName} first capture timestamp. Qpc100ns={qpcPosition}");
                }

                if ((flags & AudioClientBufferFlags.DataDiscontinuity) != 0)
                {
                    discontinuityCount++;
                    AppLogger.Warn($"{friendlyName} capture reported a data discontinuity (#{discontinuityCount}).");
                }

                if ((flags & AudioClientBufferFlags.TimestampError) != 0)
                {
                    timestampErrorCount++;
                    if (timestampErrorCount == 1)
                    {
                        AppLogger.Warn($"{friendlyName} capture reported a timestamp error; this packet's timestamps were ignored.");
                    }
                }

                if ((flags & AudioClientBufferFlags.Silent) != 0)
                {
                    silentPacketCount++;
                }

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
        if (bitsPerSample is not 16 and not 32)
        {
            throw new NotSupportedException($"Only 16-bit PCM and 32-bit float capture are supported in this build. Received {bitsPerSample}-bit.");
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
                var monoSample = MixFrameToMono(buffer, frameOffset, channels, bitsPerSample);
                var mixedSample = FloatToPcm16(monoSample);
                var level = Math.Abs(monoSample);
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

    private WaveFormatEx InitializeAudioClient(IAudioClient audioClient, AudioClientStreamFlags flags)
    {
        if (bitsPerSample >= 32)
        {
            var floatFormat = WaveFormatEx.CreateIeeeFloat(sampleRate, requestedChannels);
            requestedCaptureFormat = new CapturedStreamFormat(
                AudioFormatText.IeeeFloatTag,
                sampleRate,
                requestedChannels,
                32);
            try
            {
                audioClient.Initialize(AudioClientShareMode.Shared, flags, 2_000_000, 0, ref floatFormat, IntPtr.Zero);
                return floatFormat;
            }
            catch (COMException ex)
            {
                AppLogger.Warn($"{friendlyName} float capture format was not accepted. Falling back to 16-bit PCM. {ex.Message}");
            }
        }

        var pcmFormat = WaveFormatEx.CreatePcm(sampleRate, requestedChannels, 16);
        requestedCaptureFormat = new CapturedStreamFormat(AudioFormatText.PcmTag, sampleRate, requestedChannels, 16);
        audioClient.Initialize(AudioClientShareMode.Shared, flags, 2_000_000, 0, ref pcmFormat, IntPtr.Zero);
        return pcmFormat;
    }

    private IAudioClient ActivateAndInitializeAudioClient(
        IMMDevice device,
        AudioClientStreamFlags flags,
        out WaveFormatEx format,
        out bool rawActivated)
    {
        if (kind == WasapiCaptureKind.Microphone)
        {
            object? client2Object = null;
            try
            {
                var audioClient2Guid = typeof(IAudioClient2).GUID;
                device.Activate(ref audioClient2Guid, CLSCTX.All, IntPtr.Zero, out client2Object);
                var client2 = (IAudioClient2)client2Object;
                var properties = AudioClientProperties.CreateRaw();
                var result = client2.SetClientProperties(ref properties);
                if (result < 0)
                {
                    throw new COMException("The microphone endpoint rejected RAW stream properties.", result);
                }

                ReadNativeMixFormat(client2);
                format = InitializeAudioClient(client2, flags);
                rawActivated = true;
                return client2;
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException)
            {
                ReleaseComObject(client2Object);
                AppLogger.Warn(
                    $"Microphone audio processing. RawRequested=True; RawActivated=False; Fallback=DefaultSharedMode; WindowsProcessingMayBeActive=True; HResult=0x{ex.HResult:X8}; Reason={ex.Message}");
            }
        }

        var audioClientGuid = typeof(IAudioClient).GUID;
        device.Activate(ref audioClientGuid, CLSCTX.All, IntPtr.Zero, out var audioClientObject);
        var defaultClient = (IAudioClient)audioClientObject;
        try
        {
            ReadNativeMixFormat(defaultClient);
            format = InitializeAudioClient(defaultClient, flags);
            rawActivated = false;
            return defaultClient;
        }
        catch
        {
            ReleaseComObject(defaultClient);
            throw;
        }
    }

    /// <summary>Reads the endpoint's native mix format before initialization. Best effort only.</summary>
    private void ReadNativeMixFormat(IAudioClient audioClient)
    {
        if (nativeMixFormatDescription is not null)
        {
            return;
        }

        try
        {
            nativeMixFormatDescription = AudioEndpointDiagnostics.DescribeMixFormatPointer(audioClient.GetMixFormat());
        }
        catch (Exception ex)
        {
            nativeMixFormatDescription = "<unavailable>";
            AppLogger.Warn($"{friendlyName} native mix format could not be read. {ex.Message}");
        }
    }

    private void LogMicrophoneAudioEffects(IAudioClient audioClient)
    {
        IAudioEffectsManager? effectsManager = null;
        IntPtr effectsPointer = IntPtr.Zero;

        try
        {
            var effectsManagerGuid = typeof(IAudioEffectsManager).GUID;
            audioClient.GetService(ref effectsManagerGuid, out var effectsManagerObject);
            effectsManager = (IAudioEffectsManager)effectsManagerObject;

            var result = effectsManager.GetAudioEffects(out effectsPointer, out var effectCount);
            if (result < 0)
            {
                AppLogger.Warn(
                    $"Microphone APO effect discovery failed. HResult=0x{result:X8}; no effect state was changed.");
                return;
            }

            var reportedEffects = new HashSet<Guid>();
            var effectSize = Marshal.SizeOf<AudioEffect>();
            for (var index = 0U; index < effectCount; index++)
            {
                var effectPointer = IntPtr.Add(effectsPointer, checked((int)(index * effectSize)));
                var effect = Marshal.PtrToStructure<AudioEffect>(effectPointer);
                reportedEffects.Add(effect.Id);
                AppLogger.Info(
                    $"Microphone APO effect. Type={GetAudioEffectName(effect.Id)}; Id={effect.Id}; State={effect.State}; CanSetState={effect.CanSetState}; Action=None");
            }

            if (effectCount == 0)
            {
                AppLogger.Info("Microphone APO effect discovery returned no effects for the current stream; no effect state was changed.");
            }

            LogRequestedEffectSummary("NoiseSuppression", NoiseSuppressionEffectId, reportedEffects);
            LogRequestedEffectSummary("AcousticEchoCancellation", AcousticEchoCancellationEffectId, reportedEffects);
            LogRequestedEffectSummary("AutomaticGainControl", AutomaticGainControlEffectId, reportedEffects);
            LogRequestedEffectSummary("DeepNoiseSuppression", DeepNoiseSuppressionEffectId, reportedEffects);

            // ELARA never changes an effect state: this discovery is strictly
            // read-only ("EffectsChangedByApplication=False" for every path).
        }
        catch (COMException ex)
        {
            AppLogger.Info(
                $"Microphone APO effect discovery is unavailable on this Windows version or endpoint. HResult=0x{ex.HResult:X8}; no effect state was changed.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Microphone APO effect discovery failed without changing effect state. {ex.Message}");
        }
        finally
        {
            if (effectsPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(effectsPointer);
            }

            ReleaseComObject(effectsManager);
        }
    }

    private static void LogRequestedEffectSummary(
        string name,
        Guid effectId,
        HashSet<Guid> reportedEffects)
    {
        AppLogger.Info(
            $"Microphone APO effect summary. Type={name}; ReportedForCurrentStream={reportedEffects.Contains(effectId)}; Action=None; EffectsChangedByApplication=False");
    }

    private static string GetAudioEffectName(Guid effectId)
    {
        if (effectId == NoiseSuppressionEffectId)
        {
            return "NoiseSuppression";
        }

        if (effectId == AcousticEchoCancellationEffectId)
        {
            return "AcousticEchoCancellation";
        }

        if (effectId == AutomaticGainControlEffectId)
        {
            return "AutomaticGainControl";
        }

        if (effectId == DeepNoiseSuppressionEffectId)
        {
            return "DeepNoiseSuppression";
        }

        return "OtherOrVendorSpecific";
    }

    private static void WriteSilence(BinaryWriter writer, int frameCount)
    {
        for (var index = 0; index < frameCount; index++)
        {
            writer.Write((short)0);
        }
    }

    private static float MixFrameToMono(byte[] buffer, int frameOffset, short channels, short bitsPerSample)
    {
        if (channels <= 1)
        {
            return bitsPerSample == 32
                ? ReadFloat32(buffer, frameOffset)
                : ReadInt16(buffer, frameOffset) / 32768F;
        }

        float sampleSum = 0F;

        for (var channelIndex = 0; channelIndex < channels; channelIndex++)
        {
            var sampleOffset = frameOffset + channelIndex * (bitsPerSample / 8);
            var sample = bitsPerSample == 32
                ? ReadFloat32(buffer, sampleOffset)
                : ReadInt16(buffer, sampleOffset) / 32768F;

            sampleSum += sample;
        }

        return Math.Clamp(sampleSum / channels, -1F, 1F);
    }

    private static short ReadInt16(byte[] buffer, int offset)
    {
        return (short)(buffer[offset] | (buffer[offset + 1] << 8));
    }

    private static float ReadFloat32(byte[] buffer, int offset)
    {
        return Math.Clamp(BitConverter.ToSingle(buffer, offset), -1F, 1F);
    }

    private static short FloatToPcm16(float sample)
    {
        var scaled = Math.Clamp(sample, -1F, 1F) * short.MaxValue;
        return (short)Math.Clamp(MathF.Round(scaled), short.MinValue, short.MaxValue);
    }

    private static void ReleaseComObject(object? comObject)
    {
        CoreAudioInterop.ReleaseComObject(comObject);
    }
}
