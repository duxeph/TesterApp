using System;

namespace BitParser.Logging {
    /// <summary>
    /// Log levels from most to least severe.
    /// </summary>
    public enum LogLevel {
        Critical = 0,  // System is unusable
        Error = 1,     // Error that needs attention
        Warning = 2,   // Something unexpected but handled
        Info = 3,      // General information
        Debug = 4      // Detailed diagnostic information
    }
    
    /// <summary>
    /// Structured logging interface.
    /// </summary>
    public interface ILogger {
        void Log(LogLevel level, string message, Exception exception = null);
        
        void LogDebug(string message);
        void LogInfo(string message);
        void LogWarning(string message, Exception exception = null);
        void LogError(string message, Exception exception);
        void LogCritical(string message, Exception exception);
        
        LogLevel MinimumLevel { get; set; }
    }
    
    /// <summary>
    /// Extension methods for ILogger.
    /// </summary>
    public static class LoggerExtensions {
        public static void LogDebug(this ILogger logger, string message) {
            logger.Log(LogLevel.Debug, message);
        }
        
        public static void LogInfo(this ILogger logger, string message) {
            logger.Log(LogLevel.Info, message);
        }
        
        public static void LogWarning(this ILogger logger, string message, Exception exception = null) {
            logger.Log(LogLevel.Warning, message, exception);
        }
        
        public static void LogError(this ILogger logger, string message, Exception exception) {
            logger.Log(LogLevel.Error, message, exception);
        }
        
        public static void LogCritical(this ILogger logger, string message, Exception exception) {
            logger.Log(LogLevel.Critical, message, exception);
        }
    }
}
