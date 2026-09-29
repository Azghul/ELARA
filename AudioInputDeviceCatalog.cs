namespace SimpleAudioRecorder;

internal readonly record struct AudioDeviceInfo(string Id, string DisplayName, bool IsDefault)
{
    public string MenuLabel => IsDefault ? $"{DisplayName} (Default)" : DisplayName;
}

internal static class AudioInputDeviceCatalog
{
    private static readonly PropertyKey FriendlyNameKey = new(
        new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        14);

    public static IReadOnlyList<AudioDeviceInfo> GetMicrophones()
    {
        return GetDevices(EDataFlow.Capture);
    }

    public static IReadOnlyList<AudioDeviceInfo> GetPlaybackDevices()
    {
        return GetDevices(EDataFlow.Render);
    }

    private static IReadOnlyList<AudioDeviceInfo> GetDevices(EDataFlow dataFlow)
    {
        using var _ = CoreAudioInterop.EnterComScope(CoreAudioInterop.COINIT_APARTMENTTHREADED);

        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        IMMDevice? defaultDevice = null;

        try
        {
            enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(typeof(MMDeviceEnumeratorComObject))!;
            collection = enumerator.EnumAudioEndpoints(dataFlow, DeviceState.Active);

            string? defaultId = null;
            try
            {
                defaultDevice = enumerator.GetDefaultAudioEndpoint(dataFlow, ERole.Multimedia);
                defaultId = defaultDevice.GetId();
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Could not resolve the default {(dataFlow == EDataFlow.Render ? "playback" : "microphone")} device. {ex.Message}");
            }

            collection.GetCount(out var deviceCount);
            var devices = new List<AudioDeviceInfo>(deviceCount);

            for (var index = 0; index < deviceCount; index++)
            {
                IMMDevice? device = null;
                try
                {
                    device = collection.Item(index);
                    var id = device.GetId();
                    var name = GetFriendlyName(device);
                    devices.Add(new AudioDeviceInfo(id, name, string.Equals(id, defaultId, StringComparison.Ordinal)));
                }
                finally
                {
                    CoreAudioInterop.ReleaseComObject(device);
                }
            }

            devices.Sort(static (left, right) =>
            {
                if (left.IsDefault != right.IsDefault)
                {
                    return left.IsDefault ? -1 : 1;
                }

                return StringComparer.CurrentCultureIgnoreCase.Compare(left.DisplayName, right.DisplayName);
            });

            AppLogger.Info(
                devices.Count == 0
                    ? $"Enumerated {(dataFlow == EDataFlow.Render ? "playback devices" : "microphones")}: none"
                    : $"Enumerated {(dataFlow == EDataFlow.Render ? "playback devices" : "microphones")}: " + string.Join(" | ", devices.Select(device => $"{device.DisplayName} [{device.Id}]")));

            return devices;
        }
        finally
        {
            CoreAudioInterop.ReleaseComObject(defaultDevice);
            CoreAudioInterop.ReleaseComObject(collection);
            CoreAudioInterop.ReleaseComObject(enumerator);
        }
    }

    public static string GetFriendlyName(IMMDevice device)
    {
        IPropertyStore? propertyStore = null;
        PropVariant propVariant = default;

        try
        {
            propertyStore = device.OpenPropertyStore(StorageAccessMode.Read);
            if (propertyStore is null)
            {
                AppLogger.Warn("Could not open the property store of an audio device; falling back to its endpoint id.");
                return device.GetId();
            }

            var friendlyNameKey = FriendlyNameKey;
            propertyStore.GetValue(ref friendlyNameKey, out propVariant);

            return propVariant.GetString()?.Trim() switch
            {
                { Length: > 0 } name => name,
                _ => device.GetId(),
            };
        }
        catch (Exception ex)
        {
            // Never fail device enumeration or capture startup just because the
            // friendly name could not be read; the endpoint id is a safe fallback.
            AppLogger.Warn($"Could not read the friendly name of an audio device; falling back to its endpoint id. {ex.Message}");
            return device.GetId();
        }
        finally
        {
            try
            {
                CoreAudioInterop.ClearPropVariant(ref propVariant);
            }
            catch
            {
            }

            CoreAudioInterop.ReleaseComObject(propertyStore);
        }
    }
}
