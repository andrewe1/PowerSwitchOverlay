// ==============================================================================
// PowerModeService.cs - Power mode detection and switching service
// Extracted from PowerPlanContext.cs for reuse in overlay.
// ==============================================================================
//
// [CHANGE LOG]
// 2026-01-05 - AI - Optimized to use case-insensitive dictionary for O(1) lookup
// ==============================================================================

using System.Runtime.InteropServices;

namespace PowerSwitchOverlay;

/// <summary>
/// Service for detecting and switching Windows 11 power modes.
/// </summary>
public class PowerModeService
{
    [DllImport("powrprof.dll", EntryPoint = "PowerSetActiveOverlayScheme")]
    private static extern uint PowerSetActiveOverlayScheme(Guid overlaySchemeGuid);

    [DllImport("powrprof.dll", EntryPoint = "PowerGetActualOverlayScheme")]
    private static extern uint PowerGetActualOverlayScheme(out Guid actualOverlayGuid);

    // WHY: Use StringComparer.OrdinalIgnoreCase for O(1) case-insensitive lookup
    private static readonly Dictionary<string, string> PowerModes = new(StringComparer.OrdinalIgnoreCase)
    {
        { "961cc777-2547-4f9d-8174-7d86181b8a7a", "Best Power Efficiency" },
        { "00000000-0000-0000-0000-000000000000", "Balanced" },
        { "ded574b5-45a0-4f42-8737-46345c09c238", "Best Performance" }
    };

    /// <summary>
    /// Gets the current power mode GUID.
    /// </summary>
    public string GetCurrentModeGuid()
    {
        try
        {
            uint result = PowerGetActualOverlayScheme(out Guid overlayGuid);
            if (result == 0)
            {
                return overlayGuid.ToString();
            }
        }
        catch
        {
            // API not available
        }
        
        return string.Empty;
    }

    /// <summary>
    /// Gets the friendly name of the current power mode.
    /// WHY: Now O(1) lookup instead of O(n) loop.
    /// </summary>
    public string GetCurrentModeName()
    {
        var guid = GetCurrentModeGuid();
        
        // WHY: TryGetValue with case-insensitive dictionary is O(1)
        if (PowerModes.TryGetValue(guid, out var name))
        {
            return name;
        }
        
        return "Unknown";
    }

    /// <summary>
    /// Sets the active power mode.
    /// </summary>
    public bool SetMode(string guid)
    {
        try
        {
            var overlayGuid = Guid.Parse(guid);
            uint result = PowerSetActiveOverlayScheme(overlayGuid);
            return result == 0;
        }
        catch
        {
            return false;
        }
    }

    // Mode GUIDs for cycling
    private static readonly string[] ModeGuids = new[]
    {
        "961cc777-2547-4f9d-8174-7d86181b8a7a", // Best Power Efficiency
        "00000000-0000-0000-0000-000000000000", // Balanced
        "ded574b5-45a0-4f42-8737-46345c09c238"  // Best Performance
    };

    /// <summary>
    /// Cycles to the next power mode.
    /// </summary>
    public void CycleMode()
    {
        var currentGuid = GetCurrentModeGuid().ToLowerInvariant();
        
        // Find current index
        int currentIndex = Array.FindIndex(ModeGuids, g => g.Equals(currentGuid, StringComparison.OrdinalIgnoreCase));
        
        // Cycle to next (or start from 0 if not found)
        int nextIndex = (currentIndex + 1) % ModeGuids.Length;
        
        SetMode(ModeGuids[nextIndex]);
    }
}
