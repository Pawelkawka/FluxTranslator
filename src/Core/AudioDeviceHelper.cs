using System.Collections.Generic;
using NAudio.CoreAudioApi;

namespace FluxTranslator.Core;

public static class AudioDeviceHelper
{
    public static AudioDeviceInfo[] GetOutputDevices()
    {
        var devices = new List<AudioDeviceInfo>();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var deviceCollection = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            var defaultDeviceId = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)?.ID;

            foreach (var device in deviceCollection)
            {
                devices.Add(new AudioDeviceInfo(
                    Id: device.ID,
                    Name: device.FriendlyName,
                    IsDefault: device.ID == defaultDeviceId
                ));

                device.Dispose();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Could not enumerate audio devices: {ex.Message}");
        }

        return devices.ToArray();
    }
}

public record AudioDeviceInfo(string Id, string Name, bool IsDefault);
