using System;
using System.IO;

namespace GhostBar
{
    public static class Logger
    {
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GhostBar", "Logs");
        
        private static readonly string LogFile;
        private static readonly object _lock = new object();

        static Logger()
        {
            // Ensure log directory exists
            Directory.CreateDirectory(LogDirectory);
            
            // Create log file with date
            var date = DateTime.Now.ToString("yyyy-MM-dd");
            LogFile = Path.Combine(LogDirectory, $"ghostbar_{date}.log");
        }

        public static void Log(string category, string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var entry = $"[{timestamp}] [{category}] {message}";
            
            lock (_lock)
            {
                try
                {
                    File.AppendAllText(LogFile, entry + Environment.NewLine);
                }
                catch
                {
                    // Silently fail if logging fails
                }
            }

            // Also output to debug console
            System.Diagnostics.Debug.WriteLine(entry);
        }

        public static void Action(string action)
        {
            Log("ACTION", action);
        }

        public static void API(string method, string endpoint, string status)
        {
            Log("API", $"{method} {endpoint} -> {status}");
        }

        public static void APIRequest(string endpoint, string body)
        {
            Log("API-REQ", $"{endpoint} | Body: {Truncate(body, 200)}");
        }

        public static void APIResponse(string endpoint, int statusCode, string body)
        {
            Log("API-RES", $"{endpoint} | Status: {statusCode} | Body: {Truncate(body, 500)}");
        }

        public static void Error(string message, Exception? ex = null)
        {
            if (ex != null)
            {
                Log("ERROR", $"{message} | Exception: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Log("ERROR", $"  Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                }
            }
            else
            {
                Log("ERROR", message);
            }
        }

        public static void Info(string message)
        {
            Log("INFO", message);
        }

        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "(empty)";
            if (text.Length <= maxLength) return text;
            return text.Substring(0, maxLength) + "...";
        }

        public static string GetLogFilePath()
        {
            return LogFile;
        }
    }
}
