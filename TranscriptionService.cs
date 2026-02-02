#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace GhostBar
{
    /// <summary>
    /// Orchestrates audio capture, transcription, and speaker tracking
    /// </summary>
    public class TranscriptionService : IDisposable
    {
        private readonly AudioCapture _audioCapture;
        private readonly VoskTranscriber _transcriber;
        private readonly SpeakerTracker _speakerTracker;
        private readonly List<TranscriptSegment> _segments;
        private readonly Stopwatch _elapsedTimer;
        
        private bool _isRunning;
        private double _audioTimeOffset;

        /// <summary>
        /// Raised when partial (in-progress) text is available
        /// </summary>
        public event EventHandler<string>? PartialTextUpdated;

        /// <summary>
        /// Raised when a complete transcript segment is available
        /// </summary>
        public event EventHandler<TranscriptSegment>? SegmentCompleted;

        /// <summary>
        /// Raised when transcription starts or stops
        /// </summary>
        public event EventHandler<bool>? StateChanged;

        /// <summary>
        /// Raised on error
        /// </summary>
        public event EventHandler<string>? Error;

        public bool IsRunning => _isRunning;
        public TimeSpan Elapsed => _elapsedTimer.Elapsed;
        public IReadOnlyList<TranscriptSegment> Segments => _segments;

        public TranscriptionService()
        {
            _audioCapture = new AudioCapture();
            _transcriber = new VoskTranscriber();
            _speakerTracker = new SpeakerTracker();
            _segments = new List<TranscriptSegment>();
            _elapsedTimer = new Stopwatch();

            // Wire up events
            _audioCapture.DataAvailable += OnAudioDataAvailable;
            _audioCapture.RecordingStopped += OnRecordingStopped;
            _transcriber.PartialResult += OnPartialResult;
            _transcriber.FinalResult += OnFinalResult;
        }

        /// <summary>
        /// Initialize with model paths. Must be called before Start().
        /// </summary>
        public void Initialize(string modelPath, string? spkModelPath = null)
        {
            _transcriber.Initialize(modelPath, spkModelPath);
        }

        /// <summary>
        /// Check if the service is initialized and ready
        /// </summary>
        public bool IsInitialized => _transcriber.IsInitialized;

        /// <summary>
        /// Start transcription with the specified audio source
        /// </summary>
        public void Start(AudioSourceMode mode, string? filePath = null)
        {
            if (_isRunning)
            {
                Stop();
            }

            if (!_transcriber.IsInitialized)
            {
                Error?.Invoke(this, "Transcriber not initialized. Please ensure models are downloaded.");
                return;
            }

            try
            {
                Logger.Info($"TranscriptionService starting in {mode} mode");

                // Reset state
                _segments.Clear();
                _speakerTracker.Reset();
                _transcriber.Reset();
                _audioTimeOffset = 0;

                // Start timer
                _elapsedTimer.Restart();

                // Start audio capture
                _audioCapture.Start(mode, filePath);
                
                _isRunning = true;
                StateChanged?.Invoke(this, true);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to start transcription: {ex.Message}");
                Error?.Invoke(this, $"Failed to start: {ex.Message}");
            }
        }

        /// <summary>
        /// Stop transcription
        /// </summary>
        public void Stop()
        {
            if (!_isRunning)
                return;

            Logger.Info("TranscriptionService stopping");

            _audioCapture.Stop();
            _elapsedTimer.Stop();

            // Get any remaining audio
            var finalSegment = _transcriber.Finalize();
            if (finalSegment != null && !string.IsNullOrWhiteSpace(finalSegment.Text))
            {
                ProcessSegment(finalSegment);
            }

            _isRunning = false;
            StateChanged?.Invoke(this, false);
        }

        /// <summary>
        /// Export transcript to file
        /// </summary>
        public void Export(string filePath, TranscriptFormat format)
        {
            try
            {
                var content = format switch
                {
                    TranscriptFormat.PlainText => ExportAsPlainText(),
                    TranscriptFormat.SRT => ExportAsSRT(),
                    _ => ExportAsPlainText()
                };

                File.WriteAllText(filePath, content, Encoding.UTF8);
                Logger.Info($"Transcript exported to: {filePath}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to export transcript: {ex.Message}");
                Error?.Invoke(this, $"Failed to export: {ex.Message}");
            }
        }

        private string ExportAsPlainText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"GhostBar Meeting Transcript");
            sb.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Duration: {_elapsedTimer.Elapsed:hh\\:mm\\:ss}");
            sb.AppendLine($"Speakers: {_speakerTracker.SpeakerCount}");
            sb.AppendLine(new string('-', 50));
            sb.AppendLine();

            foreach (var segment in _segments)
            {
                var timestamp = TimeSpan.FromSeconds(segment.StartTime);
                var speaker = segment.SpeakerId ?? "Unknown";
                sb.AppendLine($"[{timestamp:hh\\:mm\\:ss}] [{speaker}]");
                sb.AppendLine(segment.Text);
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private string ExportAsSRT()
        {
            var sb = new StringBuilder();
            int index = 1;

            foreach (var segment in _segments)
            {
                var startTime = TimeSpan.FromSeconds(segment.StartTime);
                var endTime = TimeSpan.FromSeconds(segment.EndTime);
                var speaker = segment.SpeakerId ?? "Unknown";

                sb.AppendLine(index.ToString());
                sb.AppendLine($"{FormatSRTTime(startTime)} --> {FormatSRTTime(endTime)}");
                sb.AppendLine($"[{speaker}] {segment.Text}");
                sb.AppendLine();

                index++;
            }

            return sb.ToString();
        }

        private static string FormatSRTTime(TimeSpan ts)
        {
            return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";
        }

        private void OnAudioDataAvailable(object? sender, AudioDataEventArgs e)
        {
            if (!_isRunning)
                return;

            _transcriber.ProcessAudio(e.Buffer, e.BytesRecorded);
        }

        private void OnRecordingStopped(object? sender, EventArgs e)
        {
            if (_isRunning)
            {
                Stop();
            }
        }

        private void OnPartialResult(object? sender, PartialResultEventArgs e)
        {
            PartialTextUpdated?.Invoke(this, e.Text);
        }

        private void OnFinalResult(object? sender, TranscriptSegmentEventArgs e)
        {
            ProcessSegment(e.Segment);
        }

        private void ProcessSegment(TranscriptSegment segment)
        {
            // Adjust timestamps based on elapsed time
            var baseTime = _elapsedTimer.Elapsed.TotalSeconds;
            
            // If we have word-level timestamps, use them; otherwise estimate
            if (segment.Words == null || segment.Words.Count == 0)
            {
                segment.StartTime = baseTime - 2; // Estimate 2 seconds ago
                segment.EndTime = baseTime;
            }

            // Identify speaker if we have a vector
            if (segment.SpeakerVector != null && segment.SpeakerVector.Length > 0)
            {
                segment.SpeakerId = _speakerTracker.IdentifySpeaker(segment.SpeakerVector);
            }
            else
            {
                segment.SpeakerId = "Speaker 1"; // Default if no speaker model
            }

            _segments.Add(segment);
            SegmentCompleted?.Invoke(this, segment);

            Logger.Info($"Segment: [{segment.SpeakerId}] {segment.Text.Substring(0, Math.Min(50, segment.Text.Length))}...");
        }

        public void Dispose()
        {
            Stop();
            _audioCapture.Dispose();
            _transcriber.Dispose();
        }
    }

    /// <summary>
    /// Transcript export formats
    /// </summary>
    public enum TranscriptFormat
    {
        PlainText,
        SRT
    }
}
