// ==============================================================================
// AmdAdl.cs - AMD Display Library (ADL) P/Invoke wrapper
// WHY: Provides direct access to AMD GPU clock speeds without WinRing0 driver.
// Uses atiadlxx.dll which is installed with AMD GPU drivers.
// Supports both Overdrive6 (older GPUs) and OverdriveN (newer APUs/GPUs).
// ==============================================================================
//
// [CHANGE LOG]
// 2026-01-06 - AI - Fixed GPU detection to use ADL1, then try OverdriveN for clocks
// 2026-01-06 - AI - Added OverdriveN API for AMD APU support
// 2026-01-06 - AI - Created AMD ADL wrapper for GPU clock monitoring
// ==============================================================================

using System.Runtime.InteropServices;

namespace PowerSwitchOverlay;

/// <summary>
/// P/Invoke wrapper for AMD Display Library (ADL) to read GPU clock speeds.
/// WHY: ADL is installed with AMD drivers - no additional DLLs needed, no Defender warnings.
/// Uses ADL1 for GPU detection, tries OverdriveN then Overdrive6 for clock reading.
/// </summary>
public static class AmdAdl
{
    private const string ADL_DLL = "atiadlxx.dll";
    
    // ADL return codes
    public const int ADL_OK = 0;
    public const int ADL_ERR = -1;
    
    // Memory allocation callback delegate
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr ADL_Main_Memory_Alloc(int size);
    
    // ADL Adapter Info structure
    [StructLayout(LayoutKind.Sequential)]
    public struct AdapterInfo
    {
        public int Size;
        public int AdapterIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string UDID;
        public int BusNumber;
        public int DeviceNumber;
        public int FunctionNumber;
        public int VendorID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string AdapterName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string DisplayName;
        public int Present;
        public int Exist;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string DriverPath;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string DriverPathExt;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string PNPString;
        public int OSDisplayIndex;
    }
    
    // Overdrive 6 current status (older discrete GPUs)
    [StructLayout(LayoutKind.Sequential)]
    public struct ADL_OD6_CURRENTSTATUS
    {
        public int EngineClock;   // GPU Core clock in 10 kHz (divide by 100 for MHz)
        public int MemoryClock;   // Memory clock in 10 kHz
        public int ActivityPercent;
        public int CurrentPerformanceLevel;
        public int CurrentBusSpeed;
        public int CurrentBusLanes;
        public int MaximumBusLanes;
        public int Reserved;
    }
    
    // OverdriveN performance status (newer APUs and GPUs)
    [StructLayout(LayoutKind.Sequential)]
    public struct ADLODNPerformanceStatus
    {
        public int CoreClock;      // Current core clock in MHz
        public int MemoryClock;    // Current memory clock in MHz
        public int DCEFClock;
        public int GFXClock;
        public int UVDClock;
        public int VCEClock;
        public int GPUActivityPercent;
        public int CurrentCorePerformanceLevel;
        public int CurrentMemoryPerformanceLevel;
        public int CurrentDCEFPerformanceLevel;
        public int CurrentGFXPerformanceLevel;
        public int UVDPerformanceLevel;
        public int VCEPerformanceLevel;
        public int CurrentBusSpeed;
        public int CurrentBusLanes;
        public int MaximumBusLanes;
        public int FANRPM;
        public int FanMaxRPM;
        public int Temperature;   // Temperature in milli-degrees Celsius
        public int VDDCCurrent;
        public int VDDCICurrent;
        public int VDDCP;
        public int VDDCC;
        public int MVDDCurrent;
    }
    
    // P/Invoke declarations - ADL1 (basic)
    [DllImport(ADL_DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ADL_Main_Control_Create(ADL_Main_Memory_Alloc callback, int enumConnectedAdapters);
    
    [DllImport(ADL_DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ADL_Main_Control_Destroy();
    
    [DllImport(ADL_DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ADL_Adapter_NumberOfAdapters_Get(ref int numAdapters);
    
    [DllImport(ADL_DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ADL_Adapter_AdapterInfo_Get(IntPtr info, int inputSize);
    
    // Overdrive 6 (older GPUs)
    [DllImport(ADL_DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ADL_Overdrive6_CurrentStatus_Get(int adapterIndex, ref ADL_OD6_CURRENTSTATUS status);
    
    // OverdriveN (newer APUs and GPUs) - ADL2 context version
    [DllImport(ADL_DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ADL2_Main_Control_Create(ADL_Main_Memory_Alloc callback, int enumConnectedAdapters, ref IntPtr context);
    
    [DllImport(ADL_DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ADL2_Main_Control_Destroy(IntPtr context);
    
    [DllImport(ADL_DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int ADL2_OverdriveN_PerformanceStatus_Get(IntPtr context, int adapterIndex, ref ADLODNPerformanceStatus status);
    
    // Memory allocation callback for ADL
    private static IntPtr ADL_Main_Memory_Alloc_Callback(int size)
    {
        return Marshal.AllocHGlobal(size);
    }
    
    private static readonly ADL_Main_Memory_Alloc _memoryAlloc = ADL_Main_Memory_Alloc_Callback;
    
    private static bool _initialized;
    private static int _primaryAdapterIndex = -1;
    private static string _gpuName = "Unknown AMD GPU";
    private static IntPtr _adl2Context = IntPtr.Zero;
    
    /// <summary>
    /// True if AMD ADL was successfully initialized and an AMD GPU was found.
    /// </summary>
    public static bool IsAvailable { get; private set; }
    
    /// <summary>
    /// Name of the detected AMD GPU.
    /// </summary>
    public static string GpuName => _gpuName;
    
    /// <summary>
    /// Initializes the AMD ADL library and detects AMD GPUs.
    /// Uses ADL1 for reliable GPU detection.
    /// </summary>
    public static bool Initialize()
    {
        if (_initialized) return IsAvailable;
        
        try
        {
            // Use ADL1 for GPU detection (more reliable)
            int result = ADL_Main_Control_Create(_memoryAlloc, 1);
            if (result != ADL_OK)
            {
                IsAvailable = false;
                return false;
            }
            
            // Get number of adapters
            int numAdapters = 0;
            result = ADL_Adapter_NumberOfAdapters_Get(ref numAdapters);
            if (result != ADL_OK || numAdapters <= 0)
            {
                ADL_Main_Control_Destroy();
                IsAvailable = false;
                return false;
            }
            
            // Get adapter info
            int adapterInfoSize = Marshal.SizeOf<AdapterInfo>();
            IntPtr adapterBuffer = Marshal.AllocHGlobal(adapterInfoSize * numAdapters);
            
            try
            {
                result = ADL_Adapter_AdapterInfo_Get(adapterBuffer, adapterInfoSize * numAdapters);
                if (result == ADL_OK)
                {
                    // Find first active adapter
                    for (int i = 0; i < numAdapters; i++)
                    {
                        IntPtr offset = IntPtr.Add(adapterBuffer, i * adapterInfoSize);
                        AdapterInfo info = Marshal.PtrToStructure<AdapterInfo>(offset);
                        
                        if (info.Present != 0 && info.Exist != 0)
                        {
                            _primaryAdapterIndex = info.AdapterIndex;
                            _gpuName = info.AdapterName?.Trim() ?? "AMD GPU";
                            break;
                        }
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(adapterBuffer);
            }
            
            // Also try to initialize ADL2 for OverdriveN (ignore failure, it's optional)
            try
            {
                ADL2_Main_Control_Create(_memoryAlloc, 1, ref _adl2Context);
            }
            catch { _adl2Context = IntPtr.Zero; }
            
            _initialized = true;
            IsAvailable = _primaryAdapterIndex >= 0;
            return IsAvailable;
        }
        catch (DllNotFoundException)
        {
            // AMD drivers not installed
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
    /// Gets the current GPU core clock in MHz.
    /// Tries OverdriveN first (modern APUs), falls back to Overdrive6 (older GPUs).
    /// </summary>
    public static int GetGpuClockMHz()
    {
        if (!IsAvailable || _primaryAdapterIndex < 0) return 0;
        
        try
        {
            // Try OverdriveN first (for modern APUs like Ryzen with Radeon Graphics)
            if (_adl2Context != IntPtr.Zero)
            {
                ADLODNPerformanceStatus status = new();
                int result = ADL2_OverdriveN_PerformanceStatus_Get(_adl2Context, _primaryAdapterIndex, ref status);
                
                if (result == ADL_OK)
                {
                    // WHY: OverdriveN returns clocks in 10 kHz units (like OD6), divide by 100 for MHz
                    // Try GFXClock first (more accurate for APUs), then CoreClock
                    if (status.GFXClock > 0)
                        return status.GFXClock / 100;
                    if (status.CoreClock > 0)
                        return status.CoreClock / 100;
                }
            }
            
            // Fall back to Overdrive6 (older discrete GPUs)
            ADL_OD6_CURRENTSTATUS od6Status = new();
            int od6Result = ADL_Overdrive6_CurrentStatus_Get(_primaryAdapterIndex, ref od6Status);
            
            if (od6Result == ADL_OK && od6Status.EngineClock > 0)
            {
                // EngineClock is in 10 kHz units, divide by 100 for MHz
                return od6Status.EngineClock / 100;
            }
        }
        catch { }
        
        return 0;
    }
    
    /// <summary>
    /// Shuts down the AMD ADL library.
    /// </summary>
    public static void Shutdown()
    {
        if (_initialized)
        {
            try
            {
                if (_adl2Context != IntPtr.Zero)
                    ADL2_Main_Control_Destroy(_adl2Context);
                ADL_Main_Control_Destroy();
            }
            catch { }
            _adl2Context = IntPtr.Zero;
            _primaryAdapterIndex = -1;  // WHY: Reset adapter index for clean re-init
            _initialized = false;
            IsAvailable = false;
        }
    }
}
