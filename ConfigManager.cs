using System;
using System.IO;
using System.Text.Json;

namespace GhostBar
{
    public class AppConfig
    {
        public string OpenAIKey { get; set; } = "";
    }

    public static class ConfigManager
    {
        private static AppConfig _config = new AppConfig();
        private static readonly string _configPath;

        static ConfigManager()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var ghostDir = Path.Combine(appData, "GhostBar");
            Directory.CreateDirectory(ghostDir);
            _configPath = Path.Combine(ghostDir, "config.json");
            
            LoadConfig();
        }

        public static string OpenAIKey
        {
            get
            {
                // Priority: Environment Variable -> Config File
                var envKey = Environment.GetEnvironmentVariable("GHOSTBAR_OPENAI_API_KEY");
                if (!string.IsNullOrWhiteSpace(envKey)) return envKey;
                
                return _config.OpenAIKey;
            }
            set
            {
                _config.OpenAIKey = value;
                SaveConfig();
            }
        }

        private static void LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    _config = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load config: {ex.Message}");
            }
        }

        private static void SaveConfig()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_config, options);
                File.WriteAllText(_configPath, json);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save config: {ex.Message}");
            }
        }
    }
}
