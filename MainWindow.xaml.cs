// ==============================================================================
// MainWindow.xaml.cs - Transparent overlay window code-behind
// Handles window dragging, telemetry updates, and hotkey registration.
// Optimized: telemetry runs async on background thread.
// ==============================================================================
//
// TODO: Potential Features & Improvements
// ----------------------------------------
// [x] Save/restore window position on restart
// [x] Add settings persistence (display mode, position, etc.)
// [ ] Add multi-monitor support (choose which monitor to display on)
// [ ] Add animation when switching display modes
// [x] Add network usage monitoring (upload/download speed)
// [ ] Add disk I/O monitoring
// [ ] Add click-through toggle via keyboard shortcut
// [ ] Add configurable update interval
// [ ] Add battery health/cycle count display
// [ ] Add auto-hide when fullscreen app is running
// [ ] Add themes (dark/light/custom colors)
// [ ] Add drag-snap to screen edges/corners
// [ ] Add configurable hotkey for show/hide
// [ ] Add system tray balloon notifications for low battery
// [2026-01-06] - AI - Integrated uptime tracker persistence: restores "Since 100%"
//                     counter on startup and saves state on close.
// [2026-01-06] - AI - Added 98% tolerance for full charge detection (degraded batteries)
// [2026-01-06] - AI - Fixed uptime to persist accumulated active seconds (excludes sleep)
// [2026-01-07] - AI - Added CompactClockAlt mode: alternates battery/clock time every 10s
// [2026-01-07] - AI - Added first-launch tutorial with 3 sequential tip balloons
// [2026-01-08] - AI - Fixed visual bug in MicroTime mode: explicitly collapsed conflicted text elements & reset margins to correct icon offset
// [2026-01-08] - AI - Pushed time+icons up 2px in MicroTime, pushed only icons up 2px in Micro (Percentage)
// [2026-01-13] - AI - Fixed bug: "Since 100%" timer now resets properly when battery is recharged to 100%
// [2026-01-08] - AI - Added drag-to-reposition for tutorial popup, "Launch Quick Start" menu option
// [2026-01-08] - AI - Fixed crash: WMI query in ClockMonitor crashed when called from background thread
// [2026-01-10] - AI - Fixed topmost z-order issue after autostart: periodically re-asserts topmost
//                     using Win32 SetWindowPos until user clicks overlay
// =============================================================================

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace PowerSwitchOverlay;

/// <summary>
/// Main overlay window that displays battery, power mode, and system telemetry.
/// Supports three display modes (Compact, Standard, Advanced) and global hotkeys.
/// </summary>
public partial class MainWindow : Window
{
    // Win32 API for click-through and hotkey
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int GWL_EXSTYLE = -20;
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 9000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    
    // WHY: SetWindowPos constants for re-asserting topmost status
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;  // WHY: Don't steal focus when re-asserting

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // Modifier keys
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint VK_O = 0x4F;

    // Window sizes for each mode
    private const double HeightMicro = 35;
    private const double HeightCompact = 38;  // WHY: Trimmed for tighter compact mode frame
    private const double HeightStandard = 80;
    private const double HeightAdvanced = 125;
    private const double HeightAdvancedInfo = 159;  // WHY: Extra height for hardware names display
    private const double WidthMicro = 110;
    private const double WidthCompact = 180;
    private const double WidthStandard = 220;
    private const double WidthAdvanced = 220;

    private readonly BatteryMonitor _batteryMonitor;
    private readonly TelemetryMonitor _telemetryMonitor;
    private readonly ClockMonitor _clockMonitor;
    private readonly PowerModeService _powerModeService;
    private readonly UptimeTracker _uptimeTracker;
    private readonly SettingsService _settingsService;
    private readonly DispatcherTimer _updateTimer;
    
    private bool _isClickThrough = false;
    private IntPtr _hwnd;
    private int _tickCount = 0;
    
    // WHY: Track if user has interacted with the overlay to stop topmost re-assertion
    private bool _hasUserInteracted = false;
    private DispatcherTimer? _topmostTimer;
    
    public DisplayMode CurrentDisplayMode { get; private set; } = DisplayMode.Standard;
    public int CurrentOpacity { get; private set; } = 100;
    /// <summary>True if overlay is in click-through mode (mouse clicks pass through).</summary>
    public bool IsClickThrough => _isClickThrough;
    
    /// <summary>
    /// Reference to tray service for showing context menu on Ctrl+Right-Click.
    /// WHY: Set by App.xaml.cs after both MainWindow and TrayIconService are created.
    /// </summary>
    public TrayIconService? TrayService { get; set; }

    /// <summary>
    /// Initializes the MainWindow, sets up services, and starts the update timer.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        // Initialize services
        _batteryMonitor = new BatteryMonitor();
        _telemetryMonitor = new TelemetryMonitor();
        _clockMonitor = new ClockMonitor();
        _powerModeService = new PowerModeService();
        _uptimeTracker = new UptimeTracker();
        _settingsService = new SettingsService();

        // WHY: Apply saved window position and display mode from previous session
        _settingsService.ApplyWindowPosition(this);
        CurrentDisplayMode = _settingsService.Settings.DisplayMode;
        CurrentOpacity = _settingsService.Settings.Opacity;
        
        // WHY: Restore uptime tracking from previous session, but ONLY if:
        // 1. Battery is NOT plugged in (charging)
        // 2. Battery is BELOW 98% (if at 98%+ and unplugged, the user likely recharged since last session,
        //    so we should start fresh rather than restore stale accumulated time)
        // BUG FIX: Previously restored stale time even after battery was recharged to 100%
        if (_settingsService.Settings.UptimeAccumulatedSeconds > 0 && !_batteryMonitor.IsCharging && _batteryMonitor.Percentage < 98)
        {
            _uptimeTracker.RestoreFromAccumulatedSeconds(_settingsService.Settings.UptimeAccumulatedSeconds);
        }
        // WHY: If battery is at 98%+ and unplugged, start fresh tracking immediately
        // This handles the case where user recharged and is now starting a new session
        else if (_batteryMonitor.Percentage >= 98 && !_batteryMonitor.IsCharging)
        {
            _uptimeTracker.NotifyFullCharge();
        }

        // Setup update timer (every 2 seconds for battery/power mode)
        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _updateTimer.Tick += UpdateTimer_Tick;
        _updateTimer.Start();

        // Initial update (sync is fine for first load)
        UpdateBatteryAndPowerMode();
        _ = UpdateTelemetryAsync();

        // Apply initial display mode and opacity from saved settings
        SetDisplayMode(CurrentDisplayMode);
        SetOpacity(CurrentOpacity);

        // Register hotkey after window is loaded
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    /// <summary>
    /// Called when window is loaded. Registers global Ctrl+Shift+O hotkey for toggling visibility.
    /// </summary>
    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        
        // Register Ctrl+Shift+O hotkey to toggle visibility
        RegisterHotKey(_hwnd, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_O);
        
        // Hook into window messages for hotkey
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        
        // WHY: Start topmost re-assertion timer to fix z-order issues after autostart.
        // When app starts automatically at boot, the WPF Topmost property may not properly
        // assert z-order before fullscreen apps take over. This timer periodically calls
        // SetWindowPos to re-assert topmost until user clicks the overlay.
        StartTopmostReassertionTimer();
        
        // WHY: Show tutorial only on first launch
        if (!_settingsService.Settings.HasSeenTutorial)
        {
            StartTutorial();
        }
    }
    
    /// <summary>
    /// Starts a timer that periodically re-asserts topmost z-order.
    /// WHY: After autostart, fullscreen apps may cover the overlay until user clicks it.
    /// This timer uses Win32 SetWindowPos to ensure overlay stays on top.
    /// Timer stops after 60 seconds or when user interacts with the overlay.
    /// </summary>
    private void StartTopmostReassertionTimer()
    {
        _topmostTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)  // WHY: 1 second is frequent enough without overhead
        };
        
        int tickCount = 0;
        _topmostTimer.Tick += (s, e) =>
        {
            tickCount++;
            
            // WHY: Stop after user clicks overlay or after 60 seconds
            // 60 seconds should be more than enough for user to settle after restart
            if (_hasUserInteracted || tickCount >= 60)
            {
                _topmostTimer?.Stop();
                _topmostTimer = null;
                return;
            }
            
            ReassertTopmost();
        };
        
        _topmostTimer.Start();
    }
    
    /// <summary>
    /// Uses Win32 SetWindowPos to forcibly re-assert topmost z-order.
    /// WHY: WPF's Topmost property uses HWND_TOPMOST but doesn't re-apply after
    /// other windows (especially fullscreen apps) change z-order.
    /// </summary>
    private void ReassertTopmost()
    {
        if (_hwnd != IntPtr.Zero)
        {
            // WHY: Use SWP_NOACTIVATE to avoid stealing focus, SWP_NOMOVE/NOSIZE to preserve position
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }
    
    /// <summary>
    /// Shows tutorial popup on first launch.
    /// WHY: Single popup with all tips, dismissed on click.
    /// </summary>
    private void StartTutorial()
    {
        TutorialPopup.IsOpen = true;
    }
    
    // WHY: Track drag state for tutorial popup repositioning
    private bool _isDraggingTutorial;
    private System.Windows.Point _tutorialDragStart;
    private double _tutorialOriginalHOffset;
    private double _tutorialOriginalVOffset;
    
    /// <summary>
    /// Handles mouse down on tutorial popup for drag initiation or double-click dismiss.
    /// WHY: Double-click dismisses, single click starts drag for repositioning.
    /// </summary>
    private void TutorialPopup_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Double-click dismisses the popup
        if (e.ClickCount >= 2)
        {
            DismissTutorial();
            e.Handled = true;
            return;
        }
        
        // Start drag if single click
        if (sender is FrameworkElement element)
        {
            _isDraggingTutorial = true;
            _tutorialDragStart = e.GetPosition(this);
            _tutorialOriginalHOffset = TutorialPopup.HorizontalOffset;
            _tutorialOriginalVOffset = TutorialPopup.VerticalOffset;
            element.CaptureMouse();
            e.Handled = true;
        }
    }
    
    /// <summary>
    /// Handles mouse up on tutorial popup to end drag.
    /// </summary>
    private void TutorialPopup_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingTutorial && sender is FrameworkElement element)
        {
            _isDraggingTutorial = false;
            element.ReleaseMouseCapture();
            e.Handled = true;
        }
    }
    
    /// <summary>
    /// Handles mouse move on tutorial popup for dragging.
    /// WHY: Updates popup offset based on mouse delta for repositioning.
    /// </summary>
    private void TutorialPopup_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDraggingTutorial)
        {
            var currentPos = e.GetPosition(this);
            var deltaX = currentPos.X - _tutorialDragStart.X;
            var deltaY = currentPos.Y - _tutorialDragStart.Y;
            
            TutorialPopup.HorizontalOffset = _tutorialOriginalHOffset + deltaX;
            TutorialPopup.VerticalOffset = _tutorialOriginalVOffset + deltaY;
        }
    }
    
    /// <summary>
    /// Dismisses the tutorial popup and marks it as seen.
    /// WHY: Separated from click handler to allow menu-triggered launch.
    /// </summary>
    private void DismissTutorial()
    {
        TutorialPopup.IsOpen = false;
        
        // Mark tutorial as seen so it doesn't show again
        _settingsService.SaveWindowState(this, CurrentDisplayMode, CurrentOpacity, 
            _uptimeTracker.AccumulatedSeconds, hasSeenTutorial: true);
    }
    
    /// <summary>
    /// Launches the quick start tutorial (can be called from menu).
    /// WHY: Allows users to re-view the tutorial at any time.
    /// </summary>
    public void LaunchTutorial()
    {
        // Reset popup position to default
        TutorialPopup.HorizontalOffset = 10;
        TutorialPopup.VerticalOffset = 0;
        TutorialPopup.IsOpen = true;
    }

    /// <summary>
    /// Cleanup on window close. Unregisters hotkeys, stops timers, disposes monitors.
    /// WHY: Also saves current window position and mode for next session.
    /// </summary>
    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // WHY: Save window state before closing so it persists across restarts
        // Include accumulated uptime seconds for persistence (excludes sleep time)
        _uptimeTracker.Update();  // WHY: Ensure latest active time is captured before save
        _settingsService.SaveWindowState(this, CurrentDisplayMode, CurrentOpacity, _uptimeTracker.AccumulatedSeconds);
        
        UnregisterHotKey(_hwnd, HOTKEY_ID);
        _updateTimer.Stop();
        _topmostTimer?.Stop();  // WHY: Cleanup topmost timer if still running
        _telemetryMonitor.Dispose();
        _clockMonitor.Dispose();
    }

    /// <summary>
    /// Windows message handler for global hotkey events.
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            ToggleVisibility();
            handled = true;
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Toggles window visibility between Visible and Hidden.
    /// WHY: Pauses updates when hidden to save CPU, resumes and refreshes immediately on show.
    /// </summary>
    public void ToggleVisibility()
    {
        if (Visibility == Visibility.Visible)
        {
            Visibility = Visibility.Hidden;
            _updateTimer.Stop();  // WHY: No need to update when hidden
        }
        else
        {
            Visibility = Visibility.Visible;
            _updateTimer.Start();
            UpdateBatteryAndPowerMode();  // WHY: Immediate refresh on show
            _ = UpdateTelemetryAsync();
        }
    }

    /// <summary>
    /// Toggles click-through mode. When enabled, mouse clicks pass through the overlay.
    /// </summary>
    public void ToggleClickThrough()
    {
        _isClickThrough = !_isClickThrough;
        SetClickThrough(_isClickThrough);
    }

    /// <summary>
    /// Sets the display mode and updates visibility of sections.
    /// Opens/closes ClockMonitor based on whether Advanced mode needs it.
    /// </summary>
    public void SetDisplayMode(DisplayMode mode)
    {
        CurrentDisplayMode = mode;

        switch (mode)
        {
            case DisplayMode.Micro:
                TelemetrySection.Visibility = Visibility.Collapsed;
                ClocksSection.Visibility = Visibility.Collapsed;
                TimeRemainingSection.Visibility = Visibility.Collapsed;
                BatterySection.Visibility = Visibility.Visible;
                UptimeSection.Visibility = Visibility.Collapsed;
                // WHY: Reset padding from MicroTime mode
                MainContainer.Padding = new Thickness(8, 6, 8, 4);  // WHY: Top padding 6px keeps percentage text in place
                // Smaller elements for Micro mode
                BatteryIcon.Width = 18;
                BatteryIcon.Height = 10;
                BatteryTip.Width = 1.5;
                BatteryTip.Height = 4;
                BatteryPercentText.FontSize = 14;
                // WHY: Left margin shifts battery+percentage to the right for better centering
                BatterySection.Margin = new Thickness(7, 1, 0, 0);
                PowerModeGauge.FontSize = 16;
                // WHY: Use -5 top margin as baseline (turtle position) to prevent layout shift
                PowerModeGauge.Margin = new Thickness(0, -5, 0, 0);
                // WHY: Reset alignment from MicroTime's Left to Center
                PowerModeGauge.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                // WHY: Apply correct RenderTransform immediately to avoid visual jump when switching modes
                {
                    var currentMode = _powerModeService.GetCurrentModeName();
                    bool isTurtle = currentMode == "Best Power Efficiency";
                    // WHY: Use -2 for turtle and 1 for others to push icons up 2 pixels
                    PowerModeGauge.RenderTransform = new TranslateTransform(0, isTurtle ? -2 : 1);
                }
                Height = HeightMicro;
                Width = WidthMicro;
                break;
                
            case DisplayMode.MicroTime:
                TelemetrySection.Visibility = Visibility.Collapsed;
                ClocksSection.Visibility = Visibility.Collapsed;
                TimeRemainingSection.Visibility = Visibility.Visible;
                BatterySection.Visibility = Visibility.Collapsed;
                ChargingStatusText.Visibility = Visibility.Collapsed;
                ClockAltTimeText.Visibility = Visibility.Collapsed;  // WHY: Hide clock alt text when leaving CompactClockAlt
                ClockTimeText.Visibility = Visibility.Collapsed; // WHY: Ensure all clock texts are hidden
                UptimeSection.Visibility = Visibility.Collapsed;

                // WHY: Reset BatterySection margin to prevent residual layout effects
                // even though BatterySection is collapsed
                BatterySection.Margin = new Thickness(0);
                // Smaller elements and tighter padding for MicroTime mode
                // WHY: Extra right padding (9px) shifts content left, extra top padding (6px) shifts down
                // WHY: Top padding 4px (was 6px) pushes time and icons up 2 pixels
                MainContainer.Padding = new Thickness(4, 4, 9, 4);
                TimeRemainingText.FontSize = 14;
                TimeRemainingText.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
                // WHY: Apply correct margin immediately based on battery state to avoid visual jump
                // Chrg./Calc. text needs 7px right margin, time display needs 0
                bool showingTimeValue = !_batteryMonitor.IsCharging && _batteryMonitor.TimeRemaining.TotalSeconds > 0;
                TimeRemainingText.Margin = showingTimeValue ? new Thickness(0) : new Thickness(0, 0, 7, 0);
                TimeRemainingSection.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                TimeRemainingSection.Margin = new Thickness(0);
                PowerModeGauge.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                PowerModeGauge.Margin = new Thickness(0, -5, 0, 0);
                PowerModeGauge.FontSize = 16;
                // WHY: Apply correct RenderTransform immediately to avoid visual jump when switching modes
                {
                    var currentMode = _powerModeService.GetCurrentModeName();
                    bool isTurtle = currentMode == "Best Power Efficiency";
                    // WHY: Use -2 for turtle and 1 for others to push icons up 2 pixels
                    PowerModeGauge.RenderTransform = new TranslateTransform(0, isTurtle ? -2 : 1);
                }
                Height = HeightMicro;
                Width = 93;  // Tight width with slight right padding
                break;
                
            case DisplayMode.Compact:
                TelemetrySection.Visibility = Visibility.Collapsed;
                ClocksSection.Visibility = Visibility.Collapsed;
                TimeRemainingSection.Visibility = Visibility.Visible;
                BatterySection.Visibility = Visibility.Visible;
                ChargingStatusText.Visibility = Visibility.Visible;
                ClockTimeText.Visibility = Visibility.Collapsed;
                ClockAltTimeText.Visibility = Visibility.Collapsed;  // WHY: Hide clock alt text in Compact mode
                UptimeSection.Visibility = Visibility.Collapsed;
                // Reset to normal sizes and padding
                MainContainer.Padding = new Thickness(8, 0, 8, 0);  // WHY: Minimal top/bottom padding for compact mode
                BatteryIcon.Width = 24;
                BatteryIcon.Height = 14;
                BatteryTip.Width = 2;
                BatteryTip.Height = 6;
                BatteryPercentText.FontSize = 18;
                BatterySection.Margin = new Thickness(0, 2, 0, 0);  // WHY: Shift down to center in trimmed frame
                TimeRemainingText.FontSize = 14;
                TimeRemainingText.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
                TimeRemainingText.Margin = new Thickness(0);  // WHY: Reset from MicroTime's 7px margin
                TimeRemainingSection.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                TimeRemainingSection.Margin = new Thickness(0);
                ChargingStatusText.FontSize = 10;
                // =====================================================================
                // POWER MODE ICON POSITIONING (Compact Mode)
                // =====================================================================
                // The power mode icons (🐢 turtle, ⚡ lightning, 🚀 rocket) have different
                // visual baselines due to their emoji designs.
                // 
                // HOW IT WORKS:
                // 1. Margin.Top = -5 sets the BASE position for the turtle icon (🐢)
                // 2. RenderTransform shifts ONLY lightning/rocket down from that baseline
                // 3. RenderTransform does NOT affect layout - prevents text from shifting
                //
                // TO ADJUST ICON POSITIONS:
                // - Move ALL icons: Change Margin (line below). Negative = up, Positive = down
                // - Move ONLY lightning/rocket: Change TranslateTransform Y value (currently 3)
                // - Move ONLY turtle: Would need separate logic (not currently needed)
                //
                // NOTE: This code runs BOTH at mode switch AND in UpdateBatteryAndPowerMode()
                //       when power mode changes. Keep BOTH locations in sync!
                //       Search for "DisplayMode.Compact" and "TranslateTransform" to find both.
                // =====================================================================
                PowerModeGauge.FontSize = 20;
                PowerModeGauge.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                PowerModeGauge.Margin = new Thickness(0, -5, 0, 0);  // Base position (turtle)
                {
                    var currentMode = _powerModeService.GetCurrentModeName();
                    bool isTurtle = currentMode == "Best Power Efficiency";
                    // Lightning/rocket offset: 1px down from turtle (was 3, moved up 2px).
                    PowerModeGauge.RenderTransform = isTurtle ? null : new TranslateTransform(0, 1);
                }
                Height = HeightCompact;
                Width = WidthCompact;
                break;
                
            case DisplayMode.CompactClockAlt:
                // WHY: CompactClockAlt shows clock time under battery time (replacing "remaining" text)
                TelemetrySection.Visibility = Visibility.Collapsed;
                ClocksSection.Visibility = Visibility.Collapsed;
                TimeRemainingSection.Visibility = Visibility.Visible;
                BatterySection.Visibility = Visibility.Visible;
                ClockTimeText.Visibility = Visibility.Collapsed;
                ChargingStatusText.Visibility = Visibility.Collapsed;  // WHY: Hide "remaining" text
                ClockAltTimeText.Visibility = Visibility.Visible;  // WHY: Show clock time below battery time
                UptimeSection.Visibility = Visibility.Collapsed;
                // Reset to normal sizes and padding (same as Compact)
                MainContainer.Padding = new Thickness(8, 0, 8, 0);  // WHY: Same as Compact
                BatteryIcon.Width = 24;
                BatteryIcon.Height = 14;
                BatteryTip.Width = 2;
                BatteryTip.Height = 6;
                BatteryPercentText.FontSize = 18;
                BatterySection.Margin = new Thickness(0, 2, 0, 0);  // WHY: Same as Compact
                TimeRemainingText.FontSize = 14;
                TimeRemainingText.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
                TimeRemainingText.Margin = new Thickness(0);
                TimeRemainingSection.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                TimeRemainingSection.Margin = new Thickness(0);
                PowerModeGauge.FontSize = 20;
                PowerModeGauge.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                PowerModeGauge.Margin = new Thickness(0, -5, 0, 0);
                {
                    var currentMode = _powerModeService.GetCurrentModeName();
                    bool isTurtle = currentMode == "Best Power Efficiency";
                    // WHY: Lightning/rocket offset 1px (was 3, moved up 2px)
                    PowerModeGauge.RenderTransform = isTurtle ? null : new TranslateTransform(0, 1);
                }
                Height = HeightCompact;  // WHY: Same height as Compact mode
                Width = WidthCompact;
                break;
                
            case DisplayMode.Standard:
                TelemetrySection.Visibility = Visibility.Visible;
                ClocksSection.Visibility = Visibility.Collapsed;
                TimeRemainingSection.Visibility = Visibility.Visible;
                BatterySection.Visibility = Visibility.Visible;
                ChargingStatusText.Visibility = Visibility.Visible;
                ClockAltTimeText.Visibility = Visibility.Collapsed;  // WHY: Hide clock alt text when leaving CompactClockAlt
                UptimeSection.Visibility = Visibility.Collapsed;
                // Reset to normal sizes and padding
                MainContainer.Padding = new Thickness(8, 6, 8, 4);
                BatteryIcon.Width = 24;
                BatteryIcon.Height = 14;
                BatteryTip.Width = 2;
                BatteryTip.Height = 6;
                BatteryPercentText.FontSize = 18;
                BatterySection.Margin = new Thickness(0);
                TimeRemainingText.FontSize = 14;
                TimeRemainingText.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
                TimeRemainingText.Margin = new Thickness(0);  // WHY: Reset from MicroTime's 7px margin
                TimeRemainingSection.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                TimeRemainingSection.Margin = new Thickness(0);
                PowerModeGauge.FontSize = 20;
                PowerModeGauge.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                PowerModeGauge.Margin = new Thickness(0, -5, 0, 0);
                PowerModeGauge.RenderTransform = null;  // WHY: Reset from Micro modes
                Height = HeightStandard;
                Width = WidthStandard;
                break;
                
            case DisplayMode.Advanced:
                TelemetrySection.Visibility = Visibility.Visible;
                ClocksSection.Visibility = Visibility.Visible;
                HardwareNamesSection.Visibility = Visibility.Collapsed;  // WHY: Hardware names only in AdvancedInfo
                TimeRemainingSection.Visibility = Visibility.Visible;
                BatterySection.Visibility = Visibility.Visible;
                ChargingStatusText.Visibility = Visibility.Visible;
                ClockAltTimeText.Visibility = Visibility.Collapsed;  // WHY: Hide clock alt text when leaving CompactClockAlt
                UptimeSection.Visibility = Visibility.Visible;  // WHY: Show uptime in Advanced mode only
                // Reset to normal sizes and padding
                MainContainer.Padding = new Thickness(8, 6, 8, 4);
                BatteryIcon.Width = 24;
                BatteryIcon.Height = 14;
                BatteryTip.Width = 2;
                BatteryTip.Height = 6;
                BatteryPercentText.FontSize = 18;
                BatterySection.Margin = new Thickness(0);
                TimeRemainingText.FontSize = 14;
                TimeRemainingText.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
                TimeRemainingText.Margin = new Thickness(0);  // WHY: Reset from MicroTime's 7px margin
                TimeRemainingSection.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                TimeRemainingSection.Margin = new Thickness(0);
                PowerModeGauge.FontSize = 20;
                PowerModeGauge.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                PowerModeGauge.Margin = new Thickness(0, -5, 0, 0);
                PowerModeGauge.RenderTransform = null;  // WHY: Reset from Micro modes
                Height = HeightAdvanced + 30;  // WHY: Extra height for uptime section
                Width = WidthAdvanced;
                break;
                
            case DisplayMode.AdvancedInfo:
                TelemetrySection.Visibility = Visibility.Visible;
                ClocksSection.Visibility = Visibility.Visible;
                HardwareNamesSection.Visibility = Visibility.Visible;  // WHY: Show hardware names in AdvancedInfo only
                TimeRemainingSection.Visibility = Visibility.Visible;
                BatterySection.Visibility = Visibility.Visible;
                ChargingStatusText.Visibility = Visibility.Visible;
                ClockAltTimeText.Visibility = Visibility.Collapsed;  // WHY: Hide clock alt text when leaving CompactClockAlt
                UptimeSection.Visibility = Visibility.Visible;
                // Reset to normal sizes and padding
                MainContainer.Padding = new Thickness(8, 6, 8, 4);
                BatteryIcon.Width = 24;
                BatteryIcon.Height = 14;
                BatteryTip.Width = 2;
                BatteryTip.Height = 6;
                BatteryPercentText.FontSize = 18;
                BatterySection.Margin = new Thickness(0);
                TimeRemainingText.FontSize = 14;
                TimeRemainingText.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
                TimeRemainingText.Margin = new Thickness(0);
                TimeRemainingSection.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                TimeRemainingSection.Margin = new Thickness(0);
                PowerModeGauge.FontSize = 20;
                PowerModeGauge.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                PowerModeGauge.Margin = new Thickness(0, -5, 0, 0);
                PowerModeGauge.RenderTransform = null;
                Height = HeightAdvancedInfo + 30;  // WHY: Extra height for uptime + hardware names
                Width = WidthAdvanced;
                break;
        }
        
        // Lazy init: Open ClockMonitor when Advanced mode is first used
        // WHY: ClockMonitor is needed for both Advanced and AdvancedInfo modes
        // WHY: ClockMonitor stays open once initialized - WMI crashes when queried from
        // background threads, so we query CPU info once during Open() on the main thread.
        // Closing and reopening would require re-querying on the next Open(), causing crashes.
        bool needsClockMonitor = mode is DisplayMode.Advanced or DisplayMode.AdvancedInfo;
        if (needsClockMonitor && !_clockMonitor.IsOpen)
        {
            _clockMonitor.Open();
        }
        // NOTE: Don't close ClockMonitor when switching away - keep it open for app lifetime
        // to avoid WMI threading issues on re-initialization (minimal memory overhead)
    }

    /// <summary>
    /// Sets the overlay window opacity percentage (0-100).
    /// WHY: Allows users to adjust overlay visibility to their preference.
    /// </summary>
    /// <param name="opacityPercent">Opacity value from 0 (invisible) to 100 (fully opaque).</param>
    public void SetOpacity(int opacityPercent)
    {
        CurrentOpacity = Math.Clamp(opacityPercent, 0, 100);
        Opacity = CurrentOpacity / 100.0;
    }

    /// <summary>
    /// Enables or disables click-through using Win32 extended window styles.
    /// </summary>
    /// <param name="enable">True to enable click-through, false to disable.</param>
    private void SetClickThrough(bool enable)
    {
        if (_hwnd == IntPtr.Zero) return;

        int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        if (enable)
        {
            SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT);
        }
        else
        {
            SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
        }
    }

    /// <summary>
    /// Timer tick handler. Updates battery/power every 2s, telemetry every 4s.
    /// </summary>
    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        _tickCount++;
        
        // Battery and power mode update every tick (2 seconds)
        UpdateBatteryAndPowerMode();
        
        // Telemetry update every 2nd tick (4 seconds) to reduce CPU usage
        if (_tickCount % 2 == 0)
        {
            _ = UpdateTelemetryAsync();
        }
    }

    /// <summary>
    /// Updates battery percentage, time remaining, and power mode gauge icon.
    /// Lightweight operation suitable for frequent updates.
    /// </summary>
    private void UpdateBatteryAndPowerMode()
    {
        // Update battery info (lightweight)
        _batteryMonitor.Refresh();
        
        BatteryPercentText.Text = $"{_batteryMonitor.Percentage}%";
        
        // WHY: Show charging bolt on battery icon in Micro mode to indicate charging state
        ChargingBolt.Visibility = (CurrentDisplayMode == DisplayMode.Micro && _batteryMonitor.IsCharging) 
            ? Visibility.Visible 
            : Visibility.Collapsed;
        
        // WHY: Use actual BatteryIcon width for fill calculation (18px in Micro, 24px in other modes)
        double maxFillWidth = BatteryIcon.Width - 4; // Account for border thickness and margin
        double fillWidth = (_batteryMonitor.Percentage / 100.0) * maxFillWidth;
        BatteryFill.Width = Math.Max(2, fillWidth);
        
        // Battery color based on level - applied to entire battery (border, fill, tip)
        var batteryBrush = _batteryMonitor.Percentage switch
        {
            <= 20 => (SolidColorBrush)FindResource("DangerBrush"),
            <= 40 => (SolidColorBrush)FindResource("WarningBrush"),
            _ => (SolidColorBrush)FindResource("AccentBrush")
        };
        BatteryFill.Background = batteryBrush;
        BatteryIcon.BorderBrush = batteryBrush;
        BatteryTip.Background = batteryBrush;
        
        // WHY: Track uptime since battery was unplugged at near-full charge.
        // The timer should only start when unplugged, not while still charging.
        // WHY: Use 98% threshold instead of 100% to handle degraded batteries that
        // may drop a few percent between unplugging and app startup/resume.
        if (_batteryMonitor.Percentage >= 98 && !_batteryMonitor.IsCharging && !_uptimeTracker.IsTracking)
        {
            // Battery is at 98%+ and just got unplugged - start tracking
            _uptimeTracker.NotifyFullCharge();
        }
        else if (_batteryMonitor.IsCharging && _uptimeTracker.IsTracking)
        {
            // Reset tracking when plugged back in (will restart when unplugged at 98%+)
            _uptimeTracker.Reset();
        }
        
        // WHY: Update accumulated time before displaying (adds delta since last update)
        if (_uptimeTracker.IsTracking)
        {
            _uptimeTracker.Update();
        }
        
        // Update uptime display (Advanced and AdvancedInfo modes)
        if ((CurrentDisplayMode == DisplayMode.Advanced || CurrentDisplayMode == DisplayMode.AdvancedInfo) && _uptimeTracker.IsTracking)
        {
            UptimeText.Text = _uptimeTracker.GetFormattedUptime() ?? "--";
        }
        else if (CurrentDisplayMode == DisplayMode.Advanced || CurrentDisplayMode == DisplayMode.AdvancedInfo)
        {
            UptimeText.Text = "--";
        }

        // Time remaining (and clock time in CompactClockAlt mode)
        // WHY: CompactClockAlt shows both clock time at top and battery time at bottom
        if (CurrentDisplayMode == DisplayMode.CompactClockAlt)
        {
            // Update clock time display below battery time
            var now = DateTime.Now;
            ClockAltTimeText.Text = now.ToString("h:mmtt").ToLower();
            
            // Show battery time remaining at bottom (no "remaining" text)
            if (_batteryMonitor.IsCharging)
            {
                TimeRemainingText.Text = "Chrg.";
            }
            else if (_batteryMonitor.TimeRemaining.TotalSeconds > 0)
            {
                var time = _batteryMonitor.TimeRemaining;
                TimeRemainingText.Text = time.TotalHours >= 1 
                    ? $"{(int)time.TotalHours}h {time.Minutes}m"
                    : $"{time.Minutes}m";
            }
            else
            {
                TimeRemainingText.Text = "Calc.";
            }
        }
        else if (_batteryMonitor.IsCharging)
        {
            TimeRemainingText.Text = "Chrg.";
            ChargingStatusText.Text = "plugged in";
            // WHY: Shift abbreviated text left in MicroTime for better centering
            if (CurrentDisplayMode == DisplayMode.MicroTime)
                TimeRemainingText.Margin = new Thickness(0, 0, 7, 0);
        }
        else if (_batteryMonitor.TimeRemaining.TotalSeconds > 0)
        {
            var time = _batteryMonitor.TimeRemaining;
            TimeRemainingText.Text = time.TotalHours >= 1 
                ? $"{(int)time.TotalHours}h {time.Minutes}m"
                : $"{time.Minutes}m";
            ChargingStatusText.Text = "remaining";
            // WHY: Reset margin for time display (it's already well-positioned)
            if (CurrentDisplayMode == DisplayMode.MicroTime)
                TimeRemainingText.Margin = new Thickness(0);
        }
        else
        {
            TimeRemainingText.Text = "Calc.";
            ChargingStatusText.Text = "";
            // WHY: Shift abbreviated text left in MicroTime for better centering
            if (CurrentDisplayMode == DisplayMode.MicroTime)
                TimeRemainingText.Margin = new Thickness(0, 0, 7, 0);
        }

        // Update power mode gauge icon
        var modeName = _powerModeService.GetCurrentModeName();
        PowerModeGauge.ToolTip = modeName;
        (PowerModeGauge.Text, PowerModeGauge.Foreground) = modeName switch
        {
            "Best Power Efficiency" => ("🐢", (SolidColorBrush)FindResource("AccentBrush")),     // Green
            "Best Performance" => ("🚀", (SolidColorBrush)FindResource("DangerBrush")),          // Red
            _ => ("⚡", (SolidColorBrush)FindResource("WarningBrush"))                            // Yellow
        };
        
        // WHY: Use RenderTransform instead of Margin to adjust icon position.
        // RenderTransform moves the icon visually WITHOUT affecting layout,
        // preventing the percentage text from shifting when switching power modes.
        if (CurrentDisplayMode == DisplayMode.Micro || CurrentDisplayMode == DisplayMode.MicroTime)
        {
            bool isTurtle = modeName == "Best Power Efficiency";
            // WHY: Micro (Percentage): push only icons up 2 pixels - turtle at -2, others at 1 (was 0/3)
            // MicroTime: this offset is combined with reduced top padding so both time AND icons move up 2px
            double yOffset = isTurtle ? -2 : 1;
            PowerModeGauge.RenderTransform = new TranslateTransform(0, yOffset);
        }
        else if (CurrentDisplayMode is DisplayMode.Compact or DisplayMode.CompactClockAlt)
        {
            // See documentation block in SetDisplayMode() -> DisplayMode.Compact case
            // for full explanation of icon positioning system.
            // KEEP IN SYNC with that location when adjusting values!
            bool isTurtle = modeName == "Best Power Efficiency";
            // Lightning/rocket offset: 1px down from turtle (was 3, moved up 2px)
            PowerModeGauge.RenderTransform = isTurtle ? null : new TranslateTransform(0, 1);
        }
        else
        {
            // Reset transform for non-Micro/Compact modes
            PowerModeGauge.RenderTransform = null;
        }
    }

    /// <summary>
    /// Asynchronously updates CPU, RAM, GPU usage and clock speeds.
    /// Runs on background thread to avoid UI blocking.
    /// </summary>
    private async Task UpdateTelemetryAsync()
    {
        // Skip telemetry updates in Compact and CompactClockAlt modes
        if (CurrentDisplayMode is DisplayMode.Compact or DisplayMode.CompactClockAlt)
            return;

        // WHY: Only include network monitoring in Advanced modes where it's displayed
        bool needsNetwork = CurrentDisplayMode is DisplayMode.Advanced or DisplayMode.AdvancedInfo;
        
        // Run telemetry refresh on background thread to avoid blocking UI
        await _telemetryMonitor.RefreshAsync(includeNetwork: needsNetwork);
        
        // Also refresh clocks if in Advanced or AdvancedInfo mode
        if (needsNetwork)
        {
            await _clockMonitor.RefreshAsync();
        }
        
        // Update UI on dispatcher thread
        await Dispatcher.InvokeAsync(() =>
        {
            CpuText.Text = $"{_telemetryMonitor.CpuUsage:F0}%";
            CpuBar.Value = _telemetryMonitor.CpuUsage;
            
            RamText.Text = $"{_telemetryMonitor.RamUsage:F0}%";
            RamBar.Value = _telemetryMonitor.RamUsage;
            
            GpuText.Text = $"{_telemetryMonitor.GpuUsage:F0}%";
            GpuBar.Value = _telemetryMonitor.GpuUsage;
            
            // Update clocks and hardware names if in Advanced or AdvancedInfo mode
            if (CurrentDisplayMode is DisplayMode.Advanced or DisplayMode.AdvancedInfo)
            {
                CpuClockText.Text = _clockMonitor.CpuFrequencyMHz > 0 
                    ? $"{_clockMonitor.CpuFrequencyMHz}" 
                    : "N/A";
                GpuClockText.Text = _clockMonitor.GpuFrequencyMHz > 0 
                    ? $"{_clockMonitor.GpuFrequencyMHz}" 
                    : "N/A";
                
                // WHY: Format network speed with arrows, show decimal for small values
                // Download in accent color (teal), upload in danger color (red)
                string FormatSpeed(float mbps) => mbps >= 10 ? $"{mbps:F0}" : $"{mbps:F1}";
                NetDownloadText.Text = $"↓{FormatSpeed(_telemetryMonitor.NetDownloadMbps)}";
                NetUploadText.Text = $"↑{FormatSpeed(_telemetryMonitor.NetUploadMbps)}";
                
                // WHY: Hardware names only shown in AdvancedInfo mode
                if (CurrentDisplayMode == DisplayMode.AdvancedInfo)
                {
                    CpuNameText.Text = $"CPU: {_clockMonitor.CpuName}";
                    GpuNameText.Text = _clockMonitor.GpuDetected 
                        ? $"GPU: {_clockMonitor.GpuName}" 
                        : "GPU: Not detected";
                }
            }
        });
    }

    /// <summary>
    /// Handles left mouse button for window dragging.
    /// </summary>
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // WHY: Mark user interaction to stop topmost re-assertion timer
        _hasUserInteracted = true;
        
        // Allow dragging the window
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    /// <summary>
    /// Handles Ctrl+Right-Click to show context menu on overlay.
    /// WHY: Provides quick access to menu without going to tray icon.
    /// </summary>
    private void Window_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // WHY: Mark user interaction to stop topmost re-assertion timer
        _hasUserInteracted = true;
        
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            TrayService?.ShowMenu();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Handles Ctrl+Click on power mode gauge to cycle through power modes.
    /// </summary>
    private void PowerModeGauge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Ctrl+Click to cycle power modes
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _powerModeService.CycleMode();
            UpdateBatteryAndPowerMode(); // Refresh display immediately
            e.Handled = true;
        }
    }

    /// <summary>
    /// Shows hand cursor on power mode gauge only when Ctrl is pressed.
    /// WHY: Indicates the element is clickable only with Ctrl modifier.
    /// </summary>
    private void PowerModeGauge_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        UpdatePowerModeGaugeCursor();
        // WHY: Subscribe to key events to update cursor when Ctrl is pressed/released while hovering
        PreviewKeyDown += PowerModeGauge_KeyStateChanged;
        PreviewKeyUp += PowerModeGauge_KeyStateChanged;
    }

    /// <summary>
    /// Resets cursor to default when leaving power mode gauge and unsubscribes from key events.
    /// </summary>
    private void PowerModeGauge_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        PowerModeGauge.Cursor = System.Windows.Input.Cursors.Arrow;
        PreviewKeyDown -= PowerModeGauge_KeyStateChanged;
        PreviewKeyUp -= PowerModeGauge_KeyStateChanged;
    }

    /// <summary>
    /// Updates cursor when Ctrl key state changes while mouse is over the power mode gauge.
    /// </summary>
    private void PowerModeGauge_KeyStateChanged(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl)
        {
            UpdatePowerModeGaugeCursor();
        }
    }

    /// <summary>
    /// Sets cursor based on whether Ctrl is currently pressed.
    /// </summary>
    private void UpdatePowerModeGaugeCursor()
    {
        PowerModeGauge.Cursor = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) 
            ? System.Windows.Input.Cursors.Hand 
            : System.Windows.Input.Cursors.Arrow;
    }
}

