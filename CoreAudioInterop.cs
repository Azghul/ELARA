using System.Runtime.InteropServices;

namespace SimpleAudioRecorder;

internal static class CoreAudioInterop
{
    public const int COINIT_MULTITHREADED = 0x0;

    [DllImport("ole32.dll")]
    public static extern int CoInitializeEx(IntPtr reserved, int coInit);

    [DllImport("ole32.dll")]
    public static extern void CoUninitialize();
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct WaveFormatEx
{
    public ushort FormatTag;
    public ushort Channels;
    public uint SamplesPerSec;
    public uint AvgBytesPerSec;
    public ushort BlockAlign;
    public ushort BitsPerSample;
    public ushort Size;

    public static WaveFormatEx CreatePcm(int sampleRate, short channels, short bitsPerSample)
    {
        var blockAlign = (ushort)(channels * (bitsPerSample / 8));
        return new WaveFormatEx
        {
            FormatTag = 1,
            Channels = (ushort)channels,
            SamplesPerSec = (uint)sampleRate,
            AvgBytesPerSec = (uint)(sampleRate * blockAlign),
            BlockAlign = blockAlign,
            BitsPerSample = (ushort)bitsPerSample,
            Size = 0,
        };
    }
}

[Flags]
internal enum AudioClientStreamFlags : uint
{
    None = 0,
    CrossProcess = 0x00010000,
    Loopback = 0x00020000,
    EventCallback = 0x00040000,
    NoPersist = 0x00080000,
    RateAdjust = 0x00100000,
    SourceDefaultQuality = 0x08000000,
    AutoConvertPcm = 0x80000000,
}

[Flags]
internal enum AudioClientBufferFlags : uint
{
    None = 0,
    DataDiscontinuity = 0x1,
    Silent = 0x2,
    TimestampError = 0x4,
}

internal enum AudioClientShareMode
{
    Shared = 0,
    Exclusive = 1,
}

internal enum EDataFlow
{
    Render,
    Capture,
    All,
}

internal enum ERole
{
    Console,
    Multimedia,
    Communications,
}

[Flags]
internal enum CLSCTX : uint
{
    InprocServer = 0x1,
    InprocHandler = 0x2,
    LocalServer = 0x4,
    RemoteServer = 0x10,
    All = InprocServer | InprocHandler | LocalServer | RemoteServer,
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal sealed class MMDeviceEnumeratorComObject
{
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    int NotImpl1();

    [return: MarshalAs(UnmanagedType.Interface)]
    IMMDevice GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    void Activate(
        ref Guid iid,
        CLSCTX clsCtx,
        IntPtr activationParams,
        [MarshalAs(UnmanagedType.Interface)] out object interfacePointer);
}

[ComImport]
[Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    void Initialize(
        AudioClientShareMode shareMode,
        AudioClientStreamFlags streamFlags,
        long bufferDuration,
        long periodicity,
        ref WaveFormatEx format,
        IntPtr sessionGuid);

    void GetBufferSize(out int bufferSize);

    void GetStreamLatency(out long latency);

    void GetCurrentPadding(out int currentPadding);

    IntPtr IsFormatSupported(AudioClientShareMode shareMode, ref WaveFormatEx format, IntPtr closestMatchFormat);

    IntPtr GetMixFormat();

    void GetDevicePeriod(out long defaultDevicePeriod, out long minimumDevicePeriod);

    void Start();

    void Stop();

    void Reset();

    void SetEventHandle(IntPtr eventHandle);

    void GetService(ref Guid serviceGuid, [MarshalAs(UnmanagedType.Interface)] out object interfacePointer);
}

[ComImport]
[Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClient
{
    void GetBuffer(
        out IntPtr data,
        out int numFramesToRead,
        out AudioClientBufferFlags flags,
        out long devicePosition,
        out long qpcPosition);

    void ReleaseBuffer(int numFramesRead);

    void GetNextPacketSize(out int numFramesInNextPacket);
}
