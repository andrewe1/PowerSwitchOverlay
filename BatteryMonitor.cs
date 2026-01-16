// ==============================================================================
// BatteryMonitor.cs - Battery status monitoring service
// Uses SystemInformation.PowerStatus to get battery percentage and time remaining.
// ==============================================================================
// CHANGE LOG:
// [2026-01-05] - Claude - Removed JustReachedFullCharge property. Uptime tracking
//                         now handled in MainWindow based on unplug detection.
// ==============================================================================

using System.Windows.Forms;

namespace PowerSwitchOverlay;

/// <summary>
/// Monitors battery status including charge percentage, time remaining, and charging state.
/// Uses Windows Forms SystemInformation.PowerStatus API.
/// </summary>
public class BatteryMonitor
{
    /// <summary>Current battery charge percentage (0-100).</summary>
    public int Percentage { get; private set; }
    
    /// <summary>Estimated time remaining on battery. Zero if unknown or charging.</summary>
    public TimeSpan TimeRemaining { get; private set; }
    
    /// <summary>True if the system is plugged in and charging.</summary>
    public bool IsCharging { get; private set; }
    
    /// <summary>True if the system has a battery. False for desktops.</summary>
    public bool HasBattery { get; private set; }

    public BatteryMonitor()
    {
        Refresh();
    }

    /// <summary>
    /// Refreshes battery status from system.
    /// </summary>
    public void Refresh()
    {
        try
        {
            var status = SystemInformation.PowerStatus;
            
            // Check if system has a battery
            HasBattery = status.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery;
            
            if (!HasBattery)
            {
                Percentage = 100;
                TimeRemaining = TimeSpan.Zero;
                IsCharging = true;
                return;
            }

            // Get battery percentage (0.0 to 1.0)
            Percentage = (int)(status.BatteryLifePercent * 100);
            
            // Clamp to valid range
            Percentage = Math.Clamp(Percentage, 0, 100);

            // Check charging status
            IsCharging = status.PowerLineStatus == PowerLineStatus.Online;

            // Get time remaining (in seconds, -1 if unknown)
            int secondsRemaining = status.BatteryLifeRemaining;
            if (secondsRemaining > 0)
            {
                TimeRemaining = TimeSpan.FromSeconds(secondsRemaining);
            }
            else
            {
                TimeRemaining = TimeSpan.Zero;
            }
        }
        catch
        {
            // Default values on error
            Percentage = 0;
            TimeRemaining = TimeSpan.Zero;
            IsCharging = false;
            HasBattery = false;
        }
    }
}
