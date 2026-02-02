#nullable enable
using System;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GhostBar
{
    /// <summary>
    /// Audio source modes for transcription
    /// </summary>
    public enum AudioSourceMode
    {
        Microphone,
        SystemAudio,
        Both,
        File
    }

    /// <summary>
    /// Unified audio capture manager supporting microphone, system audio, both, and file input.
    /// Outputs 16kHz, 16-bit, mono PCM required by Vosk.
    /// </summary>
    public class AudioCapture : IDisposable
    {
        private WaveInEvent? _micCapture;
        private WasapiLoopbackCapture? _loopbackCapture;
        private AudioFileReader? _fileReader;
        private WaveFileWriter? _mixedWriter;
        private MemoryStream? _mixedStream;
        
        private readonly WaveFormat _targetFormat = new WaveFormat(16000, 16, 1); // 16kHz, 16-bit, mono
        private AudioSourceMode _currentMode;
        private bool _isRecording;
        private string? _currentFilePath;

        // Buffers for mixing "Both" mode
        private byte[]? _micBuffer;
        private byte[]? _loopbackBuffer;
        private readonly object _bufferLock = new object();

        /// <summary>
        /// Raised when audio data is available in 16kHz 16-bit mono PCM format
        /// </summary>
        public event EventHandler<AudioDataEventArgs>? DataAvailable;

        /// <summary>
        /// Raised when recording stops (file ended or manually stopped)
        /// </summary>
        public event EventHandler? RecordingStopped;

        public bool IsRecording => _isRecording;
        public AudioSourceMode CurrentMode => _currentMode;

        /// <summary>
        /// Start capturing audio from the specified source
        /// </summary>
        public void Start(AudioSourceMode mode, string? filePath = null)
        {
            if (_isRecording)
                Stop();

            _currentMode = mode;
            _currentFilePath = filePath;
            _isRecording = true;

            Logger.Info($"AudioCapture starting in {mode} mode");

            switch (mode)
            {
                case AudioSourceMode.Microphone:
                    StartMicrophone();
                    break;
                case AudioSourceMode.SystemAudio:
                    StartLoopback();
                    break;
                case AudioSourceMode.Both:
                    StartBoth();
                    break;
                case AudioSourceMode.File:
                    if (string.IsNullOrEmpty(filePath))
                        throw new ArgumentException("File path required for File mode");
                    StartFile(filePath);
                    break;
            }
        }

        /// <summary>
        /// Stop capturing audio
        /// </summary>
        public void Stop()
        {
            if (!_isRecording)
                return;

            Logger.Info("AudioCapture stopping");
            _isRecording = false;

            _micCapture?.StopRecording();
            _loopbackCapture?.StopRecording();

            DisposeCaptures();
            RecordingStopped?.Invoke(this, EventArgs.Empty);
        }

        private void StartMicrophone()
        {
            _micCapture = new WaveInEvent
            {
                WaveFormat = _targetFormat,
                BufferMilliseconds = 100
            };

            _micCapture.DataAvailable += OnMicDataAvailable;
            _micCapture.RecordingStopped += OnRecordingStopped;
            _micCapture.StartRecording();
        }

        private void StartLoopback()
        {
            _loopbackCapture = new WasapiLoopbackCapture();
            _loopbackCapture.DataAvailable += OnLoopbackDataAvailable;
            _loopbackCapture.RecordingStopped += OnRecordingStopped;
            _loopbackCapture.StartRecording();
        }

        private void StartBoth()
        {
            // Start both mic and loopback
            _micCapture = new WaveInEvent
            {
                WaveFormat = _targetFormat,
                BufferMilliseconds = 100
            };

            _loopbackCapture = new WasapiLoopbackCapture();

            _micCapture.DataAvailable += OnMicDataForMixing;
            _loopbackCapture.DataAvailable += OnLoopbackDataForMixing;
            _micCapture.RecordingStopped += OnRecordingStopped;

            _micCapture.StartRecording();
            _loopbackCapture.StartRecording();
        }

        private void StartFile(string filePath)
        {
            try
            {
                _fileReader = new AudioFileReader(filePath);
                
                // Process file in background
                System.Threading.Tasks.Task.Run(() => ProcessAudioFile());
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to open audio file: {ex.Message}");
                throw;
            }
        }

        private void ProcessAudioFile()
        {
            if (_fileReader == null) return;

            try
            {
                // Resample to 16kHz mono
                var resampler = new WdlResamplingSampleProvider(_fileReader, 16000);
                var mono = resampler.ToMono();
                
                var buffer = new float[4096];
                int samplesRead;

                while (_isRecording && (samplesRead = mono.Read(buffer, 0, buffer.Length)) > 0)
                {
                    // Convert float samples to 16-bit PCM bytes
                    var pcmBytes = new byte[samplesRead * 2];
                    for (int i = 0; i < samplesRead; i++)
                    {
                        var sample = (short)(buffer[i] * 32767);
                        pcmBytes[i * 2] = (byte)(sample & 0xFF);
                        pcmBytes[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
                    }

                    DataAvailable?.Invoke(this, new AudioDataEventArgs(pcmBytes, pcmBytes.Length));
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error processing audio file: {ex.Message}");
            }
            finally
            {
                _isRecording = false;
                RecordingStopped?.Invoke(this, EventArgs.Empty);
            }
        }

        private void OnMicDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded > 0)
            {
                DataAvailable?.Invoke(this, new AudioDataEventArgs(e.Buffer, e.BytesRecorded));
            }
        }

        private void OnLoopbackDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded > 0 && _loopbackCapture != null)
            {
                // Resample loopback audio to 16kHz mono
                var converted = ConvertToTargetFormat(e.Buffer, e.BytesRecorded, _loopbackCapture.WaveFormat);
                if (converted != null && converted.Length > 0)
                {
                    DataAvailable?.Invoke(this, new AudioDataEventArgs(converted, converted.Length));
                }
            }
        }

        private void OnMicDataForMixing(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded > 0)
            {
                lock (_bufferLock)
                {
                    _micBuffer = new byte[e.BytesRecorded];
                    Array.Copy(e.Buffer, _micBuffer, e.BytesRecorded);
                    TryMixAndSend();
                }
            }
        }

        private void OnLoopbackDataForMixing(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded > 0 && _loopbackCapture != null)
            {
                var converted = ConvertToTargetFormat(e.Buffer, e.BytesRecorded, _loopbackCapture.WaveFormat);
                if (converted != null && converted.Length > 0)
                {
                    lock (_bufferLock)
                    {
                        _loopbackBuffer = converted;
                        TryMixAndSend();
                    }
                }
            }
        }

        private void TryMixAndSend()
        {
            // Simple mixing: if we have both buffers, mix them
            if (_micBuffer != null && _loopbackBuffer != null)
            {
                var mixedLength = Math.Min(_micBuffer.Length, _loopbackBuffer.Length);
                var mixed = new byte[mixedLength];

                // Mix by averaging samples
                for (int i = 0; i < mixedLength - 1; i += 2)
                {
                    var micSample = (short)((_micBuffer[i + 1] << 8) | _micBuffer[i]);
                    var loopSample = (short)((_loopbackBuffer[i + 1] << 8) | _loopbackBuffer[i]);
                    var mixedSample = (short)((micSample + loopSample) / 2);
                    
                    mixed[i] = (byte)(mixedSample & 0xFF);
                    mixed[i + 1] = (byte)((mixedSample >> 8) & 0xFF);
                }

                DataAvailable?.Invoke(this, new AudioDataEventArgs(mixed, mixedLength));

                _micBuffer = null;
                _loopbackBuffer = null;
            }
            else if (_micBuffer != null)
            {
                // If only mic data available, send it
                DataAvailable?.Invoke(this, new AudioDataEventArgs(_micBuffer, _micBuffer.Length));
                _micBuffer = null;
            }
        }

        private byte[]? ConvertToTargetFormat(byte[] buffer, int bytesRecorded, WaveFormat sourceFormat)
        {
            try
            {
                using var inputStream = new MemoryStream(buffer, 0, bytesRecorded);
                using var rawStream = new RawSourceWaveStream(inputStream, sourceFormat);
                
                // Convert to sample provider for resampling
                var sampleProvider = rawStream.ToSampleProvider();
                var resampled = new WdlResamplingSampleProvider(sampleProvider, 16000);
                var mono = resampled.ToMono();

                // Read resampled samples
                var outputSamples = new float[bytesRecorded]; // Oversize buffer
                var samplesRead = mono.Read(outputSamples, 0, outputSamples.Length);

                if (samplesRead == 0) return null;

                // Convert to 16-bit PCM
                var pcmBytes = new byte[samplesRead * 2];
                for (int i = 0; i < samplesRead; i++)
                {
                    var sample = (short)(outputSamples[i] * 32767);
                    pcmBytes[i * 2] = (byte)(sample & 0xFF);
                    pcmBytes[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
                }

                return pcmBytes;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error converting audio format: {ex.Message}");
                return null;
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                Logger.Error($"Recording error: {e.Exception.Message}");
            }
        }

        private void DisposeCaptures()
        {
            _micCapture?.Dispose();
            _micCapture = null;

            _loopbackCapture?.Dispose();
            _loopbackCapture = null;

            _fileReader?.Dispose();
            _fileReader = null;

            _mixedWriter?.Dispose();
            _mixedWriter = null;

            _mixedStream?.Dispose();
            _mixedStream = null;
        }

        public void Dispose()
        {
            Stop();
            DisposeCaptures();
        }
    }

    /// <summary>
    /// Event args for audio data
    /// </summary>
    public class AudioDataEventArgs : EventArgs
    {
        public byte[] Buffer { get; }
        public int BytesRecorded { get; }

        public AudioDataEventArgs(byte[] buffer, int bytesRecorded)
        {
            Buffer = buffer;
            BytesRecorded = bytesRecorded;
        }
    }
}
