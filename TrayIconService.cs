// ==============================================================================
// TrayIconService.cs - System tray icon management
// Provides tray menu for overlay controls and power mode switching.
// ==============================================================================
//
// [CHANGE LOG]
// 2026-01-17 - AI - Added "Force on Top (Experimental)" toggle for staying above fullscreen apps
// 2026-01-08 - AI - Added "Launch Quick Start" menu item for tutorial re-access
// 2026-01-06 - AI - Renamed "Micro" to "Micro (Percentage)" for clarity
// 2026-01-06 - AI - Added Ko-fi support link in tray menu
// 2026-01-05 - AI - Fixed icon handle memory leak, added readonly modifiers
// 2026-01-05 - AI - Added auto-incrementing version number display to menu
// ==============================================================================

using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace PowerSwitchOverlay;

/// <summary>
/// Manages the system tray icon and context menu.
/// Provides menu for overlay controls, display modes, and power mode switching.
/// </summary>
public class TrayIconService : IDisposable
{
    // WHY: DestroyIcon is required to free GDI handles created by GetHicon()
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly MainWindow _overlayWindow;
    private readonly PowerModeService _powerModeService;
    private readonly Bitmap? _kofiIcon;  // WHY: Cache Ko-fi icon to avoid loading on every menu open
    private bool _disposed;

    /// <summary>
    /// Initializes tray icon with context menu and power icon.
    /// </summary>
    /// <param name="overlayWindow">Reference to the main overlay window for control callbacks.</param>
    public TrayIconService(MainWindow overlayWindow)
    {
        _overlayWindow = overlayWindow;
        _powerModeService = new PowerModeService();
        
        // WHY: Load Ko-fi icon once at startup instead of on every menu open
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("PowerSwitchOverlay.ko-fi.png");
            if (stream != null)
            {
                _kofiIcon = new Bitmap(stream);
            }
        }
        catch { /* Resource not found, proceed without icon */ }
        
        _contextMenu = new ContextMenuStrip();
        _contextMenu.Opening += (s, e) => RefreshMenu();

        _trayIcon = new NotifyIcon
        {
            Icon = CreatePowerIcon(),
            Visible = true,
            Text = "Power Switch Overlay",
            ContextMenuStrip = _contextMenu
        };

        _trayIcon.DoubleClick += (s, e) => _overlayWindow.ToggleVisibility();

        RefreshMenu();
    }

    /// <summary>
    /// Shows the context menu at the current cursor position.
    /// WHY: Allows Ctrl+Right-Click on overlay to show menu without going to tray.
    /// </summary>
    public void ShowMenu()
    {
        RefreshMenu();
        var cursorPos = System.Windows.Forms.Cursor.Position;
        _contextMenu.Show(cursorPos);
    }

    /// <summary>
    /// Creates a 16x16 icon with a lightning bolt for the tray.
    /// WHY: Properly manages GDI handle to prevent memory leak.
    /// </summary>
    private static Icon CreatePowerIcon()
    {
        using var bitmap = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        using var brush = new SolidBrush(Color.FromArgb(0, 212, 170)); // Accent color
        using var pen = new Pen(Color.FromArgb(0, 180, 150), 1);

        System.Drawing.Point[] bolt = {
            new(8, 0),
            new(4, 7),
            new(7, 7),
            new(5, 15),
            new(12, 6),
            new(9, 6),
            new(11, 0)
        };

        g.FillPolygon(brush, bolt);
        g.DrawPolygon(pen, bolt);

        // WHY: GetHicon() creates a GDI handle that must be destroyed after Icon.FromHandle
        IntPtr hIcon = bitmap.GetHicon();
        // WHY: Icon.FromHandle doesn't take ownership, so we create a clone and destroy the original
        var tempIcon = Icon.FromHandle(hIcon);
        var icon = (Icon)tempIcon.Clone();
        tempIcon.Dispose();
        DestroyIcon(hIcon);
        
        return icon;
    }

    /// <summary>
    /// Rebuilds the context menu. Called on menu open to ensure current state.
    /// </summary>
    private void RefreshMenu()
    {
        _contextMenu.Items.Clear();

        // Overlay controls section
        var overlayHeader = new ToolStripMenuItem("Overlay") { Enabled = false };
        _contextMenu.Items.Add(overlayHeader);

        var toggleVisibility = new ToolStripMenuItem("Toggle Visibility (Ctrl+Shift+O)");
        toggleVisibility.Click += (s, e) => _overlayWindow.ToggleVisibility();
        _contextMenu.Items.Add(toggleVisibility);

        var toggleClickThrough = new ToolStripMenuItem("Toggle Click-Through")
        {
            // WHY: Show checkmark when click-through is currently enabled
            Checked = _overlayWindow.IsClickThrough
        };
        toggleClickThrough.Click += (s, e) => _overlayWindow.ToggleClickThrough();
        _contextMenu.Items.Add(toggleClickThrough);

        // WHY: Force On Top experimental feature - stays above fullscreen apps and Photo Viewer
        var forceOnTop = new ToolStripMenuItem("Force on Top (Experimental)")
        {
            Checked = _overlayWindow.IsForceOnTop
        };
        forceOnTop.Click += (s, e) => _overlayWindow.ToggleForceOnTop();
        _contextMenu.Items.Add(forceOnTop);

        // WHY: Allow users to re-view the quick start tutorial at any time
        var launchTutorial = new ToolStripMenuItem("Launch Quick Start");
        launchTutorial.Click += (s, e) => _overlayWindow.LaunchTutorial();
        _contextMenu.Items.Add(launchTutorial);

        // Display Mode submenu
        var displayModeMenu = new ToolStripMenuItem("Display Mode");
        AddDisplayModeItem(displayModeMenu, "Micro (Percentage)", DisplayMode.Micro);
        AddDisplayModeItem(displayModeMenu, "Micro (Time)", DisplayMode.MicroTime);
        AddDisplayModeItem(displayModeMenu, "Compact", DisplayMode.Compact);
        AddDisplayModeItem(displayModeMenu, "Compact (Clock Alt)", DisplayMode.CompactClockAlt);
        AddDisplayModeItem(displayModeMenu, "Standard", DisplayMode.Standard);
        AddDisplayModeItem(displayModeMenu, "Advanced", DisplayMode.Advanced);
        AddDisplayModeItem(displayModeMenu, "Advanced (Info)", DisplayMode.AdvancedInfo);
        _contextMenu.Items.Add(displayModeMenu);

        // Opacity submenu
        var opacityMenu = new ToolStripMenuItem("Opacity");
        AddOpacityItem(opacityMenu, "15%", 15);
        AddOpacityItem(opacityMenu, "30%", 30);
        AddOpacityItem(opacityMenu, "60%", 60);
        AddOpacityItem(opacityMenu, "80%", 80);
        AddOpacityItem(opacityMenu, "100%", 100);
        _contextMenu.Items.Add(opacityMenu);

        _contextMenu.Items.Add(new ToolStripSeparator());

        // Power modes section
        var powerHeader = new ToolStripMenuItem("Power Mode") { Enabled = false };
        _contextMenu.Items.Add(powerHeader);

        var currentMode = _powerModeService.GetCurrentModeGuid().ToLowerInvariant();
        
        AddPowerModeItem("Best Power Efficiency", "961cc777-2547-4f9d-8174-7d86181b8a7a", currentMode);
        AddPowerModeItem("Balanced", "00000000-0000-0000-0000-000000000000", currentMode);
        AddPowerModeItem("Best Performance", "ded574b5-45a0-4f42-8737-46345c09c238", currentMode);

        _contextMenu.Items.Add(new ToolStripSeparator());

        // Run at Startup option
        var runAtStartup = new ToolStripMenuItem("Run at Startup")
        {
            // WHY: Show checkmark when app is registered to run at Windows startup
            Checked = StartupManager.IsEnabled
        };
        runAtStartup.Click += (s, e) => StartupManager.Toggle();
        _contextMenu.Items.Add(runAtStartup);

        _contextMenu.Items.Add(new ToolStripSeparator());

        // WHY: Display version number for user reference - auto-incremented on each build
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var versionText = version != null ? $"v{version.Major}.{version.Minor}.{version.Build}" : "v?.?.?";
        var versionItem = new ToolStripMenuItem(versionText) { Enabled = false };
        _contextMenu.Items.Add(versionItem);

        // WHY: Ko-fi support link to allow users to support the developer
        // Uses cached icon loaded at startup for faster menu opening
        var supportItem = new ToolStripMenuItem("Buy me a Coffee", _kofiIcon);
        supportItem.Click += (s, e) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://ko-fi.com/replicrafts",
                    UseShellExecute = true
                });
            }
            catch { /* Ignore if browser fails to open */ }
        };
        _contextMenu.Items.Add(supportItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        // Exit
        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (s, e) => ExitApplication();
        _contextMenu.Items.Add(exitItem);
    }

    /// <summary>
    /// Adds a display mode menu item with radio-style check mark.
    /// </summary>
    private void AddDisplayModeItem(ToolStripMenuItem parent, string name, DisplayMode mode)
    {
        var item = new ToolStripMenuItem(name)
        {
            Checked = _overlayWindow.CurrentDisplayMode == mode
        };
        item.Click += (s, e) => _overlayWindow.SetDisplayMode(mode);
        parent.DropDownItems.Add(item);
    }

    /// <summary>
    /// Adds an opacity menu item with radio-style check mark.
    /// WHY: Provides user control over overlay transparency via tray menu.
    /// </summary>
    private void AddOpacityItem(ToolStripMenuItem parent, string name, int opacityPercent)
    {
        var item = new ToolStripMenuItem(name)
        {
            Checked = _overlayWindow.CurrentOpacity == opacityPercent
        };
        item.Click += (s, e) => _overlayWindow.SetOpacity(opacityPercent);
        parent.DropDownItems.Add(item);
    }

    /// <summary>
    /// Adds a power mode menu item with radio-style check mark.
    /// Shows balloon notification on successful mode change.
    /// </summary>
    private void AddPowerModeItem(string name, string guid, string currentGuid)
    {
        var item = new ToolStripMenuItem(name)
        {
            Tag = guid,
            Checked = guid.Equals(currentGuid, StringComparison.OrdinalIgnoreCase)
        };
        item.Click += (s, e) =>
        {
            if (_powerModeService.SetMode(guid))
            {
                _trayIcon.BalloonTipTitle = "Power Mode Changed";
                _trayIcon.BalloonTipText = $"Switched to: {name}";
                _trayIcon.BalloonTipIcon = ToolTipIcon.Info;
                _trayIcon.ShowBalloonTip(2000);
            }
        };
        _contextMenu.Items.Add(item);
    }

    /// <summary>
    /// Exits the application cleanly, hiding tray icon first.
    /// </summary>
    private void ExitApplication()
    {
        _trayIcon.Visible = false;
        Application.Current.Shutdown();
    }

    /// <summary>
    /// Disposes tray icon and context menu resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _contextMenu.Dispose();
        _kofiIcon?.Dispose();  // WHY: Dispose cached bitmap resource
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}

