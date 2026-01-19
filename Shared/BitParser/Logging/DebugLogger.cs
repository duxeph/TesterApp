using System;

namespace BitParser.Logging {
    /// <summary>
    /// Debug output logger (writes to Debug.WriteLine).
    /// Useful during development.
    /// </summary>
    public class DebugLogger : ILogger {
        public LogLevel MinimumLevel { get; set; } = LogLevel.Debug;
        
        public void Log(LogLevel level, string message, Exception exception = null) {
            if (level > MinimumLevel) return;
            
            string prefix = GetLevelPrefix(level);
            string logMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {prefix} {message}";
            
            System.Diagnostics.Debug.WriteLine(logMessage);
            
            if (exception != null) {
                System.Diagnostics.Debug.WriteLine($"  Exception: {exception.GetType().Name}: {exception.Message}");
                if (exception.StackTrace != null) {
                    System.Diagnostics.Debug.WriteLine($"  {exception.StackTrace}");
                }
            }
        }
        
        private string GetLevelPrefix(LogLevel level) {
            switch (level) {
                case LogLevel.Critical: return "[CRIT]";
                case LogLevel.Error:    return "[ERROR]";
                case LogLevel.Warning:  return "[WARN]";
                case LogLevel.Info:     return "[INFO]";
                case LogLevel.Debug:    return "[DEBUG]";
                default:                return "[?]";
            }
        }
        
        public void LogDebug(string message) => Log(LogLevel.Debug, message);
        public void LogInfo(string message) => Log(LogLevel.Info, message);
        public void LogWarning(string message, Exception exception = null) => Log(LogLevel.Warning, message, exception);
        public void LogError(string message, Exception exception) => Log(LogLevel.Error, message, exception);
        public void LogCritical(string message, Exception exception) => Log(LogLevel.Critical, message, exception);
    }
}
