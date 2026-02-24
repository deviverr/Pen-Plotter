using System;
using System.Diagnostics;
using System.IO;

namespace PlotterControl.Utils
{
    public static class Logger
    {
        private static string _logFilePath;

        static Logger()
        {
            // Initialize log file path in the application's base directory
            _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PlotterControl.log");
            Info("Logger initialized.");
        }

        public static void Info(string message)
        {
            Log(message, "INFO");
        }

        public static void Warning(string message)
        {
            Log(message, "WARN");
        }

        public static void Error(string message, Exception ex = null)
        {
            Log(message, "ERROR", ex);
        }

        private static void Log(string message, string level, Exception ex = null)
        {
            string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
            if (ex != null)
            {
                logEntry += Environment.NewLine + ex.ToString();
            }

            // Output to Debug console
            Debug.WriteLine(logEntry);

            // Output to log file
            try
            {
                File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
            }
            catch (Exception fileEx)
            {
                Debug.WriteLine($"Error writing to log file: {fileEx.Message}");
            }
        }
    }
}
