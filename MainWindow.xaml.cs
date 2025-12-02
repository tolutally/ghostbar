using System;
using System.Drawing;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Forms;

namespace GhostBar
{
    public partial class MainWindow : Window
    {
        private HwndSource? _hwndSource;
        private const int HOTKEY_ID = 1;  // Global hotkey id
        private NotifyIcon? _notifyIcon;
        private DateTime _lastEscapeTime = DateTime.MinValue;
        private const int DOUBLE_TAP_MS = 400; // Time window for double-tap

        public MainWindow()
        {
            InitializeComponent();
            Logger.Info("MainWindow initializing...");
            Loaded += MainWindow_Loaded;
            InitializeNotifyIcon();
        }

        private void InitializeNotifyIcon()
        {
            Logger.Action("Initializing NotifyIcon");
            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = SystemIcons.Application; // Default icon, can be replaced
            _notifyIcon.Text = "GhostBar - Ctrl+Space to show";
            _notifyIcon.Visible = true;
            _notifyIcon.DoubleClick += (s, e) => ShowWindow();
            
            // Context menu for the tray icon
            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Show", null, (s, e) => ShowWindow());
            contextMenu.Items.Add("Exit", null, (s, e) => ExitApplication());
            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        private void ShowWindow()
        {
            Logger.Action("ShowWindow called");
            Show();
            Activate();
            PromptTextBox.Focus();
        }

        private void ExitApplication()
        {
            Logger.Action("ExitApplication called");
            _notifyIcon?.Dispose();
            System.Windows.Application.Current.Shutdown();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Logger.Info("MainWindow Loaded event fired");
            
            // Position at top center of primary screen
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            Left = (screenWidth - Width) / 2;
            Top = 20;

            // Get HWND for this window
            _hwndSource = (HwndSource?)PresentationSource.FromVisual(this);
            if (_hwndSource == null)
            {
                Logger.Error("Failed to get HwndSource");
                return;
            }

            IntPtr hwnd = _hwndSource.Handle;
            Logger.Info($"Window handle: {hwnd}");

            // Exclude this window from screen capture
            NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
            Logger.Action("SetWindowDisplayAffinity applied");

            // Hook into window messages for the hotkey
            _hwndSource.AddHook(HwndHook);

            // Register global hotkey: Ctrl + Space
            bool registered = NativeMethods.RegisterHotKey(
                hwnd,
                HOTKEY_ID,
                NativeMethods.MOD_CONTROL,
                0x20 // VK_SPACE
            );
            Logger.Info($"Hotkey registration result: {registered}");

            // For testing: keep visible on startup
            // Hide(); // Uncomment this to start hidden
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_hwndSource != null)
            {
                NativeMethods.UnregisterHotKey(_hwndSource.Handle, HOTKEY_ID);
                _hwndSource.RemoveHook(HwndHook);
            }

            _notifyIcon?.Dispose();
            base.OnClosed(e);
        }

        private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
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
            // If window is hidden, show and focus input
            if (!IsVisible)
            {
                Logger.Action("Showing window via hotkey");
                Show();
                Activate();
                PromptTextBox.Focus();
                return;
            }

            // If visible but no text, hide
            var text = PromptTextBox.Text.Trim();
            if (string.IsNullOrEmpty(text))
            {
                Logger.Action("Hiding window via hotkey (no text)");
                Hide();
                return;
            }

            // Call OpenAI
            Logger.Action($"Sending prompt: {text.Substring(0, Math.Min(50, text.Length))}...");
            ResponseBorder.Visibility = Visibility.Visible;
            ResponseTextBlock.Text = "Thinking...";

            string answer = await OpenAIClient.AskAsync(text);

            ResponseTextBlock.Text = answer;
            PromptTextBox.Clear();
        }

        private void HideButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Action("Hide button clicked");
            Hide();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Action("Reset button clicked");
            ResetUI();
        }

        private void MenuButton_Click(object sender, RoutedEventArgs e)
        {
            // Menu functionality - can be expanded later
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            var text = PromptTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                Logger.Action($"Send button clicked with: {text.Substring(0, Math.Min(50, text.Length))}...");
                
                // Call OpenAI
                ResponseBorder.Visibility = Visibility.Visible;
                ResponseTextBlock.Text = "Thinking...";

                string answer = await OpenAIClient.AskAsync(text);

                ResponseTextBlock.Text = answer;
                PromptTextBox.Clear();
            }
        }

        private void PromptTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                Logger.Action("Enter key pressed");
                e.Handled = true;
                ToggleVisibilityOrSend();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                var now = DateTime.Now;
                
                // Check for double-tap
                if ((now - _lastEscapeTime).TotalMilliseconds < DOUBLE_TAP_MS)
                {
                    Logger.Action("Double-tap Escape detected - resetting");
                    // Double-tap: Reset everything
                    ResetUI();
                    _lastEscapeTime = DateTime.MinValue;
                }
                else
                {
                    Logger.Action("Single Escape - hiding");
                    // Single tap: Clear and hide
                    _lastEscapeTime = now;
                    PromptTextBox.Clear();
                    Hide();
                }
            }
        }

        private void ResetUI()
        {
            Logger.Action("ResetUI called");
            
            // Clear all inputs and responses
            PromptTextBox.Clear();
            ResponseTextBlock.Text = "";
            ResponseBorder.Visibility = Visibility.Collapsed;
            
            // Re-center window
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            Left = (screenWidth - Width) / 2;
            Top = 20;
            
            // Focus input
            PromptTextBox.Focus();
        }
    }
}

