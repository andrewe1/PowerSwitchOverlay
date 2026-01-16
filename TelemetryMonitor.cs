// ==============================================================================
// TelemetryMonitor.cs - System telemetry monitoring service
// Uses PerformanceCounter for CPU/RAM/GPU.
// Optimized to avoid blocking the UI thread.
// ==============================================================================
//
// [CHANGE LOG]
// 2026-01-08 - AI - Lazy-load GPU/Network counters to reduce memory in non-Advanced modes
// 2026-01-06 - AI - Optimized network monitoring to only run in Advanced modes
// 2026-01-06 - AI - Added network speed monitoring (download/upload in Mbps)
// 2026-01-06 - AI - Fixed RAM % to show physical memory usage instead of committed bytes
// 2026-01-06 - AI - Added GpuAvailable property for graceful fallback on systems without GPU counters
// 2026-01-05 - AI - Added disposed check, made _lock readonly
// ==============================================================================

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PowerSwitchOverlay;

/// <summary>
/// Monitors system telemetry including CPU, RAM, and GPU usage.
/// Uses Windows Performance Counters. GPU counters are cached and re-enumerated every 30s.
/// </summary>
public class TelemetryMonitor : IDisposable
{
    // WHY: GlobalMemoryStatusEx gives physical RAM usage matching Task Manager,
    // unlike "% Committed Bytes In Use" which includes page file
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;          // Percentage of physical memory in use (0-100)
        public ulong ullTotalPhys;         // Total physical memory in bytes
        public ulong ullAvailPhys;         // Available physical memory in bytes
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    private readonly PerformanceCounter? _cpuCounter;
    private List<PerformanceCounter> _gpuCounters = new();
    private List<PerformanceCounter> _netReceivedCounters = new();
    private List<PerformanceCounter> _netSentCounters = new();
    private DateTime _lastGpuEnumeration = DateTime.MinValue;
    private DateTime _lastNetEnumeration = DateTime.MinValue;
    private static readonly TimeSpan GpuEnumerationInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan NetEnumerationInterval = TimeSpan.FromSeconds(60);
    private bool _disposed;
    private readonly object _lock = new();
    // WHY: Lazy init flags - GPU/Network counters only created on first use to save memory
    private bool _gpuCountersInitialized;
    private bool _netCountersInitialized;

    /// <summary>Current CPU usage percentage (0-100).</summary>
    public float CpuUsage { get; private set; }
    
    /// <summary>Current physical RAM usage percentage (0-100), matching Task Manager.</summary>
    public float RamUsage { get; private set; }
    
    /// <summary>Current GPU 3D engine usage percentage (0-100). Sum of all 3D engines, capped at 100.</summary>
    public float GpuUsage { get; private set; }
    
    /// <summary>True if GPU Engine performance counters are available on this system.</summary>
    public bool GpuAvailable { get; private set; }
    
    /// <summary>Current network download speed in Mbps (megabits per second).</summary>
    public float NetDownloadMbps { get; private set; }
    
    /// <summary>Current network upload speed in Mbps (megabits per second).</summary>
    public float NetUploadMbps { get; private set; }

    public TelemetryMonitor()
    {
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            
            // Initial read to prime the counter
            _cpuCounter.NextValue();
        }
        catch
        {
            _cpuCounter = null;
        }

        // WHY: GPU and Network counters are now lazy-loaded on first use
        // This saves ~10-20 MB in Compact/Micro modes that don't need telemetry details
    }

    /// <summary>
    /// Re-enumerates GPU Engine performance counters. Called on first use and periodically.
    /// WHY: Lazy initialization saves memory when GPU monitoring isn't needed.
    /// </summary>
    private void RefreshGpuCounters()
    {
        lock (_lock)
        {
            // Dispose old counters
            foreach (var counter in _gpuCounters)
            {
                try { counter.Dispose(); } catch { }
            }
            _gpuCounters.Clear();

            try
            {
                var category = new PerformanceCounterCategory("GPU Engine");
                var instanceNames = category.GetInstanceNames();
                
                foreach (var instance in instanceNames)
                {
                    // Look for 3D engine instances (main graphics workload)
                    if (instance.Contains("engtype_3D"))
                    {
                        try
                        {
                            var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance);
                            counter.NextValue(); // Prime the counter
                            _gpuCounters.Add(counter);
                        }
                        catch
                        {
                            // Skip inaccessible instances
                        }
                    }
                }
            }
            catch
            {
                // GPU Engine category not available
            }
            
            _lastGpuEnumeration = DateTime.UtcNow;
            _gpuCountersInitialized = true;
            
            // WHY: Track if any GPU counters were found for graceful fallback in UI
            GpuAvailable = _gpuCounters.Count > 0;
        }
    }

    /// <summary>
    /// Re-enumerates network interface performance counters for bytes sent/received.
    /// WHY: Lazy initialization - only created when Advanced mode requests network stats.
    /// </summary>
    private void RefreshNetCounters()
    {
        lock (_lock)
        {
            // Dispose old counters
            foreach (var counter in _netReceivedCounters)
            {
                try { counter.Dispose(); } catch { }
            }
            foreach (var counter in _netSentCounters)
            {
                try { counter.Dispose(); } catch { }
            }
            _netReceivedCounters.Clear();
            _netSentCounters.Clear();

            try
            {
                var category = new PerformanceCounterCategory("Network Interface");
                var instanceNames = category.GetInstanceNames();
                
                foreach (var instance in instanceNames)
                {
                    try
                    {
                        var recvCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", instance);
                        var sentCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", instance);
                        recvCounter.NextValue(); // Prime the counter
                        sentCounter.NextValue();
                        _netReceivedCounters.Add(recvCounter);
                        _netSentCounters.Add(sentCounter);
                    }
                    catch
                    {
                        // Skip inaccessible interfaces
                    }
                }
            }
            catch
            {
                // Network Interface category not available
            }
            
            _lastNetEnumeration = DateTime.UtcNow;
            _netCountersInitialized = true;
        }
    }

    /// <summary>
    /// Refreshes all telemetry values synchronously.
    /// WHY: Check disposed state to prevent accessing disposed counters.
    /// </summary>
    /// <param name="includeNetwork">If true, also refreshes network speed counters. Default false for efficiency.</param>
    public void Refresh(bool includeNetwork = false)
    {
        if (_disposed) return;
        
        RefreshCpu();
        RefreshRam();
        RefreshGpu();
        
        // WHY: Network monitoring is only needed in Advanced modes, skip in simpler modes to save resources
        if (includeNetwork)
        {
            RefreshNet();
        }
    }

    /// <summary>
    /// Refreshes all telemetry values asynchronously (non-blocking).
    /// </summary>
    /// <param name="includeNetwork">If true, also refreshes network speed counters.</param>
    public Task RefreshAsync(bool includeNetwork = false)
    {
        if (_disposed) return Task.CompletedTask;
        return Task.Run(() => Refresh(includeNetwork));
    }

    /// <summary>
    /// Reads current CPU usage from Performance Counter.
    /// </summary>
    private void RefreshCpu()
    {
        try
        {
            if (_cpuCounter != null)
            {
                CpuUsage = _cpuCounter.NextValue();
            }
        }
        catch
        {
            CpuUsage = 0;
        }
    }

    /// <summary>
    /// Reads current physical RAM usage using GlobalMemoryStatusEx API.
    /// WHY: This matches Task Manager's RAM percentage, unlike the performance counter
    /// which shows committed bytes (RAM + page file).
    /// </summary>
    private void RefreshRam()
    {
        try
        {
            var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref memStatus))
            {
                RamUsage = memStatus.dwMemoryLoad;
            }
        }
        catch
        {
            RamUsage = 0;
        }
    }

    /// <summary>
    /// Reads GPU usage from cached 3D engine Performance Counters.
    /// Re-enumerates counters every 30 seconds to capture new GPU processes.
    /// </summary>
    private void RefreshGpu()
    {
        try
        {
            // WHY: Lazy init on first use, then re-enumerate periodically
            if (!_gpuCountersInitialized || DateTime.UtcNow - _lastGpuEnumeration > GpuEnumerationInterval)
            {
                RefreshGpuCounters();
            }

            float totalUsage = 0;
            
            lock (_lock)
            {
                foreach (var counter in _gpuCounters)
                {
                    try
                    {
                        // No Thread.Sleep - just read the cached counter value
                        float value = counter.NextValue();
                        totalUsage += value;
                    }
                    catch
                    {
                        // Counter may have become invalid
                    }
                }
            }
            
            // Cap at 100% (multiple engines can add up to more)
            GpuUsage = Math.Min(totalUsage, 100f);
        }
        catch
        {
            GpuUsage = 0;
        }
    }

    /// <summary>
    /// Reads current network download/upload speeds from all network interfaces.
    /// WHY: Sum across all interfaces to get total throughput, convert bytes/sec to Mbps
    /// </summary>
    private void RefreshNet()
    {
        try
        {
            // WHY: Lazy init on first use, then re-enumerate periodically
            if (!_netCountersInitialized || DateTime.UtcNow - _lastNetEnumeration > NetEnumerationInterval)
            {
                RefreshNetCounters();
            }

            float totalReceived = 0;
            float totalSent = 0;
            
            lock (_lock)
            {
                foreach (var counter in _netReceivedCounters)
                {
                    try
                    {
                        totalReceived += counter.NextValue();
                    }
                    catch { }
                }
                foreach (var counter in _netSentCounters)
                {
                    try
                    {
                        totalSent += counter.NextValue();
                    }
                    catch { }
                }
            }
            
            // Convert bytes/sec to Mbps (megabits per second)
            // 1 byte = 8 bits, 1 Mbps = 1,000,000 bits
            NetDownloadMbps = totalReceived * 8f / 1_000_000f;
            NetUploadMbps = totalSent * 8f / 1_000_000f;
        }
        catch
        {
            NetDownloadMbps = 0;
            NetUploadMbps = 0;
        }
    }

    /// <summary>
    /// Disposes all Performance Counters.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        
        _cpuCounter?.Dispose();
        
        lock (_lock)
        {
            foreach (var counter in _gpuCounters)
            {
                try { counter.Dispose(); } catch { }
            }
            _gpuCounters.Clear();
            
            foreach (var counter in _netReceivedCounters)
            {
                try { counter.Dispose(); } catch { }
            }
            _netReceivedCounters.Clear();
            
            foreach (var counter in _netSentCounters)
            {
                try { counter.Dispose(); } catch { }
            }
            _netSentCounters.Clear();
        }
        
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
