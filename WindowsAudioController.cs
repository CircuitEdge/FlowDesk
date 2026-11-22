using System.Runtime.InteropServices;

namespace FlowDeck;

internal static class WindowsAudioController
{
    private const int ClsctxAll = 23;

    public static float GetVolume() =>
        WithEndpointVolume(endpoint =>
        {
            Marshal.ThrowExceptionForHR(endpoint.GetMasterVolumeLevelScalar(out var volume));
            return volume;
        });

    public static void SetVolume(float volume) =>
        WithEndpointVolume(endpoint =>
        {
            var eventContext = Guid.Empty;
            Marshal.ThrowExceptionForHR(endpoint.SetMasterVolumeLevelScalar(volume, ref eventContext));
            return true;
        });

    public static bool GetMute() =>
        WithEndpointVolume(endpoint =>
        {
            Marshal.ThrowExceptionForHR(endpoint.GetMute(out var isMuted));
            return isMuted;
        });

    public static void SetMute(bool isMuted) =>
        WithEndpointVolume(endpoint =>
        {
            var eventContext = Guid.Empty;
            Marshal.ThrowExceptionForHR(endpoint.SetMute(isMuted, ref eventContext));
            return true;
        });

    private static T WithEndpointVolume<T>(Func<IAudioEndpointVolume, T> action)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        IMMDevice? device = null;
        object? endpointObject = null;
        IAudioEndpointVolume? endpointVolume = null;

        try
        {
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console, out device));
            var interfaceId = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref interfaceId, ClsctxAll, IntPtr.Zero, out endpointObject));
            endpointVolume = (IAudioEndpointVolume)endpointObject;
            return action(endpointVolume);
        }
        finally
        {
            if (endpointVolume != null)
                Marshal.ReleaseComObject(endpointVolume);
            else if (endpointObject != null && Marshal.IsComObject(endpointObject))
                Marshal.ReleaseComObject(endpointObject);

            if (device != null)
                Marshal.ReleaseComObject(device);

            Marshal.ReleaseComObject(enumerator);
        }
    }

    private enum DataFlow
    {
        Render,
        Capture,
        All
    }

    private enum Role
    {
        Console,
        Multimedia,
        Communications
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(DataFlow dataFlow, uint stateMask, out IntPtr devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(DataFlow dataFlow, Role role, out IMMDevice endpoint);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr client);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(
            ref Guid interfaceId,
            int classContext,
            IntPtr activationParameters,
            [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);

        [PreserveSig]
        int OpenPropertyStore(int access, out IntPtr properties);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig]
        int GetState(out uint state);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint channelCount);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool isMuted, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool isMuted);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
        [PreserveSig] int VolumeStepUp(ref Guid eventContext);
        [PreserveSig] int VolumeStepDown(ref Guid eventContext);
        [PreserveSig] int QueryHardwareSupport(out uint hardwareSupportMask);
        [PreserveSig] int GetVolumeRange(out float volumeMinDb, out float volumeMaxDb, out float volumeIncrementDb);
    }
}
