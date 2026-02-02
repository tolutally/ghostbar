#nullable enable
using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Forms;
using System.Windows.Threading;
using Microsoft.Win32;

namespace GhostBar
{
    public partial class MainWindow : Window
    {
        private HwndSource? _hwndSource;
        private const int HOTKEY_ID = 1;  // Global hotkey id
        private NotifyIcon? _notifyIcon;
        private DateTime _lastEscapeTime = DateTime.MinValue;
        private const int DOUBLE_TAP_MS = 400; // Time window for double-tap

        // Transcription components
        private ModelManager? _modelManager;
        private TranscriptionService? _transcriptionService;
        private DispatcherTimer? _elapsedTimer;
        private bool _isTranscribing;

        public MainWindow()
        {
            InitializeComponent();
            Logger.Info("MainWindow initializing...");
            Loaded += MainWindow_Loaded;
            InitializeNotifyIcon();
            InitializeTranscription();
        }

        private async void InitializeTranscription()
        {
            try
            {
                _modelManager = new ModelManager();
                _transcriptionService = new TranscriptionService();

                // Wire up transcription events
                _transcriptionService.PartialTextUpdated += OnPartialTextUpdated;
                _transcriptionService.SegmentCompleted += OnSegmentCompleted;
                _transcriptionService.StateChanged += OnTranscriptionStateChanged;
                _transcriptionService.Error += OnTranscriptionError;

                // Setup elapsed timer
                _elapsedTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _elapsedTimer.Tick += (s, e) =>
                {
                    if (_transcriptionService != null)
                    {
                        ElapsedTimeText.Text = _transcriptionService.Elapsed.ToString(@"hh\:mm\:ss");
                    }
                };

                // Check if models need to be downloaded
                if (!_modelManager.IsModelAvailable)
                {
                    Logger.Info("Models not found - will download on first use");
                }
                else
                {
                    // Initialize transcription service with existing models
                    _transcriptionService.Initialize(
                        _modelManager.ModelPath,
                        _modelManager.IsSpeakerModelAvailable ? _modelManager.SpeakerModelPath : null
                    );
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to initialize transcription: {ex.Message}");
            }
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

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            Logger.Action("Settings button clicked");

            // Create a simple input dialog since WPF doesn't have a built-in one
            var inputDialog = new Window
            {
                Width = 400,
                Height = 180,
                Title = "Settings",
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2d, 0x37, 0x48))
            };

            var stackPanel = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };
            
            stackPanel.Children.Add(new System.Windows.Controls.TextBlock 
            { 
                Text = "OpenAI API Key:", 
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var passwordBox = new System.Windows.Controls.PasswordBox 
            { 
                PasswordChar = '•',
                Margin = new Thickness(0, 0, 0, 20),
                Height = 30
            };
            
            // Pre-fill
            if (!string.IsNullOrEmpty(ConfigManager.OpenAIKey))
            {
                passwordBox.Password = ConfigManager.OpenAIKey;
            }

            var saveBtn = new System.Windows.Controls.Button 
            { 
                Content = "Save Key",
                IsDefault = true,
                Height = 30,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x25, 0x63, 0xeb)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0)
            };

            saveBtn.Click += (s, args) =>
            {
                ConfigManager.OpenAIKey = passwordBox.Password;
                inputDialog.DialogResult = true;
                inputDialog.Close();
                System.Windows.MessageBox.Show("API Key saved!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            };

            stackPanel.Children.Add(passwordBox);
            stackPanel.Children.Add(saveBtn);
            
            inputDialog.Content = stackPanel;
            inputDialog.ShowDialog();
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

        #region Transcription Handlers

        private async void RecordButton_Click(object sender, RoutedEventArgs e)
        {
            if (_transcriptionService == null || _modelManager == null)
                return;

            if (_isTranscribing)
            {
                // Stop transcription
                StopTranscription();
            }
            else
            {
                // Start transcription
                await StartTranscriptionAsync();
            }
        }

        private async System.Threading.Tasks.Task StartTranscriptionAsync()
        {
            if (_transcriptionService == null || _modelManager == null)
                return;

            try
            {
                // Check if models need to be downloaded
                if (!_modelManager.IsModelAvailable)
                {
                    var result = System.Windows.MessageBox.Show(
                        "Speech models need to be downloaded (~140MB). This is a one-time download.\n\nProceed with download?",
                        "Download Required",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result != MessageBoxResult.Yes)
                        return;

                    // Show progress
                    PartialText.Text = "Downloading models...";
                    TranscriptBorder.Visibility = Visibility.Visible;

                    _modelManager.DownloadProgress += (s, args) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            PartialText.Text = $"{args.ModelName}: {args.Status}";
                        });
                    };

                    await _modelManager.EnsureModelsAsync();

                    // Initialize transcription service
                    _transcriptionService.Initialize(
                        _modelManager.ModelPath,
                        _modelManager.IsSpeakerModelAvailable ? _modelManager.SpeakerModelPath : null
                    );

                    PartialText.Text = "";
                }

                // Get audio source mode
                var mode = AudioSourceCombo.SelectedIndex switch
                {
                    0 => AudioSourceMode.Microphone,
                    1 => AudioSourceMode.SystemAudio,
                    2 => AudioSourceMode.Both,
                    3 => AudioSourceMode.File,
                    _ => AudioSourceMode.Microphone
                };

                string? filePath = null;

                // If file mode, show file dialog
                if (mode == AudioSourceMode.File)
                {
                    var openFileDialog = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "Select Audio File",
                        Filter = "Audio Files|*.wav;*.mp3;*.m4a;*.flac;*.ogg|All Files|*.*"
                    };

                    if (openFileDialog.ShowDialog() != true)
                        return;

                    filePath = openFileDialog.FileName;
                }

                // Clear previous transcript
                TranscriptPanel.Children.Clear();
                PartialText.Text = "";
                SpeakerCountText.Text = " (0 speakers)";

                // Start transcription
                _transcriptionService.Start(mode, filePath);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to start transcription: {ex.Message}");
                System.Windows.MessageBox.Show(
                    $"Failed to start transcription: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void StopTranscription()
        {
            _transcriptionService?.Stop();
        }

        private void OnTranscriptionStateChanged(object? sender, bool isRunning)
        {
            Dispatcher.Invoke(() =>
            {
                _isTranscribing = isRunning;

                if (isRunning)
                {
                    // Update UI for recording state
                    TranscriptBorder.Visibility = Visibility.Visible;
                    ElapsedTimeText.Visibility = Visibility.Visible;
                    ElapsedTimeText.Text = "00:00:00";
                    _elapsedTimer?.Start();

                    // Update button appearance (would need template access for full update)
                    RecordButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xdc, 0x26, 0x26)); // Red
                }
                else
                {
                    // Update UI for stopped state
                    _elapsedTimer?.Stop();
                    RecordButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x25, 0x63, 0xeb)); // Blue

                    // Enable save button if we have segments
                    SaveButton.IsEnabled = _transcriptionService?.Segments.Count > 0;
                    GenerateNotesButton.IsEnabled = _transcriptionService?.Segments.Count > 0;
                }
            });
        }

        private void OnPartialTextUpdated(object? sender, string text)
        {
            Dispatcher.Invoke(() =>
            {
                PartialText.Text = text;
            });
        }

        private void OnSegmentCompleted(object? sender, TranscriptSegment segment)
        {
            Dispatcher.Invoke(() =>
            {
                // Add segment to transcript panel
                var timestamp = TimeSpan.FromSeconds(segment.StartTime);
                var speakerId = segment.SpeakerId ?? "Unknown";

                var segmentPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

                // Header with timestamp and speaker
                var header = new TextBlock
                {
                    Text = $"[{timestamp:mm\\:ss}] {speakerId}",
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x99, 0xaa)),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold
                };

                // Segment text
                var text = new TextBlock
                {
                    Text = segment.Text,
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xcc, 0xdd, 0xee)),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0)
                };

                segmentPanel.Children.Add(header);
                segmentPanel.Children.Add(text);
                TranscriptPanel.Children.Add(segmentPanel);

                // Auto-scroll to bottom
                TranscriptScroller.ScrollToEnd();

                // Update speaker count
                if (_transcriptionService != null)
                {
                    // Count unique speakers from segments
                    var speakers = new System.Collections.Generic.HashSet<string>();
                    foreach (var seg in _transcriptionService.Segments)
                    {
                        if (!string.IsNullOrEmpty(seg.SpeakerId))
                            speakers.Add(seg.SpeakerId);
                    }
                    SpeakerCountText.Text = $" ({speakers.Count} speaker{(speakers.Count != 1 ? "s" : "")})";
                }

                // Clear partial text
                PartialText.Text = "";

                // Enable save button
                SaveButton.IsEnabled = true;
                GenerateNotesButton.IsEnabled = true;
            });
        }

        private void OnTranscriptionError(object? sender, string error)
        {
            Dispatcher.Invoke(() =>
            {
                PartialText.Text = $"Error: {error}";
            });
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_transcriptionService == null || _transcriptionService.Segments.Count == 0)
                return;

            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save Transcript",
                Filter = "Text File (*.txt)|*.txt|SRT Subtitles (*.srt)|*.srt",
                DefaultExt = ".txt",
                FileName = $"transcript_{DateTime.Now:yyyy-MM-dd_HHmmss}"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                var format = saveFileDialog.FilterIndex == 2 ? TranscriptFormat.SRT : TranscriptFormat.PlainText;
                _transcriptionService.Export(saveFileDialog.FileName, format);

                System.Windows.MessageBox.Show(
                    $"Transcript saved to:\n{saveFileDialog.FileName}",
                    "Saved",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private async void GenerateNotesButton_Click(object sender, RoutedEventArgs e)
        {
            if (_transcriptionService == null || _transcriptionService.Segments.Count == 0)
                return;

            Logger.Action("GenerateNotesButton clicked");

            // Build transcript string
            var sb = new System.Text.StringBuilder();
            foreach (var segment in _transcriptionService.Segments)
            {
                var speaker = segment.SpeakerId ?? "Unknown";
                sb.AppendLine($"[{speaker}]: {segment.Text}");
            }
            string transcript = sb.ToString();

            // Show loading state
            ResponseBorder.Visibility = Visibility.Visible;
            ResponseTextBlock.Text = "🧠 Analyzing transcript and generating notes...";
            
            // Construct prompt
            string prompt = $@"You are an expert notetaker. Please analyze the following meeting transcript and generate a structured summary.
Include:
1. A brief executive summary (2-3 sentences).
2. Key discussion points (bullet points).
3. Action items with assignees (if identifiable).

TRANSCRIPT:
{transcript}";

            try
            {
                // Send to AI
                string notes = await OpenAIClient.AskAsync(prompt);
                ResponseTextBlock.Text = notes;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error generating notes: {ex.Message}");
                ResponseTextBlock.Text = "❌ Failed to generate notes. Please check the logs.";
            }
        }

        #endregion
    }
}

