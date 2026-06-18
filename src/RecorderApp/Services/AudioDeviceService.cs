using NAudio.CoreAudioApi;
using RecorderApp.Models;

namespace RecorderApp.Services;

public sealed class AudioDeviceService
{
    public IReadOnlyList<AudioDeviceOption> GetPlaybackDevices()
    {
        var result = new List<AudioDeviceOption>();

        using var enumerator = new MMDeviceEnumerator();
        using var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var defaultId = defaultDevice.ID;

        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
            {
                result.Add(new AudioDeviceOption
                {
                    DisplayName = device.ID == defaultId ? $"{device.FriendlyName}(默认)" : device.FriendlyName,
                    InputName = device.FriendlyName,
                    IsDefault = device.ID == defaultId,
                });
            }
        }

        return result;
    }
}
