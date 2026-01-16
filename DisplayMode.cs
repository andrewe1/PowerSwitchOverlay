// ==============================================================================
// DisplayMode.cs - Overlay display mode enumeration
// ==============================================================================

namespace PowerSwitchOverlay;

/// <summary>
/// Display modes for the overlay widget.
/// </summary>
public enum DisplayMode
{
    /// <summary>
    /// Micro: Minimal size, battery % and power mode icon only.
    /// </summary>
    Micro,
    
    /// <summary>
    /// MicroTime: Minimal size, time remaining and power mode icon only.
    /// </summary>
    MicroTime,
    
    /// <summary>
    /// Compact: Battery %, time remaining, power mode only.
    /// </summary>
    Compact,
    
    /// <summary>
    /// CompactClockAlt: Compact mode with alternating display - switches between
    /// battery time remaining and current clock time every 10 seconds.
    /// </summary>
    CompactClockAlt,
    
    /// <summary>
    /// Standard: Compact + CPU/RAM/GPU usage bars.
    /// </summary>
    Standard,
    
    /// <summary>
    /// Advanced: Standard + CPU/GPU clock frequencies.
    /// </summary>
    Advanced,
    
    /// <summary>
    /// AdvancedInfo: Advanced + CPU/GPU hardware names.
    /// </summary>
    AdvancedInfo
}
