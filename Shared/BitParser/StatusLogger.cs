using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace BitParser {
    /// <summary>
    /// Efficient CSV status logger for long-running operation.
    /// Features:
    /// - 1-second interval logging
    /// - Buffered writes (minimal disk I/O)
    /// - Automatic file rotation (daily or by size)
    /// - Memory-efficient (no growing buffers)
    /// - Thread-safe
    /// </summary>
    public sealed class StatusLogger : IDisposable {
        private readonly string _baseDirectory;
        private readonly string _filePrefix;
        private readonly int _maxFileSizeMB;
        private readonly int _logIntervalMs;
        
        private StreamWriter _writer;
        private string _currentFilePath;
        private DateTime _currentFileDate;
        private long _currentFileSize;
        private Timer _logTimer;
        
        // Pre-allocated buffers
        private readonly StringBuilder _lineBuilder;
        private readonly StringBuilder _headerBuilder;
        private readonly object _lock = new object();
        
        // Latest snapshot for logging
        private volatile FieldSnapshot[] _latestSnapshot;
        private volatile int _snapshotCount;
        private volatile bool _hasNewData;
        private string[] _fieldNames;
        private int _totalErrors;
        private int _activeErrors;
        
        // Statistics
        public long TotalLinesWritten { get; private set; }
        public long TotalBytesWritten { get; private set; }
        public string CurrentFilePath => _currentFilePath;
        public bool IsRunning => _logTimer != null;

        public event Action<string> OnLog;

        /// <summary>
        /// Simple field value snapshot for logging.
        /// </summary>
        public struct FieldSnapshot {
            public int FieldId;
            public float Value;
            public byte Status;  // ValidationStatus
            public ushort ErrorCount;
        }

        public StatusLogger(
            string baseDirectory = null, 
            string filePrefix = "status_log",
            int logIntervalMs = 1000,
            int maxFileSizeMB = 100) {
            
            _baseDirectory = baseDirectory ?? Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "logs");
            _filePrefix = filePrefix;
            _logIntervalMs = logIntervalMs;
            _maxFileSizeMB = maxFileSizeMB;
            
            // Pre-allocate with reasonable size
            _lineBuilder = new StringBuilder(4096);
            _headerBuilder = new StringBuilder(4096);
            _latestSnapshot = new FieldSnapshot[1024];
            
            // Ensure directory exists
            Directory.CreateDirectory(_baseDirectory);
        }

        /// <summary>
        /// Initialize with field names from schema.
        /// Call this once after loading schema.
        /// </summary>
        public void Initialize(CompiledSchema schema) {
            var names = new List<string>();
            
            foreach (var word in schema.Words) {
                if (!word.IsVisible) continue;
                
                foreach (var field in word.Fields) {
                    if (field.IsReserved || !field.IsVisible) continue;
                    names.Add($"{word.Name}.{field.Name}");
                }
            }
            
            _fieldNames = names.ToArray();
            
            // Resize snapshot array if needed
            if (_latestSnapshot.Length < _fieldNames.Length) {
                _latestSnapshot = new FieldSnapshot[_fieldNames.Length];
            }
            
            Log($"Logger initialized with {_fieldNames.Length} fields");
        }

        /// <summary>
        /// Start periodic logging.
        /// </summary>
        public void Start() {
            if (_logTimer != null) return;
            if (_fieldNames == null || _fieldNames.Length == 0) {
                Log("ERROR: Initialize() must be called before Start()");
                return;
            }
            
            OpenNewFile();
            
            _logTimer = new Timer(LogTimerCallback, null, _logIntervalMs, _logIntervalMs);
            Log($"Status logging started (interval: {_logIntervalMs}ms)");
        }

        /// <summary>
        /// Stop logging and flush.
        /// </summary>
        public void Stop() {
            _logTimer?.Dispose();
            _logTimer = null;
            
            lock (_lock) {
                _writer?.Flush();
                _writer?.Dispose();
                _writer = null;
            }
            
            Log("Status logging stopped");
        }

        /// <summary>
        /// Update snapshot from parsed values (called on each UI update).
        /// This is very fast - just copies values to snapshot array.
        /// </summary>
        public void UpdateSnapshot(ParsedValue[] values, int count, int totalErrors, int activeErrors) {
            // Fast copy to snapshot
            int copyCount = Math.Min(count, _latestSnapshot.Length);
            for (int i = 0; i < copyCount; i++) {
                _latestSnapshot[i] = new FieldSnapshot {
                    FieldId = values[i].FieldId,
                    Value = values[i].Value,
                    Status = values[i].StatusByte,
                    ErrorCount = values[i].ErrorCount
                };
            }
            _snapshotCount = copyCount;
            _totalErrors = totalErrors;
            _activeErrors = activeErrors;
            _hasNewData = true;
        }

        private void LogTimerCallback(object state) {
            if (!_hasNewData) return;
            _hasNewData = false;
            
            try {
                WriteLogLine();
            } catch (Exception ex) {
                Log($"Log write error: {ex.Message}");
            }
        }

        private void WriteLogLine() {
            lock (_lock) {
                if (_writer == null) return;
                
                // Check for file rotation
                CheckFileRotation();
                
                // Build line
                _lineBuilder.Clear();
                
                // Timestamp
                var now = DateTime.Now;
                _lineBuilder.Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                _lineBuilder.Append(',');
                
                // Total/Active errors
                _lineBuilder.Append(_totalErrors);
                _lineBuilder.Append(',');
                _lineBuilder.Append(_activeErrors);
                
                // Field values and statuses
                int count = Math.Min(_snapshotCount, _fieldNames.Length);
                for (int i = 0; i < count; i++) {
                    _lineBuilder.Append(',');
                    _lineBuilder.Append(_latestSnapshot[i].Value.ToString("F4"));
                    _lineBuilder.Append(',');
                    _lineBuilder.Append(GetStatusString(_latestSnapshot[i].Status));
                }
                
                // Pad remaining fields if snapshot is shorter
                for (int i = count; i < _fieldNames.Length; i++) {
                    _lineBuilder.Append(",,");
                }
                
                string line = _lineBuilder.ToString();
                _writer.WriteLine(line);
                
                _currentFileSize += line.Length + 2;
                TotalLinesWritten++;
                TotalBytesWritten += line.Length + 2;
                
                // Flush periodically (every 10 lines)
                if (TotalLinesWritten % 10 == 0) {
                    _writer.Flush();
                }
            }
        }

        private void CheckFileRotation() {
            bool needRotation = false;
            
            // Rotate daily
            if (DateTime.Now.Date != _currentFileDate.Date) {
                needRotation = true;
            }
            
            // Rotate by size
            if (_currentFileSize > _maxFileSizeMB * 1024L * 1024L) {
                needRotation = true;
            }
            
            if (needRotation) {
                _writer?.Flush();
                _writer?.Dispose();
                OpenNewFile();
            }
        }

        private void OpenNewFile() {
            _currentFileDate = DateTime.Now;
            string timestamp = _currentFileDate.ToString("yyyyMMdd_HHmmss");
            _currentFilePath = Path.Combine(_baseDirectory, $"{_filePrefix}_{timestamp}.csv");
            
            _writer = new StreamWriter(_currentFilePath, false, Encoding.UTF8, 65536);
            _currentFileSize = 0;
            
            // Write header
            WriteHeader();
            
            Log($"Opened log file: {_currentFilePath}");
        }

        private void WriteHeader() {
            _headerBuilder.Clear();
            _headerBuilder.Append("Timestamp,TotalErrors,ActiveErrors");
            
            if (_fieldNames != null) {
                foreach (var name in _fieldNames) {
                    _headerBuilder.Append(',');
                    _headerBuilder.Append(name);
                    _headerBuilder.Append(',');
                    _headerBuilder.Append(name + "_Status");
                }
            }
            
            string header = _headerBuilder.ToString();
            _writer.WriteLine(header);
            _currentFileSize = header.Length + 2;
        }

        private static string GetStatusString(byte status) {
            switch ((ValidationStatus)status) {
                case ValidationStatus.Valid: return "OK";
                case ValidationStatus.OutOfRange: return "RANGE";
                case ValidationStatus.FaultCondition: return "FAULT";
                default: return "-";
            }
        }

        private void Log(string message) {
            OnLog?.Invoke($"[Logger] {message}");
        }

        public void Dispose() {
            Stop();
        }
    }

    /// <summary>
    /// Long-running stability monitor with active memory pressure management.
    /// Features:
    /// - Memory tracking and automatic GC when over threshold
    /// - GC frequency monitoring
    /// - Uptime tracking
    /// - Configurable thresholds
    /// </summary>
    public sealed class StabilityMonitor {
        private readonly Timer _monitorTimer;
        private long _startTicks;
        private long _lastGcCount;
        private int _consecutiveHighMemoryCount;
        
        public long UptimeMinutes => (Environment.TickCount - _startTicks) / 60000;
        public long GCCollections { get; private set; }
        public long PeakMemoryMB { get; private set; }
        public long CurrentMemoryMB { get; private set; }
        
        // Configurable thresholds
        public long MemoryWarningThresholdMB { get; set; } = 400;
        public long MemoryCriticalThresholdMB { get; set; } = 500;
        public int HighMemoryCountBeforeGC { get; set; } = 3;
        
        public event Action<string> OnWarning;
        public event Action<string> OnCritical;
        public event Action<string> OnInfo;

        public StabilityMonitor(int monitorIntervalMs = 60000) {
            _startTicks = Environment.TickCount;
            _lastGcCount = GC.CollectionCount(0);
            
            _monitorTimer = new Timer(MonitorCallback, null, monitorIntervalMs, monitorIntervalMs);
        }

        private void MonitorCallback(object state) {
            // Track GC
            long currentGc = GC.CollectionCount(0);
            long newGcs = currentGc - _lastGcCount;
            GCCollections += newGcs;
            _lastGcCount = currentGc;
            
            // Track memory
            CurrentMemoryMB = GC.GetTotalMemory(false) / (1024 * 1024);
            if (CurrentMemoryMB > PeakMemoryMB) {
                PeakMemoryMB = CurrentMemoryMB;
            }
            
            // Memory pressure management
            if (CurrentMemoryMB > MemoryCriticalThresholdMB) {
                _consecutiveHighMemoryCount++;
                OnCritical?.Invoke($"CRITICAL: Memory at {CurrentMemoryMB} MB (threshold: {MemoryCriticalThresholdMB} MB)");
                
                // Force aggressive GC after consecutive high memory
                if (_consecutiveHighMemoryCount >= HighMemoryCountBeforeGC) {
                    OnInfo?.Invoke("Triggering aggressive garbage collection...");
                    
                    // Force full GC with compaction
                    GC.Collect(2, GCCollectionMode.Forced, true, true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    
                    long afterGcMemory = GC.GetTotalMemory(true) / (1024 * 1024);
                    long freed = CurrentMemoryMB - afterGcMemory;
                    
                    OnInfo?.Invoke($"GC completed. Freed {freed} MB. Current: {afterGcMemory} MB");
                    CurrentMemoryMB = afterGcMemory;
                    _consecutiveHighMemoryCount = 0;
                }
            } else if (CurrentMemoryMB > MemoryWarningThresholdMB) {
                OnWarning?.Invoke($"High memory usage: {CurrentMemoryMB} MB (warning threshold: {MemoryWarningThresholdMB} MB)");
                _consecutiveHighMemoryCount++;
            } else {
                _consecutiveHighMemoryCount = 0;
            }
            
            // Warning if too many GCs (average > 10/min)
            if (UptimeMinutes > 0 && (GCCollections / UptimeMinutes) > 10) {
                OnWarning?.Invoke($"High GC rate: {GCCollections} collections in {UptimeMinutes} minutes (avg {GCCollections / UptimeMinutes}/min)");
            }
        }
        
        /// <summary>
        /// Force immediate memory check and cleanup if needed.
        /// </summary>
        public void CheckMemoryNow() {
            MonitorCallback(null);
        }
        
        /// <summary>
        /// Get detailed status report.
        /// </summary>
        public string GetStatusReport() {
            return $"Uptime: {UptimeMinutes}m | Memory: {CurrentMemoryMB}MB (peak {PeakMemoryMB}MB) | GCs: {GCCollections}";
        }
        
        /// <summary>
        /// Get health status.
        /// </summary>
        public string GetHealthStatus() {
            if (CurrentMemoryMB > MemoryCriticalThresholdMB) {
                return "CRITICAL";
            } else if (CurrentMemoryMB > MemoryWarningThresholdMB) {
                return "WARNING";
            } else {
                return "HEALTHY";
            }
        }

        public void Dispose() {
            _monitorTimer?.Dispose();
        }
    }
}
