using Microsoft.Win32;

namespace FlowDeck.Services;

internal sealed class WindowsStartupService
{
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RegistryValueName = "FlowDeck";

    public bool IsEnabled()
    {
        try
        {
            using var startupKey = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: false);
            return startupKey?.GetValue(RegistryValueName) != null;
        }
        catch
        {
            return false;
        }
    }

    public void Configure(string action)
    {
        using var startupKey = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RegistryKeyPath);

        if (action == "off" || action == "disable")
        {
            startupKey?.DeleteValue(RegistryValueName, throwOnMissingValue: false);
            return;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
            throw new InvalidOperationException("Could not determine the FlowDeck executable path.");

        startupKey?.SetValue(RegistryValueName, $"\"{executable}\" --startup");
    }

}
