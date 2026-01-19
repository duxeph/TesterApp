using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace UnifiedConsole {
    /// <summary>
    /// Ultra-fast custom grid for port statistics display.
    /// Has its own independent ThrottledPortStatsUpdater for efficient UI updates.
    /// Owner-drawn for minimal overhead.
    /// 4 columns x 16 rows: Port | RX | TX | Errors
    /// </summary>
    public sealed class PortStatsGrid : Control {
        // Data
        private readonly PortRowData[] _rows;
        private const int PORT_COUNT = 16;

        // Double buffer for port stats
        private PerfProtocol.PortStats[] _buffer1;
        private PerfProtocol.PortStats[] _buffer2;
        private volatile int _activeBuffer;
        private volatile bool _hasNewData;

        // Own UI timer for independent updates
        private Timer _uiTimer;
        private readonly int _targetFps = 30;

        // Stats
        public long UIUpdatesPerformed { get; private set; }
        public long DataFramesSkipped { get; private set; }
        public double ActualFps { get; private set; }
        private DateTime _lastFpsTime = DateTime.Now;
        private int _fpsCounter;

        // Layout
        private const int HEADER_HEIGHT = 28;
        private const int ROW_HEIGHT = 24;
        private readonly int[] _colWidths = { 80, 100, 100, 100 };
        private readonly string[] _colHeaders = { "Port", "RX Count", "TX Count", "Errors" };

        // Colors
        private readonly Color _headerBg = Color.FromArgb(40, 40, 60);
        private readonly Color _headerFg = Color.White;
        private readonly Color _rowBgEven = Color.FromArgb(250, 250, 255);
        private readonly Color _rowBgOdd = Color.White;
        private readonly Color _errorBg = Color.FromArgb(255, 200, 200);
        private readonly Color _okColor = Color.DarkGreen;
        private readonly Color _errorColor = Color.DarkRed;
        private readonly Color _gridLine = Color.FromArgb(200, 200, 210);

        // Fonts (cached)
        private Font _headerFont;
        private Font _dataFont;
        private Font _portFont;

        // Brushes (cached)
        private SolidBrush _headerBrush;
        private SolidBrush _textBrush;
        private Pen _gridPen;

        public struct PortRowData {
            public int PortNumber;
            public uint RxCount;
            public uint TxCount;
            public uint ErrorCount;
            public bool HasError;
            public bool IsActive;
        }

        public PortStatsGrid() {
            SetStyle(ControlStyles.AllPaintingInWmPaint | 
                     ControlStyles.UserPaint | 
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);

            _rows = new PortRowData[PORT_COUNT];
            for (int i = 0; i < PORT_COUNT; i++) {
                _rows[i] = new PortRowData { PortNumber = i + 1, IsActive = true };
            }

            // Initialize double buffers
            _buffer1 = new PerfProtocol.PortStats[PORT_COUNT];
            _buffer2 = new PerfProtocol.PortStats[PORT_COUNT];

            InitializeGraphics();
            InitializeUITimer();
        }

        private void InitializeGraphics() {
            _headerFont = new Font("Segoe UI Semibold", 10);
            _dataFont = new Font("Consolas", 10);
            _portFont = new Font("Segoe UI Semibold", 10);

            _headerBrush = new SolidBrush(_headerFg);
            _textBrush = new SolidBrush(Color.Black);
            _gridPen = new Pen(_gridLine, 1);
        }

        private void InitializeUITimer() {
            _uiTimer = new Timer { Interval = 1000 / _targetFps };
            _uiTimer.Tick += UITimer_Tick;
            _uiTimer.Start();
        }

        /// <summary>
        /// Submit port stats data (non-blocking, double-buffered).
        /// Can be called from any thread at any rate.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SubmitData(PerfProtocol.PortStats[] stats) {
            if (stats == null) return;

            // Write to inactive buffer
            PerfProtocol.PortStats[] target = _activeBuffer == 0 ? _buffer2 : _buffer1;
            int count = Math.Min(stats.Length, PORT_COUNT);
            Array.Copy(stats, 0, target, 0, count);

            // Atomic swap
            _activeBuffer = _activeBuffer == 0 ? 1 : 0;
            if (_activeBuffer == 0) _buffer1 = target; else _buffer2 = target;

            if (_hasNewData) DataFramesSkipped++;
            _hasNewData = true;
        }

        private void UITimer_Tick(object sender, EventArgs e) {
            if (!_hasNewData) return;
            _hasNewData = false;

            // Get current active buffer
            PerfProtocol.PortStats[] source = _activeBuffer == 0 ? _buffer1 : _buffer2;

            // Update rows from buffer
            for (int i = 0; i < PORT_COUNT && i < source.Length; i++) {
                _rows[i].RxCount = source[i].RxCount;
                _rows[i].TxCount = source[i].TxCount;
                _rows[i].ErrorCount = source[i].ErrorCount;
                _rows[i].HasError = source[i].ErrorCount > 0 || 
                    (source[i].TxCount > 0 && source[i].RxCount != source[i].TxCount);
                _rows[i].IsActive = source[i].Status != 2;
            }

            // Trigger repaint
            Invalidate();
            UIUpdatesPerformed++;
            _fpsCounter++;

            // FPS calculation
            var now = DateTime.Now;
            double elapsed = (now - _lastFpsTime).TotalSeconds;
            if (elapsed >= 1.0) {
                ActualFps = _fpsCounter / elapsed;
                _fpsCounter = 0;
                _lastFpsTime = now;
            }
        }

        /// <summary>
        /// Update a single port's data directly (for backward compatibility).
        /// </summary>
        public void UpdatePort(int portIndex, uint rx, uint tx, uint errors) {
            if (portIndex < 0 || portIndex >= PORT_COUNT) return;
            _rows[portIndex].RxCount = rx;
            _rows[portIndex].TxCount = tx;
            _rows[portIndex].ErrorCount = errors;
            _rows[portIndex].HasError = errors > 0 || (tx > 0 && rx != tx);
        }

        /// <summary>
        /// Bulk update all ports at once (legacy method for backward compatibility).
        /// Now deprecated - use SubmitData instead.
        /// </summary>
        public void UpdateAllPorts(PerfProtocol.PortStats[] stats) {
            SubmitData(stats);
        }

        /// <summary>
        /// Trigger repaint manually (legacy method for backward compatibility).
        /// Now handled automatically by UI timer.
        /// </summary>
        public void RefreshDisplay() {
            // No-op - handled by timer now
        }

        protected override void OnPaint(PaintEventArgs e) {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighSpeed;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int y = 0;

            // Draw header
            using (var headerBgBrush = new SolidBrush(_headerBg)) {
                g.FillRectangle(headerBgBrush, 0, 0, Width, HEADER_HEIGHT);
            }

            int x = 0;
            for (int col = 0; col < _colHeaders.Length; col++) {
                g.DrawString(_colHeaders[col], _headerFont, _headerBrush, x + 5, 5);
                g.DrawLine(_gridPen, x + _colWidths[col], 0, x + _colWidths[col], Height);
                x += _colWidths[col];
            }

            y = HEADER_HEIGHT;

            // Draw rows
            for (int row = 0; row < PORT_COUNT; row++) {
                ref PortRowData data = ref _rows[row];
                x = 0;

                // Row background
                Color rowBg = data.HasError ? _errorBg : (row % 2 == 0 ? _rowBgEven : _rowBgOdd);
                using (var brush = new SolidBrush(rowBg)) {
                    g.FillRectangle(brush, 0, y, Width, ROW_HEIGHT);
                }

                // Grid line
                g.DrawLine(_gridPen, 0, y + ROW_HEIGHT, Width, y + ROW_HEIGHT);

                // Port column
                string portText = $"Port {data.PortNumber}";
                Color portColor = data.PortNumber <= 8 ? Color.DarkBlue : Color.DarkGreen;
                using (var brush = new SolidBrush(portColor)) {
                    g.DrawString(portText, _portFont, brush, x + 5, y + 3);
                }
                g.DrawLine(_gridPen, x + _colWidths[0], y, x + _colWidths[0], y + ROW_HEIGHT);
                x += _colWidths[0];

                // RX column
                DrawValue(g, data.RxCount.ToString("N0"), x, y, _colWidths[1], Color.Black);
                g.DrawLine(_gridPen, x + _colWidths[1], y, x + _colWidths[1], y + ROW_HEIGHT);
                x += _colWidths[1];

                // TX column
                DrawValue(g, data.TxCount.ToString("N0"), x, y, _colWidths[2], Color.Black);
                g.DrawLine(_gridPen, x + _colWidths[2], y, x + _colWidths[2], y + ROW_HEIGHT);
                x += _colWidths[2];

                // Error column
                Color errColor = data.ErrorCount > 0 ? _errorColor : _okColor;
                string errText = data.ErrorCount > 0 ? data.ErrorCount.ToString("N0") : "0";
                DrawValue(g, errText, x, y, _colWidths[3], errColor, data.ErrorCount > 0);
                g.DrawLine(_gridPen, x + _colWidths[3], y, x + _colWidths[3], y + ROW_HEIGHT);

                y += ROW_HEIGHT;
            }
        }

        private void DrawValue(Graphics g, string text, int x, int y, int width, Color color, bool bold = false) {
            using (var brush = new SolidBrush(color)) {
                var font = bold ? new Font(_dataFont, FontStyle.Bold) : _dataFont;
                g.DrawString(text, font, brush, x + 5, y + 3);
                if (bold) font.Dispose();
            }
        }

        protected override void OnResize(EventArgs e) {
            base.OnResize(e);
            // Adjust column widths proportionally
            int total = Width - 80;  // Port column fixed
            _colWidths[1] = total / 3;
            _colWidths[2] = total / 3;
            _colWidths[3] = total / 3;
        }

        public int GetPreferredHeight() {
            return HEADER_HEIGHT + (PORT_COUNT * ROW_HEIGHT) + 4;
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                _uiTimer?.Stop();
                _uiTimer?.Dispose();
                _headerFont?.Dispose();
                _dataFont?.Dispose();
                _portFont?.Dispose();
                _headerBrush?.Dispose();
                _textBrush?.Dispose();
                _gridPen?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
