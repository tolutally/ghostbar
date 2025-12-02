# GhostBar 👻

A **stealth overlay assistant** for Windows that is invisible to screen capture and screen sharing. Perfect for discreet AI-powered assistance during presentations, calls, or any screen-sharing scenario.

![.NET](https://img.shields.io/badge/.NET-10.0-purple)
![WPF](https://img.shields.io/badge/WPF-Windows-blue)
![License](https://img.shields.io/badge/License-MIT-green)

## ✨ Features

- **🔒 Stealth Mode** - Invisible to screen capture, screenshots, and screen sharing (uses Windows `WDA_EXCLUDEFROMCAPTURE`)
- **🤖 AI Integration** - OpenAI GPT-4o-mini powered responses
- **⌨️ Global Hotkeys** - Quick access from anywhere
- **📌 System Tray** - Runs quietly in the background
- **🎨 Modern UI** - Sleek, semi-transparent floating overlay

## 🎹 Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl + Space` | Toggle visibility / Send prompt (when text entered) |
| `Enter` | Send prompt to AI |
| `Escape` | Clear input and hide window |
| `Escape` (double-tap) | Reset UI and re-center window |

## 🖱️ UI Controls

- **Reset Button** (↻) - Clears input/response and re-centers the window
- **Hide Button** - Minimizes to system tray
- **Menu Button** (⋮⋮) - Reserved for future features
- **Close Button** (×) - Exits the application
- **Drag** - Click and drag the top bar to move the window

## 🏗️ Project Structure

```
GhostBar/
├── App.xaml / App.xaml.cs      # Application entry point
├── MainWindow.xaml / .cs       # Main overlay UI and logic
├── OpenAIClient.cs             # OpenAI API integration
├── NativeMethods.cs            # Windows P/Invoke declarations
├── Logger.cs                   # Debug logging utility
├── ChatMessage.cs              # Chat message model
└── GhostBar.csproj             # Project configuration
```

## 🔧 Technical Implementation

### Stealth Rendering
Uses Windows API `SetWindowDisplayAffinity` with `WDA_EXCLUDEFROMCAPTURE` flag to make the window invisible to:
- Screenshots (Win + PrintScreen)
- Screen recording software
- Video conferencing screen share (Teams, Zoom, etc.)

### Global Hotkey
Registers a system-wide hotkey using `RegisterHotKey` Windows API:
- Hotkey: `Ctrl + Space`
- Works even when GhostBar is not focused

### System Tray Integration
Uses Windows Forms `NotifyIcon` for system tray presence:
- Double-click tray icon to show window
- Right-click for context menu (Show/Exit)

## 📋 Requirements

- Windows 10/11
- .NET 10 SDK or later

## 🚀 Getting Started

### Setup
1. Clone the repository:
   ```powershell
   git clone https://github.com/tolutally/GhostBar.git
   cd GhostBar
   ```

2. Add your OpenAI API key in `OpenAIClient.cs`:
   ```csharp
   private static readonly string _apiKey = "your-api-key-here";
   ```

3. Build and run:
   ```powershell
   dotnet build
   dotnet run
   ```

### Publish (Self-Contained)
```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

## ⚠️ Important Notes

- **SSL Bypass**: The current implementation disables SSL certificate validation for debugging. This should be removed for production use.
- **API Key Security**: Move the API key to environment variables or a config file for production.
- **Stealth Limitation**: The window is still visible to the user; it's only hidden from capture software.

## 📝 Logs

Debug logs are written to:
```
%LocalAppData%\GhostBar\Logs\ghostbar_YYYY-MM-DD.log
```

## 🛠️ Development

Built with:
- **WPF** (Windows Presentation Foundation) for UI
- **Windows Forms** for NotifyIcon (system tray)
- **P/Invoke** for Windows API calls
- **HttpClient** for OpenAI API requests

## 📄 License

MIT License - feel free to use and modify as needed.
