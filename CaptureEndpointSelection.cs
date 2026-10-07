namespace ELARA;

/// <summary>
/// How a capture source resolves its Windows audio endpoint. An explicit
/// selection never degrades to a default or to another device: if the device is
/// gone, resolution fails with a clear error instead of falling back.
/// </summary>
internal enum CaptureEndpointSelectionMode
{
    ExplicitDevice,
    WindowsDefault,
}

/// <summary>
/// The immutable plan for locating one capture endpoint. Pure data: the COM
/// layer executes the plan, tests can verify it without Windows audio devices.
/// </summary>
internal readonly record struct CaptureEndpointSelection(
    CaptureEndpointSelectionMode Mode,
    string? DeviceId,
    EDataFlow DataFlow,
    ERole DefaultRole)
{
    public bool IsExplicitDevice => Mode == CaptureEndpointSelectionMode.ExplicitDevice;

    public static CaptureEndpointSelection ForExplicitDevice(string deviceId, EDataFlow dataFlow)
    {
        return new CaptureEndpointSelection(
            CaptureEndpointSelectionMode.ExplicitDevice,
            deviceId,
            dataFlow,
            ERole.Multimedia);
    }

    public static CaptureEndpointSelection ForWindowsDefault(EDataFlow dataFlow)
    {
        // The default is resolved with the multimedia role only. ELARA never
        // resolves a communications/telephony endpoint on its own.
        return new CaptureEndpointSelection(
            CaptureEndpointSelectionMode.WindowsDefault,
            null,
            dataFlow,
            ERole.Multimedia);
    }
}

/// <summary>
/// Pure planner for endpoint resolution. A null/empty preferred id is an
/// explicit "Windows default" choice by the user; any other value is an explicit
/// device that must be used verbatim or fail loudly.
/// </summary>
internal static class CaptureEndpointPlanner
{
    public static CaptureEndpointSelection Plan(EDataFlow dataFlow, string? preferredDeviceId)
    {
        return string.IsNullOrWhiteSpace(preferredDeviceId)
            ? CaptureEndpointSelection.ForWindowsDefault(dataFlow)
            : CaptureEndpointSelection.ForExplicitDevice(preferredDeviceId, dataFlow);
    }
}

/// <summary>Neutral, vendor-agnostic user-facing endpoint messages.</summary>
internal static class CaptureEndpointText
{
    public static string UnavailableDeviceMessage(EDataFlow dataFlow)
    {
        return dataFlow == EDataFlow.Render
            ? "The selected playback device is no longer available. Right-click the app and choose a different system audio device."
            : "The selected microphone is no longer available. Right-click the app and choose a different microphone.";
    }

    public static string EndpointMismatchMessage(EDataFlow dataFlow)
    {
        return dataFlow == EDataFlow.Render
            ? "The system audio endpoint no longer matches the selected device. Recording aborted."
            : "The microphone endpoint no longer matches the selected device. Recording aborted.";
    }
}