---
description: How to build the Power Switch Overlay executable
---

# Building Power Switch Overlay

## Quick Build (Trimmed, Self-Contained)

```powershell
// turbo
dotnet publish -c Release -o ./publish
```

This creates a trimmed, self-contained executable at `./publish/PowerSwitchOverlay.exe`.

## Settings Location

User settings are stored in:
```
%LOCALAPPDATA%\PowerSwitchOverlay\settings.json
```
(e.g., `C:\Users\<username>\AppData\Local\PowerSwitchOverlay\settings.json`)

To reset to first-launch state (including showing the tutorial again):
```powershell
Remove-Item "$env:LOCALAPPDATA\PowerSwitchOverlay\settings.json" -Force
```

## Build Settings (in .csproj)

The project is configured with:
- **Self-contained**: No .NET installation required
- **Single file**: All .NET code in one EXE (native DLLs separate)
- **Trimmed**: Removes unused code to reduce size
- **Icon**: Uses `app.ico` (lightning bolt)

## Version Number

The app uses auto-incrementing version numbers:
- **Format**: `Major.Minor.Build.Revision`
- **Build**: Days since January 1, 2000
- **Revision**: Half-seconds since midnight

The version is displayed in the tray menu (e.g., `v1.0.9502`). Each build automatically gets a new version number - no manual updates needed!

## Regenerating the Icon

If you need to regenerate `app.ico`, run:

```powershell
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap(256,256)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)
$brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(0,212,170))
$s = 16
$points = @(
    [System.Drawing.PointF]::new(8*$s,0*$s),
    [System.Drawing.PointF]::new(4*$s,7*$s),
    [System.Drawing.PointF]::new(7*$s,7*$s),
    [System.Drawing.PointF]::new(5*$s,15*$s),
    [System.Drawing.PointF]::new(12*$s,6*$s),
    [System.Drawing.PointF]::new(9*$s,6*$s),
    [System.Drawing.PointF]::new(11*$s,0*$s)
)
$g.FillPolygon($brush, $points)
$icon = [System.Drawing.Icon]::FromHandle($bmp.GetHicon())
$fs = [System.IO.File]::Create('app.ico')
$icon.Save($fs)
$fs.Close()
```

After build, the `./publish` folder will contain:
- `PowerSwitchOverlay.exe` - Single self-contained executable (~160MB)

**Just distribute the single EXE file!**

---

## Building the Installer (Optional)

After building the executable, you can create an installer that auto-configures startup:

1. **Ensure the app is built first** (run the Quick Build above)
2. **Open Inno Setup Compiler** (search "Inno Setup" in Start Menu)
3. **Open** `setup.iss` from the project folder
4. **Press Ctrl+F9** or click Compile → Compile

This creates `./publish/PowerOverlaySetup.exe` which:
- Installs to Program Files
- Optionally sets up Windows autostart
- Creates Start Menu shortcut
- Adds proper "Add/Remove Programs" uninstall entry

### Distribution Options

| File | For Users Who... |
|------|-----------------|
| `PowerSwitchOverlay.exe` | Want portable/manual setup |
| `PowerOverlaySetup.exe` | Want one-click install with autostart |

