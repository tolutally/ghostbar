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
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (for release builds)

## 🚀 Getting Started

### Setup
1. Clone the repository:
   ```powershell
   git clone https://github.com/tolutally/GhostBar.git
   cd GhostBar
   ```

2. Set your OpenAI API key as an environment variable:
   ```powershell
   # PowerShell (current session)
   $env:GHOSTBAR_OPENAI_API_KEY = "your-api-key-here"
   
   # Or set permanently (User level)
   [Environment]::SetEnvironmentVariable("GHOSTBAR_OPENAI_API_KEY", "your-api-key-here", "User")
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

- **API Key Security**: The API key is read from `GHOSTBAR_OPENAI_API_KEY` environment variable. Never commit secrets to Git.
- **SSL Bypass**: The current implementation disables SSL certificate validation for debugging. This should be removed for production use.
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

## 🚀 Release Guide

GhostBar is configured for automated cloud distribution via GitHub Actions.

### How to Create a Release
1.  **Commit changes** to your code.
2.  **Tag the release** with a version starting with `v` (e.g., `v1.0.0`):
    ```bash
    git tag v1.0.0
    git push origin v1.0.0
    ```
3.  **Wait for the Build**:
    *   Go to the **Actions** tab in GitHub.
    *   Watch the "GhostBar Release" workflow.
4.  **Download**:
    *   Once complete, a new **Release** will appear in the GitHub sidebar.
    *   It will contain `GhostBar-Release.zip` ready for download.

### Manual Trigger
You can also manually trigger a build without tagging:
1.  Go to **Actions** > **GhostBar Release**.
2.  Click **Run workflow**.

---

## 📄 License


MIT License - feel free to use and modify as needed.
