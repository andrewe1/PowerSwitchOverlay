// ==============================================================================
// UptimeTracker.cs - Tracks active computer uptime since last full charge
// Uses QueryUnbiasedInterruptTime to exclude sleep/hibernation time.
// ==============================================================================
//
// CHANGE LOG:
// [2026-01-05] - Claude - Initial creation. Tracks uptime since battery reached
//                         100% charge, using Windows unbiased interrupt time API.
// [2026-01-06] - AI - Added persistence support: TrackingStartTime and RestoreFromDateTime
//                     to maintain "Since 100%" counter across app restarts.
// [2026-01-06] - AI - Fixed to persist accumulated active seconds instead of start DateTime.
//                     This ensures sleep/hibernate time is NEVER counted, even after restart.
// ==============================================================================

using System.Runtime.InteropServices;

namespace PowerSwitchOverlay;

/// <summary>
/// Tracks active computer uptime since the battery was last fully charged (100%).
/// Uses Windows QueryUnbiasedInterruptTime API which excludes time spent in sleep/hibernation.
/// WHY: This gives users a realistic measure of actual battery life in active use.
/// Supports persistence across app restarts via TrackingStartTime property.
/// </summary>
public class UptimeTracker
{
    /// <summary>
    /// Gets the unbiased interrupt-time count in 100-nanosecond intervals.
    /// This time excludes periods when the system is in sleep or hibernation.
    /// </summary>
    [DllImport("kernel32.dll")]
    private static extern bool QueryUnbiasedInterruptTime(out ulong UnbiasedTime);

    /// <summary>
    /// The unbiased interrupt time recorded at last checkpoint.
    /// WHY: Used to calculate delta of active time since last update.
    /// </summary>
    private ulong _lastCheckpointUnbiasedTime;
    
    /// <summary>
    /// Accumulated active seconds since full charge.
    /// WHY: This is the true elapsed active time, excluding sleep/hibernate.
    /// </summary>
    private double _accumulatedSeconds;
    
    /// <summary>
    /// True if we are currently tracking uptime from a full charge event.
    /// </summary>
    private bool _isTracking;

    /// <summary>
    /// True if we are currently tracking uptime from a full charge event.
    /// </summary>
    public bool IsTracking => _isTracking;
    
    /// <summary>
    /// Gets the accumulated active seconds for persistence.
    /// WHY: This value excludes sleep time and can be safely persisted/restored.
    /// </summary>
    public double AccumulatedSeconds => _accumulatedSeconds;

    /// <summary>
    /// Gets the active uptime since the battery was last at 100% charge.
    /// Returns TimeSpan.Zero if not tracking.
    /// WHY: Returns accumulated seconds converted to TimeSpan. Always accurate.
    /// </summary>
    public TimeSpan UptimeSinceFullCharge => TimeSpan.FromSeconds(_accumulatedSeconds);

    /// <summary>
    /// Call when battery reaches 100% charge and is unplugged.
    /// Resets accumulated time and starts fresh tracking.
    /// </summary>
    public void NotifyFullCharge()
    {
        if (QueryUnbiasedInterruptTime(out ulong currentTime))
        {
            _lastCheckpointUnbiasedTime = currentTime;
            _accumulatedSeconds = 0;
            _isTracking = true;
        }
    }
    
    /// <summary>
    /// Updates accumulated time by adding delta since last checkpoint.
    /// Call this periodically (e.g., on each UI refresh) to keep accumulated time current.
    /// WHY: Uses unbiased time delta so sleep/hibernate is automatically excluded.
    /// </summary>
    public void Update()
    {
        if (!_isTracking) return;
        
        if (QueryUnbiasedInterruptTime(out ulong currentTime))
        {
            // Calculate delta since last checkpoint
            // WHY: Unbiased time is in 100-nanosecond intervals (same as TimeSpan ticks)
            ulong deltaTicks = currentTime - _lastCheckpointUnbiasedTime;
            double deltaSeconds = TimeSpan.FromTicks((long)deltaTicks).TotalSeconds;
            
            // Add to accumulated time
            _accumulatedSeconds += deltaSeconds;
            
            // Update checkpoint for next call
            _lastCheckpointUnbiasedTime = currentTime;
        }
    }
    
    /// <summary>
    /// Restores tracking state from previously saved accumulated seconds.
    /// WHY: Called on app startup to continue counting from where we left off.
    /// </summary>
    /// <param name="accumulatedSeconds">The accumulated active seconds from last save.</param>
    public void RestoreFromAccumulatedSeconds(double accumulatedSeconds)
    {
        if (accumulatedSeconds > 0)
        {
            _accumulatedSeconds = accumulatedSeconds;
            _isTracking = true;
            
            // Set checkpoint to current time so future updates add correctly
            if (QueryUnbiasedInterruptTime(out ulong currentTime))
            {
                _lastCheckpointUnbiasedTime = currentTime;
            }
        }
    }

    /// <summary>
    /// Resets the tracking state. Call when battery is plugged back in.
    /// </summary>
    public void Reset()
    {
        _lastCheckpointUnbiasedTime = 0;
        _accumulatedSeconds = 0;
        _isTracking = false;
    }

    /// <summary>
    /// Formats the uptime as a human-readable string (e.g., "2h 15m" or "45m").
    /// Returns null if not tracking.
    /// </summary>
    public string? GetFormattedUptime()
    {
        if (!IsTracking)
            return null;

        var uptime = UptimeSinceFullCharge;
        
        if (uptime.TotalHours >= 1)
        {
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
        }
        else if (uptime.TotalMinutes >= 1)
        {
            return $"{uptime.Minutes}m";
        }
        else
        {
            return "<1m";
        }
    }
}
