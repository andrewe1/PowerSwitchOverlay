# Power Switch Overlay

**v1.2** — *First Public Release* 🎉

A transparent, always-on-top overlay widget for Windows that displays:
- **Battery** percentage and estimated time remaining
- **Power Mode** (Best Power Efficiency / Balanced / Best Performance)
- **System Telemetry** (CPU, RAM, GPU usage)

## Features

- 🎮 **Gaming-friendly** - Works in borderless windowed mode games
- 🖱️ **Draggable** - Position anywhere on screen
- ⌨️ **Hotkey** - `Ctrl+Shift+O` to toggle visibility
- 👆 **Click-through mode** - Let clicks pass through to apps underneath
- 🔋 **Battery aware** - Color-coded battery indicator (green/yellow/red)
- 📊 **Real-time telemetry** - Updates every 2 seconds

## Usage

1. Run `PowerSwitchOverlay.exe`
2. The overlay appears in the top-left corner
3. **Drag** to reposition
4. **Right-click** the tray icon (⚡) for options:
   - Toggle Visibility
   - Toggle Click-Through
   - Change Power Mode
   - Exit

## Hotkeys

| Hotkey | Action |
|--------|--------|
| `Ctrl+Shift+O` | Toggle overlay visibility |

## Requirements

- Windows 10/11
- .NET 8.0 Runtime

## Build

```powershell
dotnet build
dotnet run
```

## Limitations

- Works in **windowed** and **borderless windowed** modes ✅
- Does **NOT** work in **exclusive fullscreen** ❌ (Windows limitation)

## License

MIT
