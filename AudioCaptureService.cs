using System.Diagnostics;
using System.Threading;

namespace SimpleAudioRecorder;

public enum CaptureMode
{
    Both,
    Microphone,
    System,
}

public readonly record struct RecordingInfo(string FilePath, CaptureMode Mode, TimeSpan Duration, DateTime CreatedAt);

public static class CaptureModeExtensions
{
    public static string ToDisplayName(this CaptureMode mode)
    {
        return mode switch
        {
            CaptureMode.Both => "Both",
            CaptureMode.Microphone => "Mic",
            CaptureMode.System => "System",
            _ => mode.ToString(),
        };
    }
}

public sealed class AudioCaptureService : IDisposable
{
    private const int TargetSampleRate = 48_000;
    private const short TargetBitsPerSample = 16;
    private const short RequestedChannels = 2;

    private readonly SemaphoreSlim transitionLock = new(1, 1);
    private readonly Stopwatch stopwatch = new();

    private WasapiCaptureSource? microphoneSource;
    private WasapiCaptureSource? systemSource;
    private string? tempDirectory;
    private string? tempMicrophonePath;
    private string? tempSystemPath;
    private string? outputFilePath;
    private CaptureMode currentMode;
    private int peakMilli;

    public bool IsRecording { get; private set; }

    public TimeSpan Elapsed => stopwatch.Elapsed;

    public string OutputDirectory { get; } = AppPaths.RecordingDirectory;

    public string LogDirectory => AppLogger.LogDirectory;

    public string? ActiveMicrophoneDeviceName { get; private set; }

    public string? ActiveMicrophoneDeviceId { get; private set; }

    public float ConsumePeak()
    {
        return Interlocked.Exchange(ref peakMilli, 0) / 1000F;
    }

    public async Task StartAsync(CaptureMode mode, string? microphoneDeviceId = null)
    {
        await transitionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            StartCore(mode, microphoneDeviceId);
        }
        finally
        {
            transitionLock.Release();
        }
    }

    public async Task<RecordingInfo> StopAsync()
    {
        await transitionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            return StopCore();
        }
        finally
        {
            transitionLock.Release();
        }
    }

    public void Dispose()
    {
        try
        {
            if (IsRecording)
            {
                StopCore();
            }
        }
        catch
        {
        }

        transitionLock.Dispose();
    }

    private void StartCore(CaptureMode mode, string? microphoneDeviceId)
    {
        if (IsRecording)
        {
            throw new InvalidOperationException("A recording session is already running.");
        }

        Directory.CreateDirectory(OutputDirectory);

        currentMode = mode;
        ActiveMicrophoneDeviceId = null;
        ActiveMicrophoneDeviceName = null;
        tempDirectory = Path.Combine(Path.GetTempPath(), "SimpleAudioRecorder", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        tempMicrophonePath = Path.Combine(tempDirectory, "microphone.pcm");
        tempSystemPath = Path.Combine(tempDirectory, "system.pcm");
        outputFilePath = Path.Combine(
            OutputDirectory,
            $"{DateTime.Now:yyyy-MM-dd HH-mm-ss} {mode.ToDisplayName().ToLowerInvariant()}.wav");

        AppLogger.Info(
            $"Starting recording. Mode={mode}; RequestedMicrophoneId={(microphoneDeviceId ?? "<default>")}; OutputFile={outputFilePath}; TempDirectory={tempDirectory}");

        try
        {
            if (mode is CaptureMode.Both or CaptureMode.Microphone)
            {
                microphoneSource = new WasapiCaptureSource(
                    "microphone",
                    WasapiCaptureKind.Microphone,
                    microphoneDeviceId,
                    tempMicrophonePath,
                    TargetSampleRate,
                    TargetBitsPerSample,
                    RequestedChannels,
                    ReportPeak);
                microphoneSource.Start();
                ActiveMicrophoneDeviceId = microphoneSource.ResolvedDeviceId;
                ActiveMicrophoneDeviceName = microphoneSource.ResolvedDeviceName;
            }

            if (mode is CaptureMode.Both or CaptureMode.System)
            {
                systemSource = new WasapiCaptureSource(
                    "system audio",
                    WasapiCaptureKind.SystemLoopback,
                    null,
                    tempSystemPath,
                    TargetSampleRate,
                    TargetBitsPerSample,
                    RequestedChannels,
                    ReportPeak);
                systemSource.Start();
            }

            Interlocked.Exchange(ref peakMilli, 0);
            stopwatch.Restart();
            IsRecording = true;
            AppLogger.Info(
                $"Recording active. Mode={currentMode}; Microphone={(ActiveMicrophoneDeviceName ?? "<not used>")} [{(ActiveMicrophoneDeviceId ?? "<n/a>")}]");
        }
        catch (Exception ex)
        {
            AppLogger.Error("Recording startup failed.", ex);
            CleanupCaptureSources();
            CleanupTempFiles();
            throw;
        }
    }

    private RecordingInfo StopCore()
    {
        if (!IsRecording)
        {
            throw new InvalidOperationException("There is no active recording to stop.");
        }

        Exception? stopError = null;
        RecordingInfo? recordingInfo = null;

        try
        {
            microphoneSource?.Stop();
            systemSource?.Stop();
        }
        catch (Exception ex)
        {
            stopError = ex;
        }
        finally
        {
            stopwatch.Stop();
            IsRecording = false;
            Interlocked.Exchange(ref peakMilli, 0);
        }

        try
        {
            if (stopError is not null)
            {
                throw stopError;
            }

            if (outputFilePath is null)
            {
                throw new InvalidOperationException("The output file path was not initialized.");
            }

            switch (currentMode)
            {
                case CaptureMode.Both:
                    WavUtility.WriteStereoWavFromMonoPcm(
                        tempSystemPath ?? throw new InvalidOperationException("Missing system audio capture."),
                        tempMicrophonePath ?? throw new InvalidOperationException("Missing microphone capture."),
                        outputFilePath,
                        TargetSampleRate,
                        TargetBitsPerSample);
                    break;
                case CaptureMode.Microphone:
                    WavUtility.WriteMonoWavFromPcm(
                        tempMicrophonePath ?? throw new InvalidOperationException("Missing microphone capture."),
                        outputFilePath,
                        TargetSampleRate,
                        TargetBitsPerSample);
                    break;
                case CaptureMode.System:
                    WavUtility.WriteMonoWavFromPcm(
                        tempSystemPath ?? throw new InvalidOperationException("Missing system audio capture."),
                        outputFilePath,
                        TargetSampleRate,
                        TargetBitsPerSample);
                    break;
            }

            var fileInfo = new FileInfo(outputFilePath);
            if (!fileInfo.Exists || fileInfo.Length <= 44)
            {
                throw new InvalidOperationException(
                    "The recording file was created but did not contain audio. If you recorded system audio, make sure audio was actively playing.");
            }

            recordingInfo = new RecordingInfo(outputFilePath, currentMode, stopwatch.Elapsed, DateTime.Now);
            AppLogger.Info(
                $"Recording saved. File={recordingInfo.Value.FilePath}; Duration={recordingInfo.Value.Duration}; Mode={recordingInfo.Value.Mode}");
            return recordingInfo.Value;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Recording stop/save failed.", ex);
            throw;
        }
        finally
        {
            CleanupCaptureSources();
            CleanupTempFiles();
        }
    }

    private void CleanupCaptureSources()
    {
        microphoneSource?.Dispose();
        systemSource?.Dispose();
        microphoneSource = null;
        systemSource = null;
        ActiveMicrophoneDeviceId = null;
        ActiveMicrophoneDeviceName = null;
    }

    private void CleanupTempFiles()
    {
        if (tempDirectory is not null && Directory.Exists(tempDirectory))
        {
            try
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
            catch
            {
            }
        }

        tempDirectory = null;
        tempMicrophonePath = null;
        tempSystemPath = null;
        outputFilePath = null;
    }

    private void ReportPeak(float peak)
    {
        var scaled = (int)(Math.Clamp(peak, 0F, 1F) * 1000F);
        while (true)
        {
            var snapshot = peakMilli;
            if (scaled <= snapshot)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref peakMilli, scaled, snapshot) == snapshot)
            {
                return;
            }
        }
    }
}
