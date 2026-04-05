namespace SimpleAudioRecorder;

internal readonly record struct AudioInputDeviceInfo(string Id, string DisplayName, bool IsDefault)
{
    public string MenuLabel => IsDefault ? $"{DisplayName} (Default)" : DisplayName;
}

internal static class AudioInputDeviceCatalog
{
    private static readonly PropertyKey FriendlyNameKey = new(
        new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        14);

    public static IReadOnlyList<AudioInputDeviceInfo> GetMicrophones()
    {
        using var _ = CoreAudioInterop.EnterComScope(CoreAudioInterop.COINIT_APARTMENTTHREADED);

        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        IMMDevice? defaultDevice = null;

        try
        {
            enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(typeof(MMDeviceEnumeratorComObject))!;
            collection = enumerator.EnumAudioEndpoints(EDataFlow.Capture, DeviceState.Active);

            string? defaultId = null;
            try
            {
                defaultDevice = enumerator.GetDefaultAudioEndpoint(EDataFlow.Capture, ERole.Multimedia);
                defaultId = defaultDevice.GetId();
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Could not resolve the default microphone. {ex.Message}");
            }

            collection.GetCount(out var deviceCount);
            var devices = new List<AudioInputDeviceInfo>(deviceCount);

            for (var index = 0; index < deviceCount; index++)
            {
                IMMDevice? device = null;
                try
                {
                    device = collection.Item(index);
                    var id = device.GetId();
                    var name = GetFriendlyName(device);
                    devices.Add(new AudioInputDeviceInfo(id, name, string.Equals(id, defaultId, StringComparison.Ordinal)));
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
                    ? "Enumerated microphones: none"
                    : "Enumerated microphones: " + string.Join(" | ", devices.Select(device => $"{device.DisplayName} [{device.Id}]")));

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
            var friendlyNameKey = FriendlyNameKey;
            propertyStore.GetValue(ref friendlyNameKey, out propVariant);

            return propVariant.GetString()?.Trim() switch
            {
                { Length: > 0 } name => name,
                _ => device.GetId(),
            };
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
