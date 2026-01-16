// ==============================================================================
// NvidiaNvml.cs - NVIDIA Management Library (NVML) P/Invoke wrapper
// WHY: Provides direct access to NVIDIA GPU clock speeds without WinRing0 driver.
// Uses nvml.dll which is installed with NVIDIA GPU drivers.
// ==============================================================================
//
// [CHANGE LOG]
// 2026-01-06 - AI - Created NVIDIA NVML wrapper for GPU clock monitoring
// ==============================================================================

using System.Runtime.InteropServices;
using System.Text;

namespace PowerSwitchOverlay;

/// <summary>
/// P/Invoke wrapper for NVIDIA Management Library (NVML) to read GPU clock speeds.
/// WHY: NVML is installed with NVIDIA drivers - no additional DLLs needed, no Defender warnings.
/// </summary>
public static class NvidiaNvml
{
    private const string NVML_DLL = "nvml.dll";
    
    // NVML return codes
    public const int NVML_SUCCESS = 0;
    
    // Clock types
    public const int NVML_CLOCK_GRAPHICS = 0;
    public const int NVML_CLOCK_SM = 1;
    public const int NVML_CLOCK_MEM = 2;
    
    // P/Invoke declarations
    [DllImport(NVML_DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nvmlInit_v2();
    
    [DllImport(NVML_DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nvmlShutdown();
    
    [DllImport(NVML_DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nvmlDeviceGetCount_v2(ref uint deviceCount);
    
    [DllImport(NVML_DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nvmlDeviceGetHandleByIndex_v2(uint index, ref IntPtr device);
    
    [DllImport(NVML_DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nvmlDeviceGetName(IntPtr device, StringBuilder name, uint length);
    
    [DllImport(NVML_DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int nvmlDeviceGetClockInfo(IntPtr device, int clockType, ref uint clock);
    
    private static bool _initialized;
    private static IntPtr _deviceHandle = IntPtr.Zero;
    private static string _gpuName = "Unknown NVIDIA GPU";
    
    /// <summary>
    /// True if NVIDIA NVML was successfully initialized and an NVIDIA GPU was found.
    /// </summary>
    public static bool IsAvailable { get; private set; }
    
    /// <summary>
    /// Name of the detected NVIDIA GPU.
    /// </summary>
    public static string GpuName => _gpuName;
    
    /// <summary>
    /// Initializes the NVIDIA NVML library and detects NVIDIA GPUs.
    /// </summary>
    public static bool Initialize()
    {
        if (_initialized) return IsAvailable;
        
        try
        {
            // Initialize NVML
            int result = nvmlInit_v2();
            if (result != NVML_SUCCESS)
            {
                IsAvailable = false;
                return false;
            }
            
            // Get device count
            uint deviceCount = 0;
            result = nvmlDeviceGetCount_v2(ref deviceCount);
            if (result != NVML_SUCCESS || deviceCount == 0)
            {
                nvmlShutdown();
                IsAvailable = false;
                return false;
            }
            
            // Get first device handle
            result = nvmlDeviceGetHandleByIndex_v2(0, ref _deviceHandle);
            if (result != NVML_SUCCESS)
            {
                nvmlShutdown();
                IsAvailable = false;
                return false;
            }
            
            // Get device name
            StringBuilder nameBuffer = new(256);
            result = nvmlDeviceGetName(_deviceHandle, nameBuffer, 256);
            if (result == NVML_SUCCESS)
            {
                _gpuName = nameBuffer.ToString().Trim();
            }
            
            _initialized = true;
            IsAvailable = true;
            return true;
        }
        catch (DllNotFoundException)
        {
            // NVIDIA drivers not installed
            IsAvailable = false;
            return false;
        }
        catch
        {
            IsAvailable = false;
            return false;
        }
    }
    
    /// <summary>
    /// Gets the current GPU graphics clock in MHz.
    /// </summary>
    public static int GetGpuClockMHz()
    {
        if (!IsAvailable || _deviceHandle == IntPtr.Zero) return 0;
        
        try
        {
            uint clockMHz = 0;
            int result = nvmlDeviceGetClockInfo(_deviceHandle, NVML_CLOCK_GRAPHICS, ref clockMHz);
            
            if (result == NVML_SUCCESS && clockMHz > 0)
            {
                return (int)clockMHz;
            }
        }
        catch { }
        
        return 0;
    }
    
    /// <summary>
    /// Shuts down the NVIDIA NVML library.
    /// </summary>
    public static void Shutdown()
    {
        if (_initialized)
        {
            try { nvmlShutdown(); } catch { }
            _initialized = false;
            IsAvailable = false;
            _deviceHandle = IntPtr.Zero;
        }
    }
}
