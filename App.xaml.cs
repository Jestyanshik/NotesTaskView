using System.IO;
using System.Windows;
using System.Windows.Threading;
using NotesTaskView.Services;
using NotesTaskView.Views;

namespace NotesTaskView;

public partial class App : Application
{
    private ConfigService? _configService;
    private UserSettingsService? _userSettingsService;
    private UserSettings? _userSettings;
    private NoteService? _noteService;
    private HotkeyManager? _hotkeyManager;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += App_OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_OnUnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_OnUnobservedTaskException;

        base.OnStartup(e);

        try
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _configService = new ConfigService();
            _userSettingsService = new UserSettingsService();
            var appConfig = _configService.Load();
            _userSettings = _userSettingsService.Load(appConfig);
            appConfig.NotesFolderPath = _userSettings.NotesDirectory;

            _noteService = new NoteService(appConfig);
            _mainWindow = new MainWindow(_noteService, _userSettingsService, _userSettings, ApplySettings);
            MainWindow = _mainWindow;

            _hotkeyManager = new HotkeyManager();
            _hotkeyManager.HotkeyPressed += OnHotkeyPressed;

            var registerErrors = _hotkeyManager.Register(_userSettings);
            if (registerErrors.Count > 0)
            {
                _ = _mainWindow.ShowOverlayErrorAsync("NotesTaskView", string.Join(Environment.NewLine, registerErrors));
            }
        }
        catch (Exception ex)
        {
            LogException("Fatal startup exception", ex);
            Shutdown(1);
        }
    }

    private List<string> ApplySettings(UserSettings settings)
    {
        if (_userSettingsService is null || _hotkeyManager is null || _noteService is null || _userSettings is null)
        {
            return ["Настройки пока недоступны."];
        }

        var registerErrors = _hotkeyManager.Register(settings);
        if (registerErrors.Count > 0)
        {
            _hotkeyManager.Register(_userSettings);
            return registerErrors;
        }

        _userSettingsService.Save(settings);
        _userSettings = settings;
        _noteService.UpdateNotesFolder(settings.NotesDirectory);
        return [];
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_hotkeyManager is not null)
        {
            _hotkeyManager.HotkeyPressed -= OnHotkeyPressed;
            _hotkeyManager.Dispose();
        }

        _noteService?.Dispose();
        base.OnExit(e);
    }

    private void App_OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException("DispatcherUnhandledException", e.Exception);
        if (_mainWindow is not null)
        {
            _ = _mainWindow.ShowOverlayErrorAsync("Ошибка", e.Exception.Message);
            e.Handled = true;
        }
    }

    private static void CurrentDomain_OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogException("UnhandledException", ex);
        }
        else
        {
            AppendCrashLog($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] UnhandledException: {e.ExceptionObject}");
        }
    }

    private static void TaskScheduler_OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException("UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private static void LogException(string title, Exception exception)
    {
        AppendCrashLog($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {title}{Environment.NewLine}{exception}{Environment.NewLine}");
    }

    private static void AppendCrashLog(string text)
    {
        try
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"), $"{text}{Environment.NewLine}");
        }
        catch
        {
            // Last-chance logging must never throw during crash handling.
        }
    }

    private async void OnHotkeyPressed(object? sender, HotkeyPressedEventArgs e)
    {
        if (_mainWindow is null)
        {
            return;
        }

        switch (e.HotkeyId)
        {
            case HotkeyManager.ToggleWindowHotkeyId:
                await _mainWindow.ToggleVisibilityAsync();
                break;
            case HotkeyManager.CreateNoteHotkeyId:
                await _mainWindow.CreateNoteFromDialogAsync();
                break;
        }
    }
}
