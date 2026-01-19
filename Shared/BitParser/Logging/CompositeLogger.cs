using System;
using System.Collections.Generic;

namespace BitParser.Logging {
    /// <summary>
    /// Composite logger that forwards to multiple loggers.
    /// Thread-safe.
    /// </summary>
    public class CompositeLogger : ILogger {
        private readonly List<ILogger> _loggers = new List<ILogger>();
        private readonly object _lock = new object();
        private LogLevel _minimumLevel = LogLevel.Info;
        
        public LogLevel MinimumLevel {
            get {
                lock (_lock) {
                    return _minimumLevel;
                }
            }
            set {
                lock (_lock) {
                    _minimumLevel = value;
                    foreach (var logger in _loggers) {
                        logger.MinimumLevel = value;
                    }
                }
            }
        }
        
        public void AddLogger(ILogger logger) {
            lock (_lock) {
                if (!_loggers.Contains(logger)) {
                    _loggers.Add(logger);
                    logger.MinimumLevel = _minimumLevel;
                }
            }
        }
        
        public void RemoveLogger(ILogger logger) {
            lock (_lock) {
                _loggers.Remove(logger);
            }
        }
        
        public void Log(LogLevel level, string message, Exception exception = null) {
            if (level > _minimumLevel) return;
            
            List<ILogger> loggersCopy;
            lock (_lock) {
                loggersCopy = new List<ILogger>(_loggers);
            }
            
            foreach (var logger in loggersCopy) {
                try {
                    logger.Log(level, message, exception);
                } catch {
                    // Don't let one logger's failure affect others
                }
            }
        }
        
        public void LogDebug(string message) => Log(LogLevel.Debug, message);
        public void LogInfo(string message) => Log(LogLevel.Info, message);
        public void LogWarning(string message, Exception exception = null) => Log(LogLevel.Warning, message, exception);
        public void LogError(string message, Exception exception) => Log(LogLevel.Error, message, exception);
        public void LogCritical(string message, Exception exception) => Log(LogLevel.Critical, message, exception);
    }
}
