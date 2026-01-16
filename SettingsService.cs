// ==============================================================================
// SettingsService.cs - User settings persistence
// Manages saving/loading of user preferences (window position, display mode, etc.)
// WHY: Stores settings in a JSON file in LocalAppData to persist across restarts.
// ==============================================================================
//
// [CHANGE LOG]
// 2026-01-07 - AI - Added HasSeenTutorial for first-launch onboarding
// 2026-01-05 - AI - Created SettingsService for position and display mode persistence
// 2026-01-05 - AI - Converted AppSettings to record struct, added readonly
// 2026-01-06 - AI - Added UptimeTrackingStartTime to persist "Since 100%" counter
// 2026-01-06 - AI - Changed to UptimeAccumulatedSeconds for accurate active-time tracking
// ==============================================================================

using System.IO;
using System.Text.Json;
using System.Windows;

namespace PowerSwitchOverlay;

/// <summary>
/// Represents the user's saved settings.
/// WHY: Record struct provides value semantics, stack allocation, and built-in equality.
/// </summary>
public record struct AppSettings
{
    /// <summary>Window left position in screen coordinates.</summary>
    public double WindowLeft { get; set; }
    
    /// <summary>Window top position in screen coordinates.</summary>
    public double WindowTop { get; set; }
    
    /// <summary>Current display mode (Micro, MicroTime, Compact, Standard, Advanced).</summary>
    public DisplayMode DisplayMode { get; set; }
    
    /// <summary>Overlay opacity (0-100).</summary>
    public int Opacity { get; set; }
    
    /// <summary>
    /// Accumulated active seconds since last 100% charge.
    /// Zero if not currently tracking.
    /// WHY: Stores active time (excluding sleep) so tracking persists accurately across restarts.
    /// </summary>
    public double UptimeAccumulatedSeconds { get; set; }
    
    /// <summary>
    /// True if user has seen the first-launch tutorial.
    /// WHY: Only show onboarding tips once.
    /// </summary>
    public bool HasSeenTutorial { get; set; }
    
    /// <summary>
    /// Creates default settings.
    /// WHY: Record structs need explicit default constructor for JSON deserialization.
    /// </summary>
    public AppSettings()
    {
        WindowLeft = 100;
        WindowTop = 100;
        DisplayMode = DisplayMode.Standard;
        Opacity = 100;
        UptimeAccumulatedSeconds = 0;
        HasSeenTutorial = false;
    }
}

/// <summary>
/// Service for persisting and loading user settings to/from a JSON file.
/// WHY: Provides a centralized location for settings management with graceful fallback to defaults.
/// </summary>
public class SettingsService
{
    private readonly string _settingsPath;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };
    
    /// <summary>
    /// Current settings loaded from disk or defaults.
    /// </summary>
    public AppSettings Settings { get; private set; }
    
    /// <summary>
    /// Initializes the settings service and loads existing settings.
    /// WHY: Creates settings directory if it doesn't exist and loads or creates default settings.
    /// </summary>
    public SettingsService()
    {
        // WHY: Store in LocalAppData for user-specific, persistent storage
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appDataPath, "PowerSwitchOverlay");
        
        // Ensure directory exists
        Directory.CreateDirectory(appFolder);
        
        _settingsPath = Path.Combine(appFolder, "settings.json");
        Settings = Load();
    }
    
    /// <summary>
    /// Loads settings from the JSON file.
    /// WHY: Returns defaults if file doesn't exist or is corrupt, preventing crashes.
    /// </summary>
    /// <returns>Loaded settings or defaults if loading fails.</returns>
    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                // WHY: Structs can't be null, Deserialize returns default if unsuccessful
                return JsonSerializer.Deserialize<AppSettings>(json);
            }
        }
        catch (Exception)
        {
            // WHY: Silently fall back to defaults on any error (corrupt file, permission issue, etc.)
        }
        
        return new AppSettings();
    }
    
    /// <summary>
    /// Saves current settings to the JSON file.
    /// WHY: Persists user preferences so they survive application restarts.
    /// </summary>
    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(Settings, _jsonOptions);
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception)
        {
            // WHY: Non-critical operation, silently ignore save failures
        }
    }
    
    /// <summary>
    /// Updates settings from current window state and saves to disk.
    /// WHY: Convenience method to capture window position and mode in one call.
    /// </summary>
    /// <param name="window">The main window to capture position from.</param>
    /// <param name="displayMode">Current display mode.</param>
    /// <param name="opacity">Current opacity (0-100).</param>
    /// <param name="uptimeAccumulatedSeconds">Accumulated active seconds since 100%, or 0 if not tracking.</param>
    public void SaveWindowState(Window window, DisplayMode displayMode, int opacity, double uptimeAccumulatedSeconds, bool? hasSeenTutorial = null)
    {
        // WHY: Struct value semantics require replacing the whole Settings value
        Settings = new AppSettings
        {
            WindowLeft = window.Left,
            WindowTop = window.Top,
            DisplayMode = displayMode,
            Opacity = opacity,
            UptimeAccumulatedSeconds = uptimeAccumulatedSeconds,
            HasSeenTutorial = hasSeenTutorial ?? Settings.HasSeenTutorial
        };
        Save();
    }
    
    /// <summary>
    /// Applies saved position to a window, ensuring it's visible on screen.
    /// WHY: Validates position to prevent window from appearing off-screen after monitor changes.
    /// </summary>
    /// <param name="window">The window to position.</param>
    public void ApplyWindowPosition(Window window)
    {
        // WHY: Check if saved position is still visible on any monitor
        var left = Settings.WindowLeft;
        var top = Settings.WindowTop;
        
        // Ensure window is at least partially visible on screen
        // WHY: SystemParameters provides virtual screen bounds across all monitors
        if (left < SystemParameters.VirtualScreenLeft)
            left = SystemParameters.VirtualScreenLeft;
        if (top < SystemParameters.VirtualScreenTop)
            top = SystemParameters.VirtualScreenTop;
        if (left > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50)
            left = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 200;
        if (top > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50)
            top = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100;
            
        window.Left = left;
        window.Top = top;
    }
}
