using System;
using System.Windows.Forms;
using System.Runtime.CompilerServices;

namespace BitParser {
    /// <summary>
    /// Optimized UI updater with minimal allocations.
    /// Uses double buffering and atomic swap.
    /// </summary>
    public sealed class ThrottledUIUpdater : IDisposable {
        private readonly Control _targetControl;
        private readonly int _targetFps;
        private readonly Timer _uiTimer;

        // Double buffer for values
        private ParsedValue[] _buffer1;
        private ParsedValue[] _buffer2;
        private volatile int _activeBuffer;  // 0 or 1
        private volatile int _activeCount;
        private volatile int _activeTotalErrors;
        private volatile int _activeNewErrors;
        private volatile bool _hasNewData;

        // Statistics
        public long UIUpdatesPerformed { get; private set; }
        public long DataFramesSkipped { get; private set; }
        public double ActualFps { get; private set; }
        public int LastTotalErrors { get; private set; }
        public int LastNewErrors { get; private set; }

        private DateTime _lastFpsTime = DateTime.Now;
        private int _fpsCounter;

        public event Action<ParsedValue[], int, int, int> OnUIUpdate;
        public event Action<string> OnLog;

        public ThrottledUIUpdater(Control targetControl, int targetFps = 30) {
            _targetControl = targetControl ?? throw new ArgumentNullException(nameof(targetControl));
            _targetFps = Math.Max(1, Math.Min(targetFps, 60));

            // Pre-allocate double buffers
            _buffer1 = new ParsedValue[2048];
            _buffer2 = new ParsedValue[2048];

            _uiTimer = new Timer { Interval = 1000 / _targetFps };
            _uiTimer.Tick += UITimer_Tick;
        }

        public void Start() {
            _uiTimer.Start();
            Log($"UI updater started at {_targetFps} FPS");
        }

        public void Stop() {
            _uiTimer.Stop();
        }

        /// <summary>
        /// Submit data from ParseResult (zero allocation path).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SubmitData(ParseResult parseResult, uint sequenceNumber) {
            if (parseResult == null || parseResult.Count == 0) return;
            SubmitDataInternal(parseResult.Values, parseResult.Count, 
                               parseResult.TotalErrors, parseResult.NewErrors);
        }

        /// <summary>
        /// Submit from pipe values (for backward compatibility).
        /// </summary>
        public void SubmitData(PipeProtocol.ValueEntry[] values, uint sequenceNumber,
                               int totalErrors = 0, int newErrors = 0) {
            if (values == null || values.Length == 0) return;

            // Get inactive buffer
            ParsedValue[] target = _activeBuffer == 0 ? _buffer2 : _buffer1;
            EnsureCapacity(ref target, values.Length);

            // Convert
            for (int i = 0; i < values.Length; i++) {
                target[i] = PipeProtocol.ToParseValue(values[i]);
            }

            // Swap
            _activeBuffer = _activeBuffer == 0 ? 1 : 0;
            if (_activeBuffer == 0) _buffer1 = target; else _buffer2 = target;
            
            if (_hasNewData) DataFramesSkipped++;
            _activeCount = values.Length;
            _activeTotalErrors = totalErrors;
            _activeNewErrors = newErrors;
            _hasNewData = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SubmitDataInternal(ParsedValue[] values, int count, int totalErrors, int newErrors) {
            // Get inactive buffer
            ParsedValue[] target = _activeBuffer == 0 ? _buffer2 : _buffer1;
            EnsureCapacity(ref target, count);

            // Fast copy
            Array.Copy(values, 0, target, 0, count);

            // Atomic swap
            _activeBuffer = _activeBuffer == 0 ? 1 : 0;
            if (_activeBuffer == 0) _buffer1 = target; else _buffer2 = target;

            if (_hasNewData) DataFramesSkipped++;
            _activeCount = count;
            _activeTotalErrors = totalErrors;
            _activeNewErrors = newErrors;
            _hasNewData = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureCapacity(ref ParsedValue[] buffer, int required) {
            if (buffer.Length < required) {
                buffer = new ParsedValue[required * 2];
            }
        }

        private void UITimer_Tick(object sender, EventArgs e) {
            if (!_hasNewData) return;
            _hasNewData = false;

            // Get current active buffer
            ParsedValue[] source = _activeBuffer == 0 ? _buffer1 : _buffer2;
            int count = _activeCount;
            int totalErrors = _activeTotalErrors;
            int newErrors = _activeNewErrors;

            LastTotalErrors = totalErrors;
            LastNewErrors = newErrors;

            try {
                OnUIUpdate?.Invoke(source, count, totalErrors, newErrors);
                UIUpdatesPerformed++;
                _fpsCounter++;
            } catch (Exception ex) {
                Log($"UI error: {ex.Message}");
            }

            // FPS calculation
            var now = DateTime.Now;
            double elapsed = (now - _lastFpsTime).TotalSeconds;
            if (elapsed >= 1.0) {
                ActualFps = _fpsCounter / elapsed;
                _fpsCounter = 0;
                _lastFpsTime = now;
            }
        }

        private void Log(string message) => OnLog?.Invoke($"[UI] {message}");

        public void Dispose() {
            Stop();
            _uiTimer?.Dispose();
        }
    }

    /// <summary>
    /// Simple rate limiter.
    /// </summary>
    public sealed class DataRateLimiter {
        private readonly int _minIntervalMs;
        private int _lastTick;
        private long _droppedCount;

        public long DroppedFrames => _droppedCount;

        public DataRateLimiter(int maxPerSecond = 30) {
            _minIntervalMs = 1000 / Math.Max(1, maxPerSecond);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ShouldProcess() {
            int now = Environment.TickCount;
            if (now - _lastTick >= _minIntervalMs) {
                _lastTick = now;
                return true;
            }
            _droppedCount++;
            return false;
        }

        public void Reset() {
            _lastTick = 0;
            _droppedCount = 0;
        }
    }
}
