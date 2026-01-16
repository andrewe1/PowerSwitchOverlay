// ==============================================================================
// ClockMonitor.cs - CPU/GPU clock frequency monitoring
// WHY: Uses Performance Counters for real-time CPU clock, native vendor APIs
//      (AMD ADL / NVIDIA NVML) for GPU. No WinRing0 driver needed.
// ==============================================================================
//
// [CHANGE LOG]
// 2026-01-10 - AI - Fixed CPU clock: use Performance Counter % Processor Performance
//                   for real-time boost clock (matches Task Manager readings)
// 2026-01-06 - AI - Replaced LibreHardwareMonitor with native AMD ADL / NVIDIA NVML
// 2026-01-06 - AI - Added CpuName/GpuName detection
// 2026-01-05 - AI - Verified readonly on _lock field
// ==============================================================================

using System.Diagnostics;
using System.Management;

namespace PowerSwitchOverlay;

/// <summary>
/// GPU vendor type for clock monitoring.
/// </summary>
public enum GpuVendor
{
    None,
    Amd,
    Nvidia
}

/// <summary>
/// Monitors CPU and GPU clock frequencies using Performance Counters and native vendor APIs.
/// CPU clock uses Performance Counter "% Processor Performance" for real-time boost clock.
/// GPU clock uses AMD ADL or NVIDIA NVML depending on detected hardware.
/// No kernel drivers needed - avoids Defender warnings.
/// </summary>
public class ClockMonitor : IDisposable
{
    private readonly object _lock = new();
    private bool _disposed;
    private bool _isOpen;
    private GpuVendor _gpuVendor = GpuVendor.None;
    
    // WHY: Performance counter gives CPU frequency as % of base clock (e.g., 160% means boosting)
    // Actual MHz = (BaseCpuFrequency × PerformancePercent) / 100
    private PerformanceCounter? _cpuPerformanceCounter;
    private int _baseCpuFrequencyMHz;
    
    /// <summary>Current CPU frequency in MHz. Updated via Performance Counter % Processor Performance.</summary>
    public int CpuFrequencyMHz { get; private set; }
    
    /// <summary>Current GPU core clock in MHz. Updated via AMD ADL or NVIDIA NVML.</summary>
    public int GpuFrequencyMHz { get; private set; }
    
    /// <summary>Detected CPU model name.</summary>
    public string CpuName { get; private set; } = "Unknown CPU";
    
    /// <summary>Detected GPU model name.</summary>
    public string GpuName { get; private set; } = "Unknown GPU";
    
    /// <summary>True if a GPU was detected.</summary>
    public bool GpuDetected { get; private set; }
    
    /// <summary>
    /// True if clock readings are available (CPU or GPU returning valid values).
    /// WHY: Used for graceful degradation - hide clock section if unavailable.
    /// </summary>
    public bool ClocksAvailable => CpuFrequencyMHz > 0 || GpuFrequencyMHz > 0;
    
    /// <summary>True if the monitor is currently active.</summary>
    public bool IsOpen => _isOpen;

    /// <summary>
    /// Opens the clock monitor and initializes performance counter and GPU vendor API.
    /// WHY: Performance counter is created here (on main thread) for reliable initialization.
    ///      WMI is used only once to get base CPU frequency and name.
    /// </summary>
    public void Open()
    {
        lock (_lock)
        {
            if (_isOpen) return;
            
            // WHY: Query CPU base frequency ONCE here on the main thread
            // This is the nominal/base clock speed (e.g., 2.0 GHz for Ryzen 5875U)
            // We'll multiply this by % Processor Performance to get actual boost clock
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT MaxClockSpeed, Name FROM Win32_Processor");
                foreach (var obj in searcher.Get())
                {
                    // WHY: Use MaxClockSpeed (base frequency) instead of CurrentClockSpeed
                    // CurrentClockSpeed can return stale/incorrect values
                    int value = Convert.ToInt32(obj["MaxClockSpeed"]);
                    if (value > 0) _baseCpuFrequencyMHz = value;
                    
                    string? name = obj["Name"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(name)) CpuName = name.Trim();
                    break;
                }
                
                // WHY: Set initial display value to base frequency
                CpuFrequencyMHz = _baseCpuFrequencyMHz;
            }
            catch { }
            
            // WHY: Initialize performance counter for real-time CPU frequency
            // "Processor Information" category with "% Processor Performance" counter
            // gives actual clock as percentage of base (e.g., 160% = boosting to 1.6x base)
            try
            {
                _cpuPerformanceCounter = new PerformanceCounter(
                    "Processor Information", 
                    "% Processor Performance", 
                    "_Total");
                    
                // WHY: First reading is often 0, so we prime the counter
                _cpuPerformanceCounter.NextValue();
            }
            catch
            {
                // Performance counter not available - will use base frequency only
                _cpuPerformanceCounter = null;
            }
            
            try
            {
                // Try AMD first (user has AMD APU)
                if (AmdAdl.Initialize())
                {
                    _gpuVendor = GpuVendor.Amd;
                    GpuName = AmdAdl.GpuName;
                    GpuDetected = true;
                }
                // Then try NVIDIA
                else if (NvidiaNvml.Initialize())
                {
                    _gpuVendor = GpuVendor.Nvidia;
                    GpuName = NvidiaNvml.GpuName;
                    GpuDetected = true;
                }
                else
                {
                    // No supported GPU found - try WMI for name at least
                    _gpuVendor = GpuVendor.None;
                    GpuDetected = false;
                    RefreshGpuNameFromWmi();
                }
            }
            catch
            {
                _gpuVendor = GpuVendor.None;
                GpuDetected = false;
            }
            
            _isOpen = true;
        }
    }

    /// <summary>
    /// Closes the clock monitor and releases resources.
    /// </summary>
    public void Close()
    {
        lock (_lock)
        {
            if (!_isOpen) return;
            
            // WHY: Dispose performance counter to release resources
            _cpuPerformanceCounter?.Dispose();
            _cpuPerformanceCounter = null;
            
            if (_gpuVendor == GpuVendor.Amd)
                AmdAdl.Shutdown();
            else if (_gpuVendor == GpuVendor.Nvidia)
                NvidiaNvml.Shutdown();
            
            _isOpen = false;
        }
    }

    /// <summary>
    /// Refreshes CPU and GPU clock readings synchronously.
    /// </summary>
    public void Refresh()
    {
        RefreshCpuClock();
        RefreshGpuClock();
    }
    
    /// <summary>
    /// Reads CPU clock using performance counter.
    /// WHY: % Processor Performance gives actual clock as % of base frequency.
    /// Formula: Actual MHz = (Base MHz × Performance%) / 100
    /// </summary>
    private void RefreshCpuClock()
    {
        if (!_isOpen || _cpuPerformanceCounter == null || _baseCpuFrequencyMHz <= 0) return;
        
        try
        {
            // WHY: NextValue() returns CPU performance as percentage of base frequency
            // e.g., 160.5 means CPU is running at 160.5% of base clock (i.e., boosting)
            float performancePercent = _cpuPerformanceCounter.NextValue();
            
            if (performancePercent > 0)
            {
                // Calculate actual frequency: 2000 MHz base × 160% = 3200 MHz
                int actualMHz = (int)(_baseCpuFrequencyMHz * performancePercent / 100.0);
                if (actualMHz > 0)
                {
                    CpuFrequencyMHz = actualMHz;
                }
            }
        }
        catch
        {
            // Keep last known value on error
        }
    }

    /// <summary>
    /// Reads GPU clock from the appropriate vendor API.
    /// </summary>
    private void RefreshGpuClock()
    {
        if (!_isOpen) return;
        
        lock (_lock)
        {
            try
            {
                int clock = _gpuVendor switch
                {
                    GpuVendor.Amd => AmdAdl.GetGpuClockMHz(),
                    GpuVendor.Nvidia => NvidiaNvml.GetGpuClockMHz(),
                    _ => 0
                };
                
                if (clock > 0)
                {
                    GpuFrequencyMHz = clock;
                }
            }
            catch
            {
                // Keep last known value
            }
        }
    }

    /// <summary>
    /// Gets GPU name from WMI as fallback (no clock speed available).
    /// </summary>
    private void RefreshGpuNameFromWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            foreach (var obj in searcher.Get())
            {
                string? name = obj["Name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    GpuName = name.Trim();
                    break;
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Asynchronously refreshes CPU and GPU clock readings on a background thread.
    /// </summary>
    public Task RefreshAsync()
    {
        return Task.Run(Refresh);
    }

    /// <summary>
    /// Disposes resources and closes vendor APIs.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        
        Close();
        
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
