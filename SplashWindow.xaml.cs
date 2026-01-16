// ==============================================================================
// SplashWindow.xaml.cs - Loading splash screen
// Shows lightning bolt while app initializes.
// ==============================================================================
//
// [CHANGE LOG]
// 2026-01-11 - AI - Simplified to static lightning bolt, removed dot animation
// 2026-01-07 - AI - Created splash window for improved perceived startup time
// ==============================================================================

using System.Windows;

namespace PowerSwitchOverlay;

/// <summary>
/// Splash window shown during app initialization.
/// Displays a lightning bolt while heavy services initialize.
/// </summary>
public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Positions the splash at the specified screen coordinates.
    /// WHY: Matches where the main overlay will appear for seamless transition.
    /// </summary>
    public void SetPosition(double left, double top)
    {
        Left = left;
        Top = top;
    }

    /// <summary>
    /// Closes the splash window.
    /// </summary>
    public void CloseSplash()
    {
        Close();
    }
}

