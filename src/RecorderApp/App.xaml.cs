using System.Diagnostics;
using RecorderApp.Models;
using RecorderApp.Services;
using RecorderApp.ViewModels;
using RecorderApp.Views;

namespace RecorderApp;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Global\RecorderApp.SingleInstance";
    private const string ActivateWindowEventName = @"Global\RecorderApp.ActivateMainWindow";
    private const string TrayStartupArgument = "--tray-startup";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activateWindowEvent;
    private CancellationTokenSource? _activationListenerCts;
    private Task? _activationListenerTask;
    private RecordingCoordinator? _recordingCoordinator;
    private MainViewModel? _mainViewModel;
    private FileLogger? _logger;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var startHiddenInTray = e.Args.Any(arg => string.Equals(arg, TrayStartupArgument, StringComparison.OrdinalIgnoreCase));
        EnsureWatchDogRunning();

        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            try
            {
                using var activateEvent = EventWaitHandle.OpenExisting(ActivateWindowEventName);
                activateEvent.Set();
            }
            catch
            {
            }

            Shutdown();
            return;
        }

        _activateWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateWindowEventName);
        _activationListenerCts = new CancellationTokenSource();
        _activationListenerTask = Task.Run(() => ListenForActivationRequestsAsync(_activationListenerCts.Token));

        _logger = new FileLogger();
        _logger.Info("Application startup begin.");
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        try
        {
            var settingsStore = new JsonSettingsStore();
            var settings = settingsStore.Load();
            AppPaths.EnsureDirectories(settings.StoragePath);

            var startupRegistrationService = new StartupRegistrationService();
            startupRegistrationService.Apply(settings.AutoStartWithWindows, stopRunningWatchDogWhenDisabled: false);
            var ffmpegLocator = new FFmpegLocator();
            var ffmpegBootstrapper = new FFmpegBootstrapper(ffmpegLocator, _logger);
            var bootstrapResult = await ffmpegBootstrapper.EnsureAvailableAsync(CancellationToken.None);
            _logger.Info(bootstrapResult.Message);
            var capabilityService = new FFmpegCapabilityService(_logger);
            var commandBuilder = new FFmpegCommandBuilder();
            var audioCaptureService = new AudioLoopbackCaptureService(_logger);
            var segmentPlanner = new SegmentPlanner();
            var recoveryService = new RecordingRecoveryService(settingsStore, _logger);
            var cleanupService = new CleanupService(settingsStore, _logger);
            var audioDeviceService = new AudioDeviceService();
            var coordinator = new RecordingCoordinator(
                settingsStore,
                _logger,
                ffmpegLocator,
                capabilityService,
                commandBuilder,
                audioCaptureService,
                segmentPlanner,
                recoveryService,
                cleanupService);

            _recordingCoordinator = coordinator;
            var viewModel = new MainViewModel(
                settings,
                settingsStore,
                startupRegistrationService,
                coordinator,
                audioDeviceService,
                ffmpegBootstrapper,
                bootstrapResult.Message,
                _logger);
            _mainViewModel = viewModel;

            var mainWindow = new MainWindow(viewModel);
            MainWindow = mainWindow;
            if (startHiddenInTray)
            {
                mainWindow.PrepareForTrayOnlyStartup();
                _logger.Info("Application started in tray-only mode.");
            }
            else
            {
                mainWindow.Show();
                _logger.Info("Main window shown.");
            }

            if (settings.AutoStartRecording && bootstrapResult.IsAvailable)
            {
                await viewModel.StartRecordingAsync();
            }
        }
        catch (Exception exception)
        {
            _logger.Error("Startup failed.", exception);
            throw;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_mainViewModel is not null)
        {
            _mainViewModel.StopForApplicationExitAsync().GetAwaiter().GetResult();
        }

        _logger?.Info("Application exited.");
        _activationListenerCts?.Cancel();
        _activateWindowEvent?.Set();
        try
        {
            _activationListenerTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
        }

        _activateWindowEvent?.Dispose();
        _activationListenerCts?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        try
        {
            if (MainWindow is MainWindow mainWindow)
            {
                mainWindow.PrepareForSystemShutdown();
            }

            _logger?.Info($"Windows session is ending: {e.ReasonSessionEnding}");
            _mainViewModel?.StopForSystemShutdownAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            _logger?.Error("Failed while handling Windows session ending.", exception);
        }

        base.OnSessionEnding(e);
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.Error("Unhandled dispatcher exception.", e.Exception);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            _logger?.Error("Unhandled application exception.", exception);
        }
    }

    private async Task ListenForActivationRequestsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _activateWindowEvent is not null)
        {
            _activateWindowEvent.WaitOne();
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (MainWindow is MainWindow mainWindow)
                {
                    mainWindow.BringToFrontFromExternalLaunch();
                }
            });
        }
    }

    private static void EnsureWatchDogRunning()
    {
        try
        {
            var baseDirectory = AppContext.BaseDirectory;
            var watchDogPath = Path.Combine(baseDirectory, "WatchDog.exe");
            var mainExecutablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(mainExecutablePath) || !File.Exists(watchDogPath))
            {
                return;
            }

            var watchDogProcesses = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(watchDogPath));
            foreach (var process in watchDogProcesses)
            {
                try
                {
                    var processPath = process.MainModule?.FileName;
                    if (string.Equals(processPath, watchDogPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
                catch
                {
                }
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = watchDogPath,
                Arguments = $"\"{mainExecutablePath}\"",
                UseShellExecute = true,
                WorkingDirectory = baseDirectory,
            });
        }
        catch
        {
        }
    }
}
