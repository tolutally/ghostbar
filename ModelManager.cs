#nullable enable
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GhostBar
{
    /// <summary>
    /// Manages Vosk model downloads and storage
    /// </summary>
    public class ModelManager
    {
        private const string MODEL_NAME = "vosk-model-en-us-0.22-lgraph";
        private const string SPK_MODEL_NAME = "vosk-model-spk-0.4";
        private const string MODEL_URL = "https://alphacephei.com/vosk/models/vosk-model-en-us-0.22-lgraph.zip";
        private const string SPK_MODEL_URL = "https://alphacephei.com/vosk/models/vosk-model-spk-0.4.zip";

        private readonly string _modelsDirectory;
        private readonly HttpClient _httpClient;

        /// <summary>
        /// Raised during download to report progress (0-100)
        /// </summary>
        public event EventHandler<DownloadProgressEventArgs>? DownloadProgress;

        /// <summary>
        /// Path to the speech recognition model
        /// </summary>
        public string ModelPath => Path.Combine(_modelsDirectory, MODEL_NAME);

        /// <summary>
        /// Path to the speaker identification model
        /// </summary>
        public string SpeakerModelPath => Path.Combine(_modelsDirectory, SPK_MODEL_NAME);

        /// <summary>
        /// Check if the main speech model is available
        /// </summary>
        public bool IsModelAvailable => Directory.Exists(ModelPath) && 
            File.Exists(Path.Combine(ModelPath, "am", "final.mdl"));

        /// <summary>
        /// Check if the speaker model is available
        /// </summary>
        public bool IsSpeakerModelAvailable => Directory.Exists(SpeakerModelPath);

        public ModelManager()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _modelsDirectory = Path.Combine(localAppData, "GhostBar", "Models");
            
            // Ensure directory exists
            Directory.CreateDirectory(_modelsDirectory);

            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(30); // Long timeout for large downloads
        }

        /// <summary>
        /// Ensure all required models are downloaded
        /// </summary>
        public async Task EnsureModelsAsync(CancellationToken cancellationToken = default)
        {
            if (!IsModelAvailable)
            {
                Logger.Info("Speech model not found, downloading...");
                await DownloadModelAsync(MODEL_URL, MODEL_NAME, cancellationToken);
            }

            if (!IsSpeakerModelAvailable)
            {
                Logger.Info("Speaker model not found, downloading...");
                await DownloadModelAsync(SPK_MODEL_URL, SPK_MODEL_NAME, cancellationToken);
            }
        }

        /// <summary>
        /// Download a specific model
        /// </summary>
        private async Task DownloadModelAsync(string url, string modelName, CancellationToken cancellationToken)
        {
            var zipPath = Path.Combine(_modelsDirectory, $"{modelName}.zip");
            var extractPath = Path.Combine(_modelsDirectory, modelName);

            try
            {
                Logger.Info($"Downloading {modelName} from {url}");
                ReportProgress(modelName, 0, "Starting download...");

                // Download with progress
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var downloadedBytes = 0L;

                using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var buffer = new byte[81920];
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                        downloadedBytes += bytesRead;

                        if (totalBytes > 0)
                        {
                            var progress = (int)((downloadedBytes * 100) / totalBytes);
                            var mbDownloaded = downloadedBytes / (1024.0 * 1024.0);
                            var mbTotal = totalBytes / (1024.0 * 1024.0);
                            ReportProgress(modelName, progress, $"Downloading: {mbDownloaded:F1} / {mbTotal:F1} MB");
                        }
                    }
                }

                Logger.Info($"Download complete, extracting {modelName}...");
                ReportProgress(modelName, 100, "Extracting...");

                // Extract
                if (Directory.Exists(extractPath))
                {
                    Directory.Delete(extractPath, true);
                }

                ZipFile.ExtractToDirectory(zipPath, _modelsDirectory, true);

                // Clean up zip file
                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }

                Logger.Info($"{modelName} ready at: {extractPath}");
                ReportProgress(modelName, 100, "Ready");
            }
            catch (OperationCanceledException)
            {
                Logger.Info($"Download of {modelName} was cancelled");
                CleanupPartialDownload(zipPath, extractPath);
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to download {modelName}: {ex.Message}");
                CleanupPartialDownload(zipPath, extractPath);
                throw;
            }
        }

        private void CleanupPartialDownload(string zipPath, string extractPath)
        {
            try
            {
                if (File.Exists(zipPath))
                    File.Delete(zipPath);
                if (Directory.Exists(extractPath))
                    Directory.Delete(extractPath, true);
            }
            catch { /* Ignore cleanup errors */ }
        }

        private void ReportProgress(string modelName, int percent, string status)
        {
            DownloadProgress?.Invoke(this, new DownloadProgressEventArgs
            {
                ModelName = modelName,
                PercentComplete = percent,
                Status = status
            });
        }

        /// <summary>
        /// Get the total size of downloaded models
        /// </summary>
        public long GetModelsSizeBytes()
        {
            long totalSize = 0;

            if (Directory.Exists(ModelPath))
            {
                totalSize += GetDirectorySize(ModelPath);
            }

            if (Directory.Exists(SpeakerModelPath))
            {
                totalSize += GetDirectorySize(SpeakerModelPath);
            }

            return totalSize;
        }

        private static long GetDirectorySize(string path)
        {
            long size = 0;
            var di = new DirectoryInfo(path);
            
            foreach (var fi in di.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                size += fi.Length;
            }
            
            return size;
        }

        /// <summary>
        /// Delete all downloaded models
        /// </summary>
        public void DeleteModels()
        {
            try
            {
                if (Directory.Exists(ModelPath))
                {
                    Directory.Delete(ModelPath, true);
                    Logger.Info("Deleted speech model");
                }

                if (Directory.Exists(SpeakerModelPath))
                {
                    Directory.Delete(SpeakerModelPath, true);
                    Logger.Info("Deleted speaker model");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to delete models: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Download progress event args
    /// </summary>
    public class DownloadProgressEventArgs : EventArgs
    {
        public string ModelName { get; set; } = "";
        public int PercentComplete { get; set; }
        public string Status { get; set; } = "";
    }
}
