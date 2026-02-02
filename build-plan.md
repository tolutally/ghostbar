1. Define v0.1: what we’re actually building

For the first Windows build, I’d keep it simple but “magical”:

Features:

Always-on-top overlay window

Small rectangle (e.g., top center or top right)

Semi-transparent, dark, rounded corners

Contains:

One text box for your question

A scrollable text area for the last answer

Invisible to screen share / screenshots

Use SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE) so that:

Windows screen capture APIs (and most screen share tools that use them) skip this window.

Global hotkey

e.g., Ctrl + Shift + Space:

If overlay is hidden → show & focus input

If visible & input has text → send to OpenAI

If visible & empty → hide

OpenAI integration

Read API key from config file or secure storage.

Send your prompt to OpenAI.

Show answer in the overlay.

Panic hide

Hit Esc → instantly hide the overlay (or destroy window if you want hardcore).

That’s enough to feel like: “I have a secret ChatGPT bar on my Windows machine.”

2. The Windows magic: invisible to capture

On Windows 10/11, the key API is:

BOOL SetWindowDisplayAffinity(
  HWND  hWnd,
  DWORD dwAffinity
);


We’ll use:

WDA_EXCLUDEFROMCAPTURE (value 0x00000011 in newer SDKs)

This tells Windows: “When someone uses screen capture APIs, don’t include this window.”

Most screen sharing / recording tools (newer Zoom, Teams, etc.) use these APIs or related ones, so in practice your window will:

NOT show up when:

You screen share the whole screen
You use Snipping Tool / Print Screen (depending on how they’re implemented)
Or might appear as a black rectangle if something weird happens.

We’ll call this via P/Invoke from C#.

3. Implementation stack & architecture (Windows)

Let’s pick:

Language: C#
UI framework: WPF (simple and works well for this)
Project type: .NET 8 WPF app

3.1. Projects / classes

StealthOverlayApp (WPF project)

MainWindow.xaml / MainWindow.xaml.cs → the overlay

NativeMethods.cs → Win32 P/Invoke: SetWindowDisplayAffinity, hotkeys

HotkeyManager.cs → registers/unregisters global hotkeys

OpenAIClient.cs → handles HTTP calls to OpenAI

Settings.json or UserSecrets → store API key & config

4. Building the transparent always-on-top overlay (WPF)

High level:

WPF window with:

AllowsTransparency="True"

WindowStyle="None"

Background="Transparent"

Topmost="True"

Example XAML sketch (not full project, just the vibes):

<Window x:Class="StealthOverlay.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Width="600" Height="150"
        WindowStyle="None"
        ResizeMode="NoResize"
        AllowsTransparency="True"
        Background="Transparent"
        Topmost="True"
        ShowInTaskbar="False">
    <Border CornerRadius="12"
            Background="#AA111111"
            Padding="12"
            Margin="0">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
            </Grid.RowDefinitions>

            <!-- Input -->
            <TextBox x:Name="PromptTextBox"
                     Grid.Row="0"
                     Margin="0 0 0 8"
                     Background="#22000000"
                     Foreground="White"
                     BorderBrush="#33FFFFFF"
                     BorderThickness="1"
                     Padding="6"
                     FontSize="14"
                     KeyDown="PromptTextBox_KeyDown"/>

            <!-- Response -->
            <ScrollViewer Grid.Row="1">
                <TextBlock x:Name="ResponseTextBlock"
                           TextWrapping="Wrap"
                           Foreground="#DDFFFFFF"
                           FontSize="13"/>
            </ScrollViewer>
        </Grid>
    </Border>
</Window>


Then in the code-behind, we:

Position it at the top-center of the primary screen on Loaded.

Get the HWND and apply SetWindowDisplayAffinity.

5. Excluding the window from capture (P/Invoke)

In NativeMethods.cs:

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

public static class NativeMethods
{
    [DllImport("user32.dll")]
    public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    public const uint WDA_NONE = 0x00000000;
    public const uint WDA_MONITOR = 0x00000001;
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011; // Windows 10 2004+

    // Global hotkey
    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
}


In MainWindow.xaml.cs:

public partial class MainWindow : Window
{
    private HwndSource _hwndSource;
    private const int HOTKEY_ID = 1; // arbitrary

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Position at top center
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        this.Left = (screenWidth - this.Width) / 2;
        this.Top = 20;

        // Get handle
        _hwndSource = (HwndSource)PresentationSource.FromVisual(this);
        IntPtr hwnd = _hwndSource.Handle;

        // Exclude from capture
        NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);

        // Hook into WndProc for hotkeys
        _hwndSource.AddHook(HwndHook);

        // Register global hotkey: Ctrl + Shift + Space
        NativeMethods.RegisterHotKey(
            hwnd,
            HOTKEY_ID,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
            0x20 // VK_SPACE
        );
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_hwndSource != null)
        {
            NativeMethods.UnregisterHotKey(_hwndSource.Handle, HOTKEY_ID);
            _hwndSource.RemoveHook(HwndHook);
        }
        base.OnClosed(e);
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;

        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            ToggleVisibilityOrSend();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private async void ToggleVisibilityOrSend()
    {
        if (!IsVisible)
        {
            Show();
            Activate();
            PromptTextBox.Focus();
            return;
        }

        var text = PromptTextBox.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            Hide();
            return;
        }

        // Call OpenAI here
        ResponseTextBlock.Text = "Thinking...";
        var answer = await OpenAIClient.AskAsync(text);
        ResponseTextBlock.Text = answer;
        PromptTextBox.Clear();
    }

    private void PromptTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            ToggleVisibilityOrSend();
        }
        else if (e.Key == Key.Escape)
        {
            PromptTextBox.Clear();
            Hide();
        }
    }
}


That logic:

Registers Ctrl + Shift + Space as global hotkey.

If the overlay is hidden → show it and focus the textbox.

If visible and textbox has text → send to OpenAI.

If visible and textbox is empty → hide.

6. OpenAI client stub (C#)

Very simple version using HttpClient:

using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public static class OpenAIClient
{
    private static readonly HttpClient _httpClient = new HttpClient();
    private static readonly string _apiKey = "<PUT_YOUR_KEY_HERE>"; // later: load from config

    public static async Task<string> AskAsync(string prompt)
    {
        var requestBody = new
        {
            model = "gpt-5.1-mini",
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        var content = root
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return content ?? "";
    }
}


Later you’d:

Move the API key to a config file / secrets store.

Add error handling, retry, streaming, etc.

7. What I’d do next (iteration path)

Once you get that basic app running, the next steps:

Make the UI prettier & smaller
Shrink height, tweak opacity, nicer font.
Add a “pin last answer” toggle
So the answer stays on screen while you talk.
Add a quick “clear all” / panic hotkey
e.g., Ctrl + Shift + Esc → immediately clear response + hide.

Then later we can:
Add voice input and TTS output.
Add multi-monitor logic (e.g., move overlay to current cursor display).
Port the concepts to macOS with the constraints we discussed.