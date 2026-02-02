#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using Vosk;

namespace GhostBar
{
    /// <summary>
    /// A single transcript segment with timing and optional speaker info
    /// </summary>
    public class TranscriptSegment
    {
        public double StartTime { get; set; }
        public double EndTime { get; set; }
        public string Text { get; set; } = "";
        public double[]? SpeakerVector { get; set; }
        public string? SpeakerId { get; set; }
        public List<WordInfo>? Words { get; set; }
    }

    /// <summary>
    /// Word-level timing information
    /// </summary>
    public class WordInfo
    {
        public string Word { get; set; } = "";
        public double Start { get; set; }
        public double End { get; set; }
        public double Confidence { get; set; }
    }

    /// <summary>
    /// Wrapper around Vosk for speech recognition with word timestamps and speaker vectors
    /// </summary>
    public class VoskTranscriber : IDisposable
    {
        private Model? _model;
        private SpkModel? _spkModel;
        private VoskRecognizer? _recognizer;
        private bool _isInitialized;
        private readonly object _lock = new object();

        /// <summary>
        /// Raised when a partial result is available (during speech)
        /// </summary>
        public event EventHandler<PartialResultEventArgs>? PartialResult;

        /// <summary>
        /// Raised when a final result is available (utterance complete)
        /// </summary>
        public event EventHandler<TranscriptSegmentEventArgs>? FinalResult;

        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// Initialize the transcriber with model paths
        /// </summary>
        /// <param name="modelPath">Path to the Vosk speech model folder</param>
        /// <param name="spkModelPath">Optional path to the speaker model folder</param>
        public void Initialize(string modelPath, string? spkModelPath = null)
        {
            lock (_lock)
            {
                if (_isInitialized)
                {
                    Dispose();
                }

                try
                {
                    Logger.Info($"VoskTranscriber initializing with model: {modelPath}");

                    // Set Vosk log level (0 = info, -1 = quiet)
                    Vosk.Vosk.SetLogLevel(0);

                    // Load the speech model
                    _model = new Model(modelPath);
                    Logger.Info("Speech model loaded");

                    // Load speaker model if provided
                    if (!string.IsNullOrEmpty(spkModelPath))
                    {
                        try
                        {
                            _spkModel = new SpkModel(spkModelPath);
                            Logger.Info("Speaker model loaded");
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Failed to load speaker model: {ex.Message}");
                            _spkModel = null;
                        }
                    }

                    // Create recognizer at 16kHz
                    _recognizer = new VoskRecognizer(_model, 16000.0f);
                    _recognizer.SetMaxAlternatives(0);
                    _recognizer.SetWords(true); // Enable word-level timestamps

                    // Add speaker model if available
                    if (_spkModel != null)
                    {
                        _recognizer.SetSpkModel(_spkModel);
                    }

                    _isInitialized = true;
                    Logger.Info("VoskTranscriber initialized successfully");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to initialize VoskTranscriber: {ex.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// Process audio data and emit results
        /// </summary>
        /// <param name="buffer">16kHz 16-bit mono PCM audio data</param>
        /// <param name="bytesRecorded">Number of bytes in buffer</param>
        public void ProcessAudio(byte[] buffer, int bytesRecorded)
        {
            if (!_isInitialized || _recognizer == null)
            {
                Logger.Error("VoskTranscriber not initialized");
                return;
            }

            lock (_lock)
            {
                try
                {
                    if (_recognizer.AcceptWaveform(buffer, bytesRecorded))
                    {
                        // Utterance complete - get final result
                        var resultJson = _recognizer.Result();
                        var segment = ParseResult(resultJson);
                        
                        if (segment != null && !string.IsNullOrWhiteSpace(segment.Text))
                        {
                            FinalResult?.Invoke(this, new TranscriptSegmentEventArgs(segment));
                        }
                    }
                    else
                    {
                        // Still processing - get partial result
                        var partialJson = _recognizer.PartialResult();
                        var partialText = ParsePartialResult(partialJson);
                        
                        if (!string.IsNullOrWhiteSpace(partialText))
                        {
                            PartialResult?.Invoke(this, new PartialResultEventArgs(partialText));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error processing audio: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Force final result at end of stream
        /// </summary>
        public TranscriptSegment? Finalize()
        {
            if (!_isInitialized || _recognizer == null)
                return null;

            lock (_lock)
            {
                try
                {
                    var resultJson = _recognizer.FinalResult();
                    return ParseResult(resultJson);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error finalizing: {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// Reset the recognizer state for a new session
        /// </summary>
        public void Reset()
        {
            lock (_lock)
            {
                _recognizer?.Reset();
            }
        }

        private TranscriptSegment? ParseResult(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var segment = new TranscriptSegment();

                // Get text
                if (root.TryGetProperty("text", out var textElement))
                {
                    segment.Text = textElement.GetString() ?? "";
                }

                if (string.IsNullOrWhiteSpace(segment.Text))
                    return null;

                // Get word-level timestamps
                if (root.TryGetProperty("result", out var resultArray))
                {
                    segment.Words = new List<WordInfo>();
                    double minStart = double.MaxValue;
                    double maxEnd = 0;

                    foreach (var wordElement in resultArray.EnumerateArray())
                    {
                        var word = new WordInfo
                        {
                            Word = wordElement.GetProperty("word").GetString() ?? "",
                            Start = wordElement.GetProperty("start").GetDouble(),
                            End = wordElement.GetProperty("end").GetDouble(),
                            Confidence = wordElement.TryGetProperty("conf", out var conf) ? conf.GetDouble() : 1.0
                        };

                        segment.Words.Add(word);

                        if (word.Start < minStart) minStart = word.Start;
                        if (word.End > maxEnd) maxEnd = word.End;
                    }

                    segment.StartTime = minStart == double.MaxValue ? 0 : minStart;
                    segment.EndTime = maxEnd;
                }

                // Get speaker vector if available
                if (root.TryGetProperty("spk", out var spkArray))
                {
                    var vectorList = new List<double>();
                    foreach (var val in spkArray.EnumerateArray())
                    {
                        vectorList.Add(val.GetDouble());
                    }
                    segment.SpeakerVector = vectorList.ToArray();
                }

                return segment;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error parsing Vosk result: {ex.Message}");
                return null;
            }
        }

        private string ParsePartialResult(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("partial", out var partialElement))
                {
                    return partialElement.GetString() ?? "";
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error parsing partial result: {ex.Message}");
            }
            return "";
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _recognizer?.Dispose();
                _recognizer = null;

                _spkModel?.Dispose();
                _spkModel = null;

                _model?.Dispose();
                _model = null;

                _isInitialized = false;
            }
        }
    }

    /// <summary>
    /// Event args for partial results
    /// </summary>
    public class PartialResultEventArgs : EventArgs
    {
        public string Text { get; }
        public PartialResultEventArgs(string text) => Text = text;
    }

    /// <summary>
    /// Event args for final transcript segments
    /// </summary>
    public class TranscriptSegmentEventArgs : EventArgs
    {
        public TranscriptSegment Segment { get; }
        public TranscriptSegmentEventArgs(TranscriptSegment segment) => Segment = segment;
    }
}
