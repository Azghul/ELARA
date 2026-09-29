using System.Diagnostics;
using System.Threading;

namespace SimpleAudioRecorder;

public enum CaptureMode
{
    Both,
    Microphone,
    System,
}

public enum OutputFormat
{
    Mp3,
    Wav,
}

public static class OutputFormatExtensions
{
    public static string ToFileExtension(this OutputFormat format)
    {
        return format switch
        {
            OutputFormat.Mp3 => ".mp3",
            OutputFormat.Wav => ".wav",
            _ => "." + format.ToString().ToLowerInvariant(),
        };
    }

    public static string ToDisplayName(this OutputFormat format)
    {
        return format switch
        {
            OutputFormat.Mp3 => "MP3",
            OutputFormat.Wav => "WAV",
            _ => format.ToString(),
        };
    }
}

public readonly record struct RecordingInfo(
    string FilePath,
    OutputFormat Format,
    CaptureMode Mode,
    TimeSpan Duration,
    DateTime CreatedAt,
    bool WasRescuedToWav,
    bool HadCaptureStopErrors);

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
    private const short TargetOutputBitsPerSample = 16;
    private const short TargetCaptureBitsPerSample = 32;
    private const short MicrophoneRequestedChannels = 1;
    private const short SystemRequestedChannels = 2;
    private const long MinimumWavDataLengthBytes = 44;
    private const long MinimumMp3FileLengthBytes = 1024;

    private readonly SemaphoreSlim transitionLock = new(1, 1);
    private readonly Stopwatch stopwatch = new();

    private WasapiCaptureSource? microphoneSource;
    private WasapiCaptureSource? systemSource;
    private string? tempDirectory;
    private string? tempMicrophonePath;
    private string? tempSystemPath;
    private string? outputFilePath;
    private CaptureMode currentMode;
    private OutputFormat currentFormat;
    private int peakMilli;

    public bool IsRecording { get; private set; }

    public TimeSpan Elapsed => stopwatch.Elapsed;

    public string? CustomOutputDirectory { get; set; }

    public string OutputDirectory => ResolveOutputDirectory();

    private string ResolveOutputDirectory()
    {
        var custom = CustomOutputDirectory;
        if (string.IsNullOrWhiteSpace(custom))
        {
            return AppPaths.RecordingDirectory;
        }

        try
        {
            Directory.CreateDirectory(custom);
            return custom;
        }
        catch (Exception ex)
        {
            AppLogger.Warn(
                $"Custom output directory '{custom}' is not usable; falling back to the default recording directory. {ex.Message}");
            return AppPaths.RecordingDirectory;
        }
    }

    public string LogDirectory => AppLogger.LogDirectory;

    public string? ActiveMicrophoneDeviceName { get; private set; }

    public string? ActiveMicrophoneDeviceId { get; private set; }

    public string? ActiveSystemDeviceName { get; private set; }

    public string? ActiveSystemDeviceId { get; private set; }

    public float ConsumePeak()
    {
        return Interlocked.Exchange(ref peakMilli, 0) / 1000F;
    }

    public async Task StartAsync(
        CaptureMode mode,
        string? microphoneDeviceId = null,
        string? playbackDeviceId = null,
        OutputFormat format = OutputFormat.Wav)
    {
        await transitionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            StartCore(mode, microphoneDeviceId, playbackDeviceId, format);
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

    private void StartCore(CaptureMode mode, string? microphoneDeviceId, string? playbackDeviceId, OutputFormat format)
    {
        if (IsRecording)
        {
            throw new InvalidOperationException("A recording session is already running.");
        }

        Directory.CreateDirectory(OutputDirectory);

        currentMode = mode;
        currentFormat = format;
        ActiveMicrophoneDeviceId = null;
        ActiveMicrophoneDeviceName = null;
        ActiveSystemDeviceId = null;
        ActiveSystemDeviceName = null;
        tempDirectory = Path.Combine(Path.GetTempPath(), "SimpleAudioRecorder", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        tempMicrophonePath = Path.Combine(tempDirectory, "microphone.pcm");
        tempSystemPath = Path.Combine(tempDirectory, "system.pcm");
        outputFilePath = Path.Combine(
            OutputDirectory,
            $"{DateTime.Now:yyyy-MM-dd HH-mm-ss} {mode.ToDisplayName().ToLowerInvariant()}{format.ToFileExtension()}");

        AppLogger.Info(
            $"Starting recording. Mode={mode}; RequestedFormat={format.ToDisplayName()}; RequestedMicrophoneId={(microphoneDeviceId ?? "<default>")}; RequestedPlaybackId={(playbackDeviceId ?? "<default>")}; OutputFile={outputFilePath}; TempDirectory={tempDirectory}");

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
                    TargetCaptureBitsPerSample,
                    MicrophoneRequestedChannels,
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
                    playbackDeviceId,
                    tempSystemPath,
                    TargetSampleRate,
                    TargetCaptureBitsPerSample,
                    SystemRequestedChannels,
                    ReportPeak);
                systemSource.Start();
                ActiveSystemDeviceId = systemSource.ResolvedDeviceId;
                ActiveSystemDeviceName = systemSource.ResolvedDeviceName;
            }

            Interlocked.Exchange(ref peakMilli, 0);
            stopwatch.Restart();
            IsRecording = true;
            AppLogger.Info(
                $"Recording active. Mode={currentMode}; Format={format.ToDisplayName()}; Microphone={(ActiveMicrophoneDeviceName ?? "<not used>")} [{(ActiveMicrophoneDeviceId ?? "<n/a>")}]; SystemAudio={(ActiveSystemDeviceName ?? "<not used>")} [{(ActiveSystemDeviceId ?? "<n/a>")}]");

            if (ActiveMicrophoneDeviceName is { } microphoneName
                && microphoneName.Contains("Chat", StringComparison.OrdinalIgnoreCase))
            {
                AppLogger.Warn(
                    $"The selected microphone endpoint '{microphoneName}' appears to be a chat/communications profile. That device profile can limit microphone fidelity independently of the app.");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Recording startup failed.", ex);
            throw AbortStartup(ex);
        }
    }

    private Exception AbortStartup(Exception startupError)
    {
        // Stop every already-started source as well as possible, but never dispose a
        // worker that may still be running: its synchronization objects must stay alive.
        var anyWorkerMayStillRun = false;

        foreach (var source in new[] { microphoneSource, systemSource })
        {
            if (source is null)
            {
                continue;
            }

            var stoppedSafely = false;
            try
            {
                source.Stop();
                stoppedSafely = true;
            }
            catch (TimeoutException stopEx)
            {
                anyWorkerMayStillRun = true;
                AppLogger.Error(
                    "A capture source did not stop safely during startup abort (worker thread may still be running); it will not be disposed.",
                    stopEx);
            }
            catch (Exception stopEx)
            {
                // The worker joined (for example a wrapped background exception); safe to dispose.
                stoppedSafely = true;
                AppLogger.Warn($"A capture source reported an error while stopping during startup abort. {stopEx.Message}");
            }

            if (stoppedSafely)
            {
                try
                {
                    source.Dispose();
                }
                catch
                {
                }
            }
        }

        microphoneSource = null;
        systemSource = null;
        ActiveMicrophoneDeviceId = null;
        ActiveMicrophoneDeviceName = null;
        ActiveSystemDeviceId = null;
        ActiveSystemDeviceName = null;

        // Raw-data safety: keep the temporary directory if any PCM data exists OR if a
        // worker may still be running (it could still write PCM data).
        var anyPcmData = HasPcmData(tempMicrophonePath) || HasPcmData(tempSystemPath);
        if (anyPcmData || anyWorkerMayStillRun)
        {
            LogKeptRawAudio(anyPcmData
                ? "Recording startup failed after audio was already captured."
                : "Recording startup failed and a capture worker may still be running.");
            return new InvalidOperationException(
                $"The recording could not be started completely. Raw audio was kept at: {tempDirectory}",
                startupError);
        }

        // No raw data exists and all workers terminated safely: normal cleanup is safe.
        CleanupTempFiles();
        return startupError;
    }

    private RecordingInfo StopCore()
    {
        if (!IsRecording)
        {
            throw new InvalidOperationException("There is no active recording to stop.");
        }

        // PHASE 1: stop every active capture source independently so that a failure on
        // one source never prevents the other source from being stopped and flushed.
        var stopErrors = new List<Exception>();
        var timedOut = false;

        if (microphoneSource is not null)
        {
            try
            {
                microphoneSource.Stop();
            }
            catch (TimeoutException ex)
            {
                timedOut = true;
                stopErrors.Add(ex);
                AppLogger.Error("Microphone capture did not stop safely (worker thread may still be running).", ex);
            }
            catch (Exception ex)
            {
                stopErrors.Add(ex);
                AppLogger.Warn($"Microphone capture reported an error while stopping. {ex.Message}");
            }
        }

        if (systemSource is not null)
        {
            try
            {
                systemSource.Stop();
            }
            catch (TimeoutException ex)
            {
                timedOut = true;
                stopErrors.Add(ex);
                AppLogger.Error("System audio capture did not stop safely (worker thread may still be running).", ex);
            }
            catch (Exception ex)
            {
                stopErrors.Add(ex);
                AppLogger.Warn($"System audio capture reported an error while stopping. {ex.Message}");
            }
        }

        stopwatch.Stop();
        IsRecording = false;
        Interlocked.Exchange(ref peakMilli, 0);

        try
        {
            // PHASE 2: a stop timeout means the worker thread may still be writing to the
            // PCM file. Never encode and never clean up: keep the raw audio, surface the path.
            if (timedOut)
            {
                AbandonCaptureSources();
                LogKeptRawAudio("Capture source stop timed out.");
                throw new InvalidOperationException(
                    $"The capture source could not be stopped safely. Raw audio was kept at: {tempDirectory}");
            }

            // PHASE 3: classify the captured raw PCM data.
            var anyPcmData = HasPcmData(tempMicrophonePath) || HasPcmData(tempSystemPath);
            if (!anyPcmData)
            {
                // Nothing usable was captured at all, so normal cleanup is safe.
                CleanupTempFiles();
                throw new InvalidOperationException(
                    "No audio was captured. If you recorded system audio, make sure audio was actively playing.");
            }

            if (!RequiredPcmDataComplete())
            {
                // Partial raw data exists (for example Both mode with an empty microphone
                // track). Raw data must be preserved; never encode an incomplete recording.
                LogKeptRawAudio("The recording is incomplete.");
                throw new InvalidOperationException(
                    $"The recording is incomplete. Available raw audio was kept at: {tempDirectory}");
            }

            // PHASE 4 + 5: produce the final file. Only a successfully written and validated
            // final file may trigger the temporary cleanup.
            return SaveRecording(stopErrors);
        }
        finally
        {
            // Releases COM/thread references only; never touches temporary files.
            CleanupCaptureSources();
        }
    }

    private RecordingInfo SaveRecording(List<Exception> stopErrors)
    {
        var requestedFormat = currentFormat;
        var baseName = Path.GetFileNameWithoutExtension(outputFilePath)
            ?? throw new InvalidOperationException("The output file path was not initialized.");
        var mp3Path = Path.Combine(OutputDirectory, baseName + ".mp3");
        var wavPath = Path.Combine(OutputDirectory, baseName + ".wav");
        var rescuedToWav = false;
        string finalPath;

        try
        {
            if (requestedFormat == OutputFormat.Mp3)
            {
                try
                {
                    EncodeOutput(OutputFormat.Mp3, mp3Path);
                    ValidateOutputFile(mp3Path, OutputFormat.Mp3);
                    finalPath = mp3Path;
                }
                catch (Exception mp3Error)
                {
                    // Data safety first: rescue the raw recording as WAV instead of losing it.
                    AppLogger.Error("MP3 encoding failed. Attempting to rescue the recording as WAV.", mp3Error);
                    TryDeleteFile(mp3Path);
                    EncodeOutput(OutputFormat.Wav, wavPath);
                    ValidateOutputFile(wavPath, OutputFormat.Wav);
                    rescuedToWav = true;
                    finalPath = wavPath;
                }
            }
            else
            {
                EncodeOutput(OutputFormat.Wav, wavPath);
                ValidateOutputFile(wavPath, OutputFormat.Wav);
                finalPath = wavPath;
            }
        }
        catch (Exception saveError)
        {
            // The final WAV/MP3 could not be produced. Raw PCM must never be deleted here.
            LogKeptRawAudio("Saving the final file failed.");
            throw new InvalidOperationException(
                $"The recording could not be saved. Raw audio was kept at: {tempDirectory}",
                saveError);
        }

        // A valid final file exists: the temporary raw data is no longer needed.
        CleanupTempFiles();

        var recordingInfo = new RecordingInfo(
            finalPath,
            rescuedToWav ? OutputFormat.Wav : requestedFormat,
            currentMode,
            stopwatch.Elapsed,
            DateTime.Now,
            rescuedToWav,
            stopErrors.Count > 0);

        AppLogger.Info(
            $"Recording saved. File={recordingInfo.FilePath}; Format={recordingInfo.Format.ToDisplayName()}; Requested={requestedFormat.ToDisplayName()}; RescuedToWav={rescuedToWav}; HadCaptureStopErrors={recordingInfo.HadCaptureStopErrors}; Duration={recordingInfo.Duration}; Mode={recordingInfo.Mode}");
        return recordingInfo;
    }

    private static bool HasPcmData(string? path)
    {
        return path is not null && File.Exists(path) && new FileInfo(path).Length > 0;
    }

    private bool RequiredPcmDataComplete()
    {
        return currentMode switch
        {
            CaptureMode.Both => HasPcmData(tempMicrophonePath) && HasPcmData(tempSystemPath),
            CaptureMode.Microphone => HasPcmData(tempMicrophonePath),
            CaptureMode.System => HasPcmData(tempSystemPath),
            _ => false,
        };
    }

    private void LogKeptRawAudio(string reason)
    {
        AppLogger.Error(
            $"{reason} Temporary PCM files were kept. TempDirectory={tempDirectory}; MicrophonePcm={tempMicrophonePath}; SystemPcm={tempSystemPath}");
    }

    private void AbandonCaptureSources()
    {
        // A timed-out worker thread may still be running. Do not call Stop()/Dispose()
        // on it again (that would block on Join); its own finally block releases the
        // COM objects and closes the PCM file once the thread terminates.
        microphoneSource = null;
        systemSource = null;
        ActiveMicrophoneDeviceId = null;
        ActiveMicrophoneDeviceName = null;
        ActiveSystemDeviceId = null;
        ActiveSystemDeviceName = null;
        AppLogger.Warn(
            "Capture sources were abandoned after a stop timeout. Their worker threads release COM objects and close the PCM files on exit.");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Could not delete the partial output file '{path}'. {ex.Message}");
        }
    }

    private void EncodeOutput(OutputFormat format, string path)
    {
        switch (currentMode)
        {
            case CaptureMode.Both:
                if (format == OutputFormat.Mp3)
                {
                    Mp3Utility.WriteStereoMp3FromMonoPcm(
                        tempSystemPath ?? throw new InvalidOperationException("Missing system audio capture."),
                        tempMicrophonePath ?? throw new InvalidOperationException("Missing microphone capture."),
                        path,
                        TargetSampleRate);
                }
                else
                {
                    WavUtility.WriteStereoWavFromMonoPcm(
                        tempSystemPath ?? throw new InvalidOperationException("Missing system audio capture."),
                        tempMicrophonePath ?? throw new InvalidOperationException("Missing microphone capture."),
                        path,
                        TargetSampleRate,
                        TargetOutputBitsPerSample);
                }

                break;

            case CaptureMode.Microphone:
                if (format == OutputFormat.Mp3)
                {
                    Mp3Utility.WriteMonoMp3FromPcm(
                        tempMicrophonePath ?? throw new InvalidOperationException("Missing microphone capture."),
                        path,
                        TargetSampleRate,
                        isMicrophoneTrack: true);
                }
                else
                {
                    WavUtility.WriteMonoWavFromPcm(
                        tempMicrophonePath ?? throw new InvalidOperationException("Missing microphone capture."),
                        path,
                        TargetSampleRate,
                        TargetOutputBitsPerSample,
                        isMicrophoneTrack: true);
                }

                break;

            case CaptureMode.System:
                if (format == OutputFormat.Mp3)
                {
                    Mp3Utility.WriteMonoMp3FromPcm(
                        tempSystemPath ?? throw new InvalidOperationException("Missing system audio capture."),
                        path,
                        TargetSampleRate,
                        isMicrophoneTrack: false);
                }
                else
                {
                    WavUtility.WriteMonoWavFromPcm(
                        tempSystemPath ?? throw new InvalidOperationException("Missing system audio capture."),
                        path,
                        TargetSampleRate,
                        TargetOutputBitsPerSample,
                        isMicrophoneTrack: false);
                }

                break;
        }
    }

    private static void ValidateOutputFile(string path, OutputFormat format)
    {
        var fileInfo = new FileInfo(path);
        var minimumLength = format == OutputFormat.Mp3 ? MinimumMp3FileLengthBytes : MinimumWavDataLengthBytes;
        if (!fileInfo.Exists || fileInfo.Length <= minimumLength)
        {
            throw new InvalidOperationException(
                "The recording file was created but did not contain audio. If you recorded system audio, make sure audio was actively playing.");
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
        ActiveSystemDeviceId = null;
        ActiveSystemDeviceName = null;
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
