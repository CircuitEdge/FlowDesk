using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace FlowDeck.Services;

internal sealed class AppDiscoveryService
{
    public IReadOnlyList<DiscoveredApp> Discover()
    {
        var roots = new List<string>();
        AddExistingFolder(roots, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu));
        AddExistingFolder(roots, Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));
        AddExistingFolder(roots, Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        AddExistingFolder(roots, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
        AddExistingFolder(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddExistingFolder(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        AddExistingFolder(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"));

        var shortcuts = roots.SelectMany(root => SafeEnumerateFiles(root, [".lnk"]));
        var executables = roots
            .Where(root => !root.Contains("Start Menu") && !root.Contains("Desktop", StringComparison.OrdinalIgnoreCase))
            .SelectMany(root => SafeEnumerateFiles(root, [".exe"]));

        var found = new List<DiscoveredApp>();
        foreach (var path in shortcuts.Concat(executables))
        {
            var extension = Path.GetExtension(path);
            var name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(name) || IsUtilityName(name))
                continue;

            found.Add(new DiscoveredApp(name, path, extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)));
        }

        found.AddRange(FindAppPathRegistryApps());
        found.AddRange(FindMicrosoftStoreApps());
        found.AddRange(FindSteamGames());

        return found
            .GroupBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsUtilityName(string name) =>
        name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)
        || name.Contains("setup", StringComparison.OrdinalIgnoreCase)
        || name.Contains("installer", StringComparison.OrdinalIgnoreCase)
        || name.Contains("crash handler", StringComparison.OrdinalIgnoreCase)
        || name.Contains("prerequisite", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<DiscoveredApp> FindAppPathRegistryApps()
    {
        var found = new List<DiscoveredApp>();
        foreach (var baseKey in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var root in new[]
                     {
                         @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths",
                         @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths"
                     })
            {
                try
                {
                    using var key = baseKey.OpenSubKey(root);
                    if (key == null)
                        continue;

                    foreach (var executable in key.GetSubKeyNames())
                    {
                        using var appKey = key.OpenSubKey(executable);
                        var path = appKey?.GetValue(null)?.ToString()?.Trim('"');
                        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                            found.Add(new DiscoveredApp(Path.GetFileNameWithoutExtension(executable), path, false));
                    }
                }
                catch
                {
                }
            }
        }

        return found;
    }

    private static IEnumerable<DiscoveredApp> FindMicrosoftStoreApps()
    {
        List<DiscoveredApp>? found = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                found = EnumerateMicrosoftStoreApps();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true,
            Name = "FlowDeck Store app discovery"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!thread.Join(TimeSpan.FromSeconds(5)))
        {
            Debug.WriteLine("Microsoft Store app discovery timed out.");
            return [];
        }

        if (failure != null)
        {
            Debug.WriteLine($"Microsoft Store app discovery failed: {failure}");
            return [];
        }

        return found ?? [];
    }

    private static List<DiscoveredApp> EnumerateMicrosoftStoreApps()
    {
        var found = new List<DiscoveredApp>();
        object? shell = null;
        object? folder = null;
        object? items = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null)
                return found;

            shell = Activator.CreateInstance(shellType);
            if (shell == null)
                return found;

            folder = InvokeShellMember(shell, "NameSpace", BindingFlags.InvokeMethod, "shell:AppsFolder");
            if (folder == null)
                return found;

            items = InvokeShellMember(folder, "Items", BindingFlags.InvokeMethod);
            if (items == null)
                return found;

            var count = Convert.ToInt32(
                InvokeShellMember(items, "Count", BindingFlags.GetProperty),
                System.Globalization.CultureInfo.InvariantCulture);
            for (var index = 0; index < count; index++)
            {
                object? item = null;
                try
                {
                    item = InvokeShellMember(items, "Item", BindingFlags.GetProperty, index);
                    if (item == null)
                        continue;

                    var name = InvokeShellMember(item, "Name", BindingFlags.GetProperty)?.ToString()?.Trim();
                    var path = InvokeShellMember(item, "Path", BindingFlags.GetProperty)?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(path))
                        found.Add(new DiscoveredApp(name, $"shell:AppsFolder\\{path}", false));
                }
                finally
                {
                    ReleaseComObject(item);
                }
            }
        }
        finally
        {
            ReleaseComObject(items);
            ReleaseComObject(folder);
            ReleaseComObject(shell);
        }

        return found;
    }

    private static object? InvokeShellMember(
        object target,
        string member,
        BindingFlags operation,
        params object?[] arguments) =>
        target.GetType().InvokeMember(
            member,
            operation | BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            target,
            arguments,
            System.Globalization.CultureInfo.InvariantCulture);

    private static void ReleaseComObject(object? value)
    {
        if (value != null && Marshal.IsComObject(value))
            Marshal.ReleaseComObject(value);
    }

    private static IEnumerable<DiscoveredApp> FindSteamGames()
    {
        string? steamPath = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath")?.ToString();
        if (string.IsNullOrWhiteSpace(steamPath) || !Directory.Exists(steamPath))
            yield break;

        var libraryFolders = new List<string> { steamPath.Replace('/', '\\') };
        var libraryFile = Path.Combine(steamPath, "config", "libraryfolders.vdf");
        if (File.Exists(libraryFile))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(libraryFile), "\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
                libraryFolders.Add(match.Groups[1].Value.Replace("\\\\", "\\"));
        }

        foreach (var library in libraryFolders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var steamApps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamApps))
                continue;

            foreach (var manifest in Directory.GetFiles(steamApps, "appmanifest_*.acf"))
            {
                var appId = Path.GetFileNameWithoutExtension(manifest)["appmanifest_".Length..];
                string text;
                try
                {
                    text = File.ReadAllText(manifest);
                }
                catch
                {
                    continue;
                }

                var nameMatch = Regex.Match(text, "\"name\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                if (nameMatch.Success && !string.IsNullOrWhiteSpace(nameMatch.Groups[1].Value))
                    yield return new DiscoveredApp(nameMatch.Groups[1].Value, $"steam://rungameid/{appId}", false);
            }
        }
    }

    private static void AddExistingFolder(List<string> folders, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            folders.Add(path);
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root, string[] extensions)
    {
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        var maxDepth = extensions.Contains(".lnk", StringComparer.OrdinalIgnoreCase) ? 8 : 3;

        while (pending.Count > 0)
        {
            var (folder, depth) = pending.Pop();
            string[] files = [];
            string[] directories = [];
            try
            {
                files = Directory.GetFiles(folder);
                if (depth < maxDepth)
                    directories = Directory.GetDirectories(folder);
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
            catch (System.Security.SecurityException)
            {
            }

            foreach (var file in files)
            {
                if (extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    yield return file;
            }

            foreach (var child in directories)
                pending.Push((child, depth + 1));
        }
    }
}
