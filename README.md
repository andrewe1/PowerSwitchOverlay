![Power Switch Overlay Banner](./Screenshots/Banner%201.jpg)

# Power Switch Overlay

**v1.2** — *First Public Release* 🎉

A transparent, always-on-top overlay widget for Windows that displays:
- **Battery** percentage and estimated time remaining
- **Power Mode** (Best Power Efficiency / Balanced / Best Performance)
- **System Telemetry** (CPU, RAM, GPU usage)

<table border="0" cellspacing="0" cellpadding="0">
<tr>
<td width="50%" valign="top">

### 🖱️ Drag & Drop Positioning

Position the overlay anywhere on your screen with a simple drag gesture. Your position is automatically saved and restored on next launch.

- Click and hold anywhere on the overlay
- Drag to your preferred location
- Release to lock in place

</td>
<td width="50%">

<img src="./Screenshots/drag%20gif.gif" alt="Drag demo" />

</td>
</tr>
</table>

<table border="0" cellspacing="0" cellpadding="0">
<tr>
<td width="50%">

<img src="./Screenshots/use%20gif.gif" alt="In-game demo" />

</td>
<td width="50%" valign="top">

### 🎮 In-Game Power Control

Keep the overlay visible over your games in borderless windowed mode. Switch between power plans on the fly without leaving your game.

- Position anywhere on screen while gaming
- Click the ⚡ power icon to cycle through plans
- Instantly switch from efficiency to performance mode

</td>
</tr>
</table>

<p align="center">
  <img src="./Screenshots/Banner%202.jpg" alt="100% battery charged" />
</p>

### ⏱️ Track Your Real Battery Life

Ever wonder how long your laptop *actually* lasts on a charge? The "Since 100%" timer tracks exactly how long you've been unplugged — helping you understand your real-world battery performance.

- See elapsed time since full charge
- Monitor actual battery drain over time
- Know exactly when you'll need to plug in

## Features

- 🎮 **Gaming-friendly** - Works in borderless windowed mode games
- 🖱️ **Draggable** - Position anywhere on screen
- ⌨️ **Hotkey** - `Ctrl+Shift+O` to toggle visibility
- 👆 **Click-through mode** - Let clicks pass through to apps underneath
- 🔋 **Battery aware** - Color-coded battery indicator (green/yellow/red)
- 📊 **Real-time telemetry** - Updates every 2 seconds
- 🖥️ **Remote desktop friendly** - Works over Parsec, RDP, and streaming apps — monitor your laptop battery while working fullscreen

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

- Windows 10/11 (64-bit)

## Build

```powershell
dotnet build
dotnet run
```

## Limitations

- ✅ Works in **windowed** and **borderless fullscreen** apps and games
- ⚠️ **Exclusive fullscreen** — use the experimental *Force On Top* feature (right-click tray icon)

## License

MIT
