using System;
using System.IO;

namespace MavenInspector
{
    public static class Logger
    {
        private static string? _logFilePath;
        private static readonly object _lock = new object();

        public static void Init(string logDir)
        {
            try
            {
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }
                _logFilePath = Path.Combine(logDir, "mvn_inspector.log");
                
                // Optional: Clear or rotate log if too large
                if (File.Exists(_logFilePath) && new FileInfo(_logFilePath).Length > 10 * 1024 * 1024) // 10MB
                {
                    File.Move(_logFilePath, _logFilePath + ".old", true);
                }

                Log("Logger initialized.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to initialize logger: {ex.Message}");
            }
        }

        public static void Log(string message, string level = "INFO")
        {
            var logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
            
            // Always write to stderr for MCP console viewing
            Console.Error.WriteLine(logLine);

            if (_logFilePath != null)
            {
                lock (_lock)
                {
                    try
                    {
                        File.AppendAllLines(_logFilePath, new[] { logLine });
                    }
                    catch
                    {
                        // Ignore write errors to avoid crashing
                    }
                }
            }
        }
    }
}
