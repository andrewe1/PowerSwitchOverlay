// ==============================================================================
// StartupManager.cs - Windows startup management
// Adds/removes app from Windows startup via registry.
// ==============================================================================
//
// CHANGE LOG:
// [2026-01-05] - Claude - Initial creation. Registry-based startup management.
// ==============================================================================

using Microsoft.Win32;

namespace PowerSwitchOverlay;

/// <summary>
/// Manages Windows startup registration via registry.
/// Uses HKCU\Software\Microsoft\Windows\CurrentVersion\Run for user-level startup.
/// WHY: Allows users to have the overlay start automatically with Windows.
/// </summary>
public static class StartupManager
{
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "PowerSwitchOverlay";

    /// <summary>
    /// Checks if the app is currently registered to run at startup.
    /// </summary>
    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
                return key?.GetValue(AppName) != null;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Enables automatic startup by adding registry entry.
    /// WHY: Registers the current executable path so Windows launches it at login.
    /// </summary>
    public static bool Enable()
    {
        try
        {
            // Get path to the current executable
            // WHY: Environment.ProcessPath works correctly in single-file apps
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return false;
            
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
            key?.SetValue(AppName, $"\"{exePath}\"");
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Disables automatic startup by removing registry entry.
    /// WHY: Cleanly removes the startup registration without leaving orphaned entries.
    /// </summary>
    public static bool Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
            if (key?.GetValue(AppName) != null)
            {
                key.DeleteValue(AppName);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Toggles the startup state.
    /// </summary>
    public static bool Toggle()
    {
        return IsEnabled ? Disable() : Enable();
    }
}
