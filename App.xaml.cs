using System.ComponentModel;
using System.Diagnostics;
using System.Data;
using System.IO;
using System.Windows;
using FlowDeck.Services;

namespace FlowDeck;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private SingleInstanceService? _singleInstance;
    private string[] _startupArguments = [];
    private bool _restartRequested;

    protected override void OnStartup(StartupEventArgs e)
    {
        _startupArguments = e.Args;
        DispatcherUnhandledException += (_, args) =>
        {
            LogException("UI", args.Exception);

            var result = MessageBox.Show(
                $"FlowDeck encountered an unexpected error:\n\n{args.Exception.Message}\n\nRestart FlowDeck now?",
                "FlowDeck Error",
                MessageBoxButton.YesNo,
                MessageBoxImage.Error);
            _restartRequested = result == MessageBoxResult.Yes;
            args.Handled = true;
            Shutdown();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                LogException("AppDomain", exception);
                MessageBox.Show(
                    $"A fatal error caused FlowDeck to close:\n\n{exception.Message}",
                    "FlowDeck Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };

        base.OnStartup(e);

        _singleInstance = new SingleInstanceService();
        var restartWait = _startupArguments.Contains("--restart", StringComparer.OrdinalIgnoreCase)
            ? TimeSpan.FromSeconds(15)
            : TimeSpan.Zero;
        if (!_singleInstance.TryAcquirePrimaryInstance(restartWait))
        {
            _singleInstance.NotifyPrimaryInstance();
            Shutdown();
            return;
        }

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        _singleInstance.StartListening(mainWindow.ActivateFromSecondInstance);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        _singleInstance = null;
        if (_restartRequested)
            RestartApplication();

        base.OnExit(e);
    }

    private void RestartApplication()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            MessageBox.Show(
                "FlowDeck could not be restarted because its executable path is unavailable.",
                "FlowDeck Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = true
            };
            foreach (var argument in _startupArguments)
                startInfo.ArgumentList.Add(argument);
            startInfo.ArgumentList.Add("--restart");
            Process.Start(startInfo);
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or IOException)
        {
            LogException("Restart", exception);
            MessageBox.Show(
                $"FlowDeck could not be restarted:\n\n{exception.Message}",
                "FlowDeck Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static void LogException(string source, Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FlowDeck");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "crash.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{source}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception loggingException) when (
            loggingException is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Debug.WriteLine($"Could not write the FlowDeck crash log: {loggingException}");
        }
    }
}
