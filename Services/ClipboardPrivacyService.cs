using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace FlowDeck.Services;

internal sealed class ClipboardPrivacyService
{
    private const string ExcludeFromMonitoringFormat = "ExcludeClipboardContentFromMonitorProcessing";

    public uint GetClipboardSequenceNumber() => GetClipboardSequenceNumberNative();

    public string? ReadAllowedText(IReadOnlySet<string> excludedApplications)
    {
        var sequenceBeforeRead = GetClipboardSequenceNumberNative();
        var data = Clipboard.GetDataObject();
        if (data?.GetDataPresent(ExcludeFromMonitoringFormat, autoConvert: false) == true)
            return null;

        if (excludedApplications.Count > 0)
        {
            var ownerProcessName = GetClipboardOwnerProcessName();
            if (ownerProcessName == null || excludedApplications.Contains(ownerProcessName))
                return null;
        }

        var text = Clipboard.GetText();
        return GetClipboardSequenceNumberNative() == sequenceBeforeRead ? text : null;
    }

    private static string? GetClipboardOwnerProcessName()
    {
        var ownerWindow = GetClipboardOwner();
        if (ownerWindow == IntPtr.Zero || GetWindowThreadProcessId(ownerWindow, out var processId) == 0)
            return null;

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardOwner();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetClipboardSequenceNumber")]
    private static extern uint GetClipboardSequenceNumberNative();
}
