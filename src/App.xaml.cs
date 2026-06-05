using System.Diagnostics;
using System.IO;
using System.Windows;
using FluxTranslator.Core;
using FluxTranslator.TrayIcon;
using FluxTranslator.Views;

namespace FluxTranslator;

public partial class App : Application
{
    private const string SingleInstanceMutexName = "FluxTranslator_SingleInstance_Mutex";
    private Mutex?           _singleInstanceMutex;
    private TrayIconManager? _tray;
    private MainWindow?      _mainWindow;
    private Process?         _fluxHelperProcess;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, name: SingleInstanceMutexName, createdNew: out var createdNew);
        if (!createdNew)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        AppLogger.Initialise();

        AccentColorHelper.Apply(Resources);

        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
            AppLogger.Error($"[UnhandledException] {ev.ExceptionObject}");

        DispatcherUnhandledException += (_, ev) =>
        {
            AppLogger.Error($"[DispatcherUnhandled] {ev.Exception}");
            ev.Handled = true;
            MessageBox.Show(
                $"An unexpected error occurred:\n\n{ev.Exception.Message}\n\nSee log.txt for details.",
                AppSettings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        };

        base.OnStartup(e);

        StartFluxHelper();

        _tray = new TrayIconManager();
        _tray.ShowRequested += OnTrayShow;
        _tray.ExitRequested += OnTrayExit;

        _mainWindow = new MainWindow();
        MainWindow  = _mainWindow;
        _mainWindow.Show();
    }

    private void StartFluxHelper()
    {
        try
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var fluxHelperPath = Path.Combine(appDir, "FluxHelper.exe");

            if (!File.Exists(fluxHelperPath))
            {
                AppLogger.Warn($"FluxHelper.exe not found at: {fluxHelperPath}");
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName        = fluxHelperPath,
                UseShellExecute = false,
                CreateNoWindow  = true,
                WorkingDirectory = appDir,
            };

            psi.EnvironmentVariables["FLUXTRANSLATOR_PID"] = Environment.ProcessId.ToString();

            _fluxHelperProcess = new Process
            {
                StartInfo = psi,
                EnableRaisingEvents = true,
            };

            _fluxHelperProcess.Exited += (_, _) =>
                AppLogger.Info("FluxHelper process exited.");

            _fluxHelperProcess.Start();
            AppLogger.Info($"FluxHelper started (PID: {_fluxHelperProcess.Id})");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to start FluxHelper: {ex.Message}");
        }
    }

    private void StopFluxHelper()
    {
        if (_fluxHelperProcess is null)
            return;

        try
        {
            if (!_fluxHelperProcess.HasExited)
            {
                AppLogger.Info("Stopping FluxHelper backend...");
                _fluxHelperProcess.Kill();
                _fluxHelperProcess.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Error stopping FluxHelper: {ex.Message}");
        }
        finally
        {
            _fluxHelperProcess.Dispose();
            _fluxHelperProcess = null;
        }
    }

    private void OnTrayShow()
    {
        Dispatcher.Invoke(() =>
        {
            if (_mainWindow is null) return;
            _mainWindow.Show();
            if (_mainWindow.WindowState == WindowState.Minimized)
                _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        });
    }

    private void OnTrayExit()
    {
        Dispatcher.Invoke(() =>
        {
            _tray?.Dispose();
            _mainWindow?.RequestExit();
            Shutdown();
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        StopFluxHelper();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        AppLogger.Info("Application exiting.");
        AppLogger.Close();
        base.OnExit(e);
    }
}
