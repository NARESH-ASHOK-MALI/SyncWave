using System;
using System.IO;
using System.Threading;

namespace SyncWave.Utils
{
    /// <summary>
    /// Thread-safe logger that writes timestamped entries
    /// to both a log file and the debug output.
    /// </summary>
    public static class Logger
    {
        private static readonly object _lock = new();
        private static readonly string _logPath;

        static Logger()
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SyncWave", "Logs");
            Directory.CreateDirectory(logDir);
            _logPath = Path.Combine(logDir, $"syncwave_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        }

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);
        public static void Error(string message) => Write("ERROR", message);
        public static void Error(string message, Exception ex) =>
            Write("ERROR", $"{message} | {ex.GetType().Name}: {ex.Message}");

        private static void Write(string level, string message)
        {
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [Thread {Thread.CurrentThread.ManagedThreadId}] {message}";
            lock (_lock)
            {
                try
                {
                    File.AppendAllText(_logPath, entry + Environment.NewLine);
                }
                catch
                {
                    // Silently fail on file write errors to prevent cascading issues
                }
            }
            System.Diagnostics.Debug.WriteLine(entry);
        }
    }
}
