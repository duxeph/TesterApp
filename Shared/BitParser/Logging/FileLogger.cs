using System;
using System.IO;
using System.Text;

namespace BitParser.Logging {
    /// <summary>
    /// File logger with automatic rotation and thread-safe writes.
    /// </summary>
    public class FileLogger : ILogger, IDisposable {
        private readonly string _logDirectory;
        private readonly string _logFilePrefix;
        private readonly long _maxFileSizeBytes;
        private readonly object _lock = new object();
        
        private StreamWriter _writer;
        private string _currentFilePath;
        private long _currentFileSize;
        
        public LogLevel MinimumLevel { get; set; } = LogLevel.Info;
        
        public FileLogger(string logDirectory, string logFilePrefix = "app", long maxFileSizeMB = 50) {
            _logDirectory = logDirectory;
            _logFilePrefix = logFilePrefix;
            _maxFileSizeBytes = maxFileSizeMB * 1024 * 1024;
            
            if (!Directory.Exists(_logDirectory)) {
                Directory.CreateDirectory(_logDirectory);
            }
            
            OpenLogFile();
        }
        
        public void Log(LogLevel level, string message, Exception exception = null) {
            if (level > MinimumLevel) return;
            
            lock (_lock) {
                try {
                    // Check file size and rotate if needed
                    if (_currentFileSize > _maxFileSizeBytes) {
                        RotateLogFile();
                    }
                    
                    // Format log entry
                    var sb = new StringBuilder();
                    sb.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] ");
                    sb.Append($"[{level.ToString().ToUpper()}] ");
                    sb.Append(message);
                    
                    if (exception != null) {
                        sb.AppendLine();
                        sb.Append("  Exception: ");
                        sb.Append(exception.GetType().Name);
                        sb.Append(": ");
                        sb.Append(exception.Message);
                        if (exception.StackTrace != null) {
                            sb.AppendLine();
                            sb.Append("  Stack Trace:");
                            sb.AppendLine();
                            sb.Append(exception.StackTrace);
                        }
                    }
                    
                    string logEntry = sb.ToString();
                    _writer.WriteLine(logEntry);
                    _writer.Flush();
                    
                    _currentFileSize += Encoding.UTF8.GetByteCount(logEntry) + 2; // +2 for \r\n
                    
                } catch {
                    // Can't log a logging error - just swallow it
                }
            }
        }
        
        private void OpenLogFile() {
            _currentFilePath = Path.Combine(
                _logDirectory,
                $"{_logFilePrefix}_{DateTime.Now:yyyyMMdd_HHmmss}.log"
            );
            
            _writer = new StreamWriter(_currentFilePath, append: true, Encoding.UTF8) {
                AutoFlush = false
            };
            
            _currentFileSize = new FileInfo(_currentFilePath).Length;
            
            _writer.WriteLine($"=== Log started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            _writer.Flush();
        }
        
        private void RotateLogFile() {
            _writer?.Dispose();
            OpenLogFile();
        }
        
        public void LogDebug(string message) => Log(LogLevel.Debug, message);
        public void LogInfo(string message) => Log(LogLevel.Info, message);
        public void LogWarning(string message, Exception exception = null) => Log(LogLevel.Warning, message, exception);
        public void LogError(string message, Exception exception) => Log(LogLevel.Error, message, exception);
        public void LogCritical(string message, Exception exception) => Log(LogLevel.Critical, message, exception);
        
        public void Dispose() {
            lock (_lock) {
                _writer?.WriteLine($"=== Log ended at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                _writer?.Dispose();
            }
        }
    }
}
