// ==============================================================================
// App.xaml.cs - WPF Application entry point
// Manages application lifecycle, tray icon, and overlay window.
// ==============================================================================
//
// [CHANGE LOG]
// 2026-01-07 - AI - Added splash screen for immediate visual feedback during startup
// 2026-01-05 - AI - Fixed mutex disposal bug that allowed multiple instances
// ==============================================================================

using System.IO;
using System.Text.Json;
using System.Windows;

namespace PowerSwitchOverlay;

public partial class App : System.Windows.Application
{
    private TrayIconService? _trayService;
    private MainWindow? _overlayWindow;
    // WHY: Mutex must live for entire app lifetime to prevent multiple instances
    private System.Threading.Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Ensure only one instance runs at a time
        // WHY: Don't use 'using' - mutex must persist until OnExit
        _singleInstanceMutex = new System.Threading.Mutex(true, "PowerSwitchOverlay_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show("Power Switch Overlay is already running.", "Already Running",
                MessageBoxButton.OK, MessageBoxImage.Information);
            _singleInstanceMutex = null;  // Don't dispose, we don't own it
            Shutdown();
            return;
        }

        // WHY: Show splash immediately at saved position for instant visual feedback
        var splash = new SplashWindow();
        var (left, top) = LoadSavedPosition();
        splash.SetPosition(left, top);
        splash.Show();
        
        // WHY: Force render of splash before heavy initialization
        splash.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);

        // Create the overlay window (transparent, always-on-top)
        // This is where the heavy initialization happens
        _overlayWindow = new MainWindow();
        _overlayWindow.Show();

        // Close splash after main window is ready
        splash.CloseSplash();

        // Create the system tray icon service
        _trayService = new TrayIconService(_overlayWindow);
        
        // WHY: Allow Ctrl+Right-Click on overlay to show context menu
        _overlayWindow.TrayService = _trayService;
    }
    
    /// <summary>
    /// Loads saved window position from settings file.
    /// WHY: Splash appears at same position where overlay will appear for seamless transition.
    /// </summary>
    private static (double left, double top) LoadSavedPosition()
    {
        try
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var settingsPath = Path.Combine(appDataPath, "PowerSwitchOverlay", "settings.json");
            
            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                double left = root.TryGetProperty("WindowLeft", out var l) ? l.GetDouble() : 100;
                double top = root.TryGetProperty("WindowTop", out var t) ? t.GetDouble() : 100;
                
                return (left, top);
            }
        }
        catch
        {
            // Fall back to defaults on any error
        }
        
        return (100, 100);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayService?.Dispose();
        // WHY: Release mutex on exit so app can be restarted
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
