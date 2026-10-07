using System.Runtime.InteropServices;

namespace ELARA;

internal enum AudioEndpointTransport
{
    Unknown,
    Usb,
    Bluetooth,
    Other,
}

internal static class AudioEndpointTransportExtensions
{
    public static string ToDisplayName(this AudioEndpointTransport transport)
    {
        return transport switch
        {
            AudioEndpointTransport.Usb => "USB",
            AudioEndpointTransport.Bluetooth => "Bluetooth",
            AudioEndpointTransport.Other => "Other bus",
            _ => "Unknown",
        };
    }
}

/// <summary>
/// Best-effort, read-only diagnostics for one audio endpoint: which endpoint
/// would actually be used (the selected device, or the Windows default in the
/// multimedia role), its device state, transport (only when a Windows device
/// property reports it reliably), form factor and the Windows shared-mode mix
/// format. This probe never starts capture, never changes Windows
/// configuration, never touches audio effects and never switches devices.
/// Failures degrade to a report with an error message.
/// </summary>
internal sealed record EndpointDiagnosticsReport(
    bool IsAvailable,
    bool IsWindowsDefault,
    string? PreferredDeviceId,
    string? EndpointId,
    string? FriendlyName,
    string? FormFactor,
    string? DeviceState,
    string DataFlow,
    string RoleDescription,
    AudioEndpointTransport Transport,
    int MixSampleRate,
    short MixChannels,
    short MixBitsPerSample,
    string? MixFormatDescription,
    uint? MixChannelMask,
    string? Error)
{
    public static EndpointDiagnosticsReport Unavailable(string? preferredDeviceId, string error)
    {
        return new EndpointDiagnosticsReport(
            IsAvailable: false,
            IsWindowsDefault: string.IsNullOrWhiteSpace(preferredDeviceId),
            PreferredDeviceId: preferredDeviceId,
            EndpointId: null,
            FriendlyName: null,
            FormFactor: null,
            DeviceState: null,
            DataFlow: string.Empty,
            RoleDescription: string.Empty,
            Transport: AudioEndpointTransport.Unknown,
            MixSampleRate: 0,
            MixChannels: 0,
            MixBitsPerSample: 0,
            MixFormatDescription: null,
            MixChannelMask: null,
            Error: error);
    }

    /// <summary>
    /// True when the endpoint name carries unambiguous telephony profile
    /// naming (e.g. "Hands-Free AG Audio", "HFP", "HSP"). Read-only
    /// classification: ELARA never switches, blocks or configures anything
    /// because of it. Alone it is not a quality warning — the actual format
    /// must indicate reduced bandwidth as well.
    /// </summary>
    public bool HasTelephonyProfileName =>
        EndpointQualityClassifier.IsStrongTelephonyProfileName(FriendlyName);

    /// <summary>
    /// Neutral quality concern derived from the actual available format and, in
    /// addition, from unambiguous telephony profile naming combined with a
    /// reduced-bandwidth format. The transport, the form factor or device-type
    /// words alone are never treated as a quality verdict.
    /// </summary>
    public string? BuildQualityWarning()
    {
        if (!IsAvailable)
        {
            return null;
        }

        var lowBandwidthWarning = EndpointQualityClassifier.BuildLowBandwidthWarning(MixSampleRate);

        if (HasTelephonyProfileName
            && EndpointQualityClassifier.HasReducedBandwidthIndicator(MixSampleRate))
        {
            return lowBandwidthWarning is null
                ? EndpointQualityClassifier.TelephonyWarning
                : $"{lowBandwidthWarning} {EndpointQualityClassifier.TelephonyWarning}";
        }

        return lowBandwidthWarning;
    }
}

internal static class AudioEndpointDiagnostics
{
    // PKEY_AudioEndpoint_FormFactor (mmdeviceapi.h): VT_UI4 EndpointFormFactor.
    private static readonly PropertyKey FormFactorKey = new(
        new Guid("1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E"),
        0);

    // PKEY_Device_EnumeratorName (devpkey.h): "USB", "BTHENUM", "HDAUDIO", ...
    private static readonly PropertyKey EnumeratorNameKey = new(
        new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        24);

    private const ushort WaveFormatTagExtensible = 0xFFFE;

    /// <summary>
    /// Read-only diagnostics for the microphone endpoint a recording would use:
    /// the preferred device when set, otherwise the Windows default capture
    /// device in the multimedia role — the same resolution the capture pipeline
    /// performs. Never a communications/telephony role, never a capture start.
    /// </summary>
    public static EndpointDiagnosticsReport GetMicrophone(string? preferredDeviceId)
    {
        return Get(EDataFlow.Capture, preferredDeviceId);
    }

    /// <summary>
    /// Read-only diagnostics for the playback endpoint a system-audio (WASAPI
    /// loopback) recording would use.
    /// </summary>
    public static EndpointDiagnosticsReport GetPlayback(string? preferredDeviceId)
    {
        return Get(EDataFlow.Render, preferredDeviceId);
    }

    /// <summary>
    /// Describes a WAVEFORMATEX(WAVEFORMATEXTENSIBLE) pointer returned by
    /// IAudioClient::GetMixFormat. Read-only; the caller frees the buffer.
    /// </summary>
    public static string DescribeMixFormatPointer(IntPtr formatPointer)
    {
        if (formatPointer == IntPtr.Zero)
        {
            return "<unavailable>";
        }

        var format = Marshal.PtrToStructure<WaveFormatEx>(formatPointer);
        if (format.FormatTag == WaveFormatTagExtensible && format.Size >= 22)
        {
            try
            {
                var subFormatBytes = new byte[16];
                Marshal.Copy(IntPtr.Add(formatPointer, 24), subFormatBytes, 0, subFormatBytes.Length);
                var subFormat = new Guid(subFormatBytes);
                return $"{AudioFormatText.ForExtensibleSubFormat(subFormat, format.BitsPerSample)} · {format.SamplesPerSec} Hz · {format.Channels} channel{(format.Channels == 1 ? string.Empty : "s")}";
            }
            catch
            {
                return "extensible";
            }
        }

        return $"{AudioFormatText.ForTagAndBits(format.FormatTag, format.BitsPerSample)} · {format.SamplesPerSec} Hz · {format.Channels} channel{(format.Channels == 1 ? string.Empty : "s")}";
    }

    private static EndpointDiagnosticsReport Get(EDataFlow dataFlow, string? preferredDeviceId)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioClient? audioClient = null;

        try
        {
            using var _ = CoreAudioInterop.EnterComScope(CoreAudioInterop.COINIT_APARTMENTTHREADED);

            enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(
                typeof(MMDeviceEnumeratorComObject))!;
            var selection = CaptureEndpointPlanner.Plan(dataFlow, preferredDeviceId);
            device = selection.IsExplicitDevice
                ? enumerator.GetDevice(selection.DeviceId!)
                : enumerator.GetDefaultAudioEndpoint(selection.DataFlow, selection.DefaultRole);

            var endpointId = device.GetId();
            var friendlyName = AudioInputDeviceCatalog.GetFriendlyName(device);
            var deviceState = device.GetState().ToString();
            var formFactor = ReadFormFactor(device);
            var transport = ReadTransport(device);

            var mixFormatPointer = IntPtr.Zero;
            int mixSampleRate = 0;
            short mixChannels = 0;
            short mixBitsPerSample = 0;
            uint? mixChannelMask = null;
            string? mixFormatDescription = null;

            try
            {
                var audioClientGuid = typeof(IAudioClient).GUID;
                device.Activate(ref audioClientGuid, CLSCTX.All, IntPtr.Zero, out var clientObject);
                audioClient = (IAudioClient)clientObject;

                mixFormatPointer = audioClient.GetMixFormat();
                if (mixFormatPointer != IntPtr.Zero)
                {
                    var mixFormat = Marshal.PtrToStructure<WaveFormatEx>(mixFormatPointer);
                    mixSampleRate = (int)mixFormat.SamplesPerSec;
                    mixChannels = (short)mixFormat.Channels;
                    mixBitsPerSample = (short)mixFormat.BitsPerSample;
                    mixChannelMask = ReadChannelMask(mixFormat, mixFormatPointer);
                    mixFormatDescription = DescribeMixFormat(mixFormat, mixFormatPointer);
                }
            }
            finally
            {
                if (mixFormatPointer != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(mixFormatPointer);
                }
            }

            var report = new EndpointDiagnosticsReport(
                IsAvailable: true,
                IsWindowsDefault: !selection.IsExplicitDevice,
                PreferredDeviceId: selection.DeviceId,
                EndpointId: endpointId,
                FriendlyName: friendlyName,
                FormFactor: formFactor,
                DeviceState: deviceState,
                DataFlow: selection.DataFlow.ToString(),
                RoleDescription: selection.IsExplicitDevice
                    ? "Explicit device selection"
                    : $"Windows default ({selection.DefaultRole} role)",
                Transport: transport,
                MixSampleRate: mixSampleRate,
                MixChannels: mixChannels,
                MixBitsPerSample: mixBitsPerSample,
                MixFormatDescription: mixFormatDescription,
                MixChannelMask: mixChannelMask,
                Error: null);

            AppLogger.Info(
                $"Endpoint diagnostics. Flow={selection.DataFlow}; Selection={(selection.IsExplicitDevice ? "Explicit" : "WindowsDefault")}; Name={friendlyName}; State={deviceState}; FormFactor={formFactor ?? "<unknown>"}; Transport={transport.ToDisplayName()}; MixFormat={mixSampleRate} Hz; Channels={mixChannels}; Bits={mixBitsPerSample}; Format={mixFormatDescription ?? "<unknown>"}; ChannelMask={(mixChannelMask.HasValue ? $"0x{mixChannelMask.Value:X8}" : "<n/a>")}; TelephonyProfileName={report.HasTelephonyProfileName}; QualityWarning={(report.BuildQualityWarning() ?? "<none>")}");
            return report;
        }
        catch (Exception ex)
        {
            AppLogger.Warn(
                $"Endpoint diagnostics unavailable. Flow={dataFlow}; PreferredDeviceId={(preferredDeviceId ?? "<default>")}; {ex.Message}");
            return EndpointDiagnosticsReport.Unavailable(preferredDeviceId, ex.Message);
        }
        finally
        {
            CoreAudioInterop.ReleaseComObject(audioClient);
            CoreAudioInterop.ReleaseComObject(device);
            CoreAudioInterop.ReleaseComObject(enumerator);
        }
    }

    private static string? ReadFormFactor(IMMDevice device)
    {
        IPropertyStore? propertyStore = null;
        PropVariant propVariant = default;

        try
        {
            propertyStore = device.OpenPropertyStore(StorageAccessMode.Read);
            var key = FormFactorKey;
            propertyStore.GetValue(ref key, out propVariant);

            return propVariant.GetUInt32() switch
            {
                0 => "RemoteNetworkDevice",
                1 => "Speakers",
                2 => "LineLevel",
                3 => "Headphones",
                4 => "Microphone",
                5 => "Headset",
                6 => "Handset",
                7 => "DigitalPassthrough",
                8 => "SPDIF",
                9 => "HDMI/Display",
                10 => "UnknownFormFactor",
                _ => null,
            };
        }
        catch
        {
            return null;
        }
        finally
        {
            TryClearPropVariant(ref propVariant);
            CoreAudioInterop.ReleaseComObject(propertyStore);
        }
    }

    /// <summary>
    /// Transport classification strictly from a Windows device property
    /// (PKEY_Device_EnumeratorName). When Windows does not report a name, the
    /// transport stays Unknown — it is never guessed from a device or vendor
    /// name, and a vendor USB dongle is never reclassified as Bluetooth.
    /// </summary>
    private static AudioEndpointTransport ReadTransport(IMMDevice device)
    {
        var enumeratorName = ReadStringProperty(device, EnumeratorNameKey);
        if (string.IsNullOrWhiteSpace(enumeratorName))
        {
            return AudioEndpointTransport.Unknown;
        }

        var normalized = enumeratorName.Trim().ToUpperInvariant();
        if (normalized == "USB")
        {
            return AudioEndpointTransport.Usb;
        }

        if (normalized.StartsWith("BTH", StringComparison.Ordinal))
        {
            return AudioEndpointTransport.Bluetooth;
        }

        return AudioEndpointTransport.Other;
    }

    private static string? ReadStringProperty(IMMDevice device, PropertyKey key)
    {
        IPropertyStore? propertyStore = null;
        PropVariant propVariant = default;

        try
        {
            propertyStore = device.OpenPropertyStore(StorageAccessMode.Read);
            var localKey = key;
            propertyStore.GetValue(ref localKey, out propVariant);
            return propVariant.GetString();
        }
        catch
        {
            return null;
        }
        finally
        {
            TryClearPropVariant(ref propVariant);
            CoreAudioInterop.ReleaseComObject(propertyStore);
        }
    }

    /// <summary>
    /// Channel mask of a WAVE_FORMAT_EXTENSIBLE mix format (dwChannelMask sits
    /// between the valid-bits field and the sub-format GUID), or null when the
    /// format is not extensible.
    /// </summary>
    private static uint? ReadChannelMask(WaveFormatEx format, IntPtr formatPointer)
    {
        if (format.FormatTag != WaveFormatTagExtensible || format.Size < 22)
        {
            return null;
        }

        try
        {
            return unchecked((uint)Marshal.ReadInt32(formatPointer, 20));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Describes the mix format including the actual payload type. The shared
    /// audio engine reports WAVE_FORMAT_EXTENSIBLE; the sub-format GUID then
    /// distinguishes IEEE float from integer PCM (offset 24 in the struct).
    /// </summary>
    private static string DescribeMixFormat(WaveFormatEx format, IntPtr formatPointer)
    {
        if (format.FormatTag == WaveFormatTagExtensible && format.Size >= 22)
        {
            try
            {
                var subFormatBytes = new byte[16];
                Marshal.Copy(IntPtr.Add(formatPointer, 24), subFormatBytes, 0, subFormatBytes.Length);
                var subFormat = new Guid(subFormatBytes);
                return AudioFormatText.ForExtensibleSubFormat(subFormat, format.BitsPerSample);
            }
            catch
            {
                return "extensible";
            }
        }

        return AudioFormatText.ForTagAndBits(format.FormatTag, format.BitsPerSample);
    }

    private static void TryClearPropVariant(ref PropVariant propVariant)
    {
        try
        {
            CoreAudioInterop.ClearPropVariant(ref propVariant);
        }
        catch
        {
        }
    }
}