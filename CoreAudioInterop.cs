using System.Runtime.InteropServices;

namespace ELARA;

internal static class CoreAudioInterop
{
    public const int COINIT_MULTITHREADED = 0x0;
    public const int COINIT_APARTMENTTHREADED = 0x2;
    public const int RpcChangedMode = unchecked((int)0x80010106);

    [DllImport("ole32.dll")]
    public static extern int CoInitializeEx(IntPtr reserved, int coInit);

    [DllImport("ole32.dll")]
    public static extern void CoUninitialize();

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant propVariant);

    public static ComScope EnterComScope(int coInit)
    {
        var hr = CoInitializeEx(IntPtr.Zero, coInit);
        return hr switch
        {
            0 or 1 => new ComScope(true),
            RpcChangedMode => new ComScope(false),
            _ => throw Marshal.GetExceptionForHR(hr) ?? new COMException("Could not initialize COM.", hr),
        };
    }

    public static void ClearPropVariant(ref PropVariant propVariant)
    {
        var hr = PropVariantClear(ref propVariant);
        if (hr < 0)
        {
            throw Marshal.GetExceptionForHR(hr) ?? new COMException("Could not clear PROPVARIANT.", hr);
        }
    }

    public static void ReleaseComObject(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.ReleaseComObject(comObject);
        }
    }

    internal readonly struct ComScope(bool shouldUninitialize) : IDisposable
    {
        public void Dispose()
        {
            if (shouldUninitialize)
            {
                CoUninitialize();
            }
        }
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct WaveFormatEx
{
    public const ushort PcmFormatTag = 1;
    public const ushort IeeeFloatFormatTag = 3;

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
            FormatTag = PcmFormatTag,
            Channels = (ushort)channels,
            SamplesPerSec = (uint)sampleRate,
            AvgBytesPerSec = (uint)(sampleRate * blockAlign),
            BlockAlign = blockAlign,
            BitsPerSample = (ushort)bitsPerSample,
            Size = 0,
        };
    }

    public static WaveFormatEx CreateIeeeFloat(int sampleRate, short channels)
    {
        const short bitsPerSample = 32;
        var blockAlign = (ushort)(channels * (bitsPerSample / 8));
        return new WaveFormatEx
        {
            FormatTag = IeeeFloatFormatTag,
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

internal enum AudioEffectState
{
    Off = 0,
    On = 1,
}

internal enum AudioStreamCategory
{
    Other = 0,
    Speech = 2,
}

[Flags]
internal enum AudioClientStreamOptions : uint
{
    None = 0,
    Raw = 0x1,
}

[StructLayout(LayoutKind.Sequential)]
internal struct AudioClientProperties
{
    public uint Size;

    [MarshalAs(UnmanagedType.Bool)]
    public bool IsOffload;

    public AudioStreamCategory Category;
    public AudioClientStreamOptions Options;

    public static AudioClientProperties CreateRaw()
    {
        return new AudioClientProperties
        {
            Size = (uint)Marshal.SizeOf<AudioClientProperties>(),
            IsOffload = false,
            Category = AudioStreamCategory.Other,
            Options = AudioClientStreamOptions.Raw,
        };
    }

    public static AudioClientProperties CreateSpeech()
    {
        return new AudioClientProperties
        {
            Size = (uint)Marshal.SizeOf<AudioClientProperties>(),
            IsOffload = false,
            Category = AudioStreamCategory.Speech,
            Options = AudioClientStreamOptions.None,
        };
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct AudioEffect
{
    public Guid Id;

    [MarshalAs(UnmanagedType.Bool)]
    public bool CanSetState;

    public AudioEffectState State;
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

[Flags]
internal enum DeviceState : uint
{
    Active = 0x00000001,
    Disabled = 0x00000002,
    NotPresent = 0x00000004,
    Unplugged = 0x00000008,
    All = 0x0000000F,
}

internal enum StorageAccessMode : uint
{
    Read = 0x00000000,
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid FormatId;
    public uint PropertyId;

    public PropertyKey(Guid formatId, uint propertyId)
    {
        FormatId = formatId;
        PropertyId = propertyId;
    }
}

// Padded to the native PROPVARIANT size (24 bytes on x64): the marshaller must
// reserve the full struct, otherwise native code (e.g. IPropertyStore::GetValue /
// PropVariantClear) writes past the 16-byte buffer and corrupts the interop frame.
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    [FieldOffset(0)]
    private readonly ushort variantType;

    [FieldOffset(8)]
    private readonly IntPtr pointerValue;

    public VarEnum VariantType => (VarEnum)variantType;

    public string? GetString()
    {
        return VariantType == VarEnum.VT_LPWSTR
            ? Marshal.PtrToStringUni(pointerValue)
            : null;
    }
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
    [return: MarshalAs(UnmanagedType.Interface)]
    IMMDeviceCollection EnumAudioEndpoints(EDataFlow dataFlow, DeviceState stateMask);

    [return: MarshalAs(UnmanagedType.Interface)]
    IMMDevice GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role);

    [return: MarshalAs(UnmanagedType.Interface)]
    IMMDevice GetDevice([MarshalAs(UnmanagedType.LPWStr)] string endpointId);

    int NotImpl4();

    int NotImpl5();
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    void GetCount(out int deviceCount);

    [return: MarshalAs(UnmanagedType.Interface)]
    IMMDevice Item(int deviceNumber);
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

    [return: MarshalAs(UnmanagedType.Interface)]
    IPropertyStore OpenPropertyStore(StorageAccessMode storageAccessMode);

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetId();

    DeviceState GetState();
}

[ComImport]
[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    void GetCount(out int propertyCount);

    void GetAt(int propertyIndex, out PropertyKey key);

    void GetValue(ref PropertyKey key, out PropVariant value);

    int NotImpl4();

    int NotImpl5();
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
[Guid("726778CD-F60A-4EDA-82DE-E47610CD78AA")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient2 : IAudioClient
{
    [PreserveSig]
    int IsOffloadCapable(AudioStreamCategory category, [MarshalAs(UnmanagedType.Bool)] out bool isOffloadCapable);

    [PreserveSig]
    int SetClientProperties(ref AudioClientProperties properties);

    [PreserveSig]
    int GetBufferSizeLimits(
        IntPtr format,
        [MarshalAs(UnmanagedType.Bool)] bool eventDriven,
        out long minimumBufferDuration,
        out long maximumBufferDuration);
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

[ComImport]
[Guid("4460B3AE-4B44-4527-8676-7548A8ACD260")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEffectsManager
{
    [PreserveSig]
    int RegisterAudioEffectsChangedNotificationCallback(IntPtr client);

    [PreserveSig]
    int UnregisterAudioEffectsChangedNotificationCallback(IntPtr client);

    [PreserveSig]
    int GetAudioEffects(out IntPtr effects, out uint effectCount);

    [PreserveSig]
    int SetAudioEffectState(ref Guid effectId, AudioEffectState state);
}
