using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using BrightIdeasSoftware;
using BitParser;

namespace UnifiedConsole {
    /// <summary>
    /// Reusable viewer panel that displays parsed bit data.
    /// Uses ThrottledUIUpdater for efficient, independent UI updates.
    /// Light theme version.
    /// </summary>
    public class ViewerPanel : UserControl {
        private readonly string _transportName;
        private TreeListView _tree;
        private Label _lblStats;
        private Label _lblTitle;
        
        // Own ThrottledUIUpdater for independent updates
        private ThrottledUIUpdater _uiUpdater;
        
        // Data
        private FastBitParser _parser;
        private List<BitNode> _rootNodes = new List<BitNode>();
        private Dictionary<string, BitNode> _nodesByKey = new Dictionary<string, BitNode>();
        
        // Stats
        private long _framesReceived;
        private long _lastSequence;
        private long _droppedFrames;
        private double _avgLatencyMs;
        private int _latencySamples;
        private DateTime _startTime;
        private System.Windows.Forms.Timer _statsTimer;

        public string TransportName => _transportName;
        public long FramesReceived => _framesReceived;
        public long DroppedFrames => _droppedFrames;
        public double AvgLatencyMs => _avgLatencyMs;
        public double FPS => _framesReceived / Math.Max(1, (DateTime.Now - _startTime).TotalSeconds);
        public double ActualUIFps => _uiUpdater?.ActualFps ?? 0;
        public long FramesSkipped => _uiUpdater?.DataFramesSkipped ?? 0;

        public event Action<string> OnLog;

        public ViewerPanel(string transportName) {
            _transportName = transportName;
            InitializeUI();
            InitializeUIUpdater();
        }

        private void InitializeUI() {
            this.BackColor = Color.FromArgb(250, 250, 255);
            this.Padding = new Padding(5);

            // Title
            _lblTitle = new Label {
                Text = $"📊 {_transportName}",
                Dock = DockStyle.Top,
                Height = 28,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = Color.FromArgb(40, 40, 80),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.FromArgb(230, 235, 245),
                Padding = new Padding(10, 0, 0, 0)
            };
            this.Controls.Add(_lblTitle);

            // Stats bar
            _lblStats = new Label {
                Text = "Waiting for data...",
                Dock = DockStyle.Bottom,
                Height = 40,
                Font = new Font("Consolas", 9),
                ForeColor = Color.FromArgb(40, 100, 40),
                BackColor = Color.FromArgb(240, 245, 240),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0)
            };
            this.Controls.Add(_lblStats);

            // Tree view
            _tree = new TreeListView {
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                ShowGroups = false,
                UseAlternatingBackColors = true,
                AlternateRowBackColor = Color.FromArgb(245, 248, 255),
                BackColor = Color.White,
                ForeColor = Color.Black,
                Font = new Font("Segoe UI", 9),
                View = View.Details,
                VirtualMode = true,
                GridLines = true,
                BorderStyle = BorderStyle.FixedSingle
            };

            _tree.Columns.Add(new OLVColumn("Name", "Name") { Width = 180, FillsFreeSpace = true });
            _tree.Columns.Add(new OLVColumn("Value", "DisplayValue") { Width = 100 });
            _tree.Columns.Add(new OLVColumn("Raw", "RawHex") { Width = 80 });
            _tree.Columns.Add(new OLVColumn("Status", "StatusText") { Width = 60 });

            _tree.CanExpandGetter = m => (m as BitNode)?.Children?.Count > 0;
            _tree.ChildrenGetter = m => (m as BitNode)?.Children;

            _tree.FormatRow += (s, e) => {
                var n = e.Model as BitNode;
                if (n == null) return;
                if (n.IsError) {
                    e.Item.BackColor = Color.FromArgb(255, 230, 230);
                    e.Item.ForeColor = Color.DarkRed;
                } else if (n.HasChanged) {
                    e.Item.BackColor = Color.FromArgb(255, 255, 220);
                }
            };

            this.Controls.Add(_tree);
            _tree.BringToFront();

            // Stats display timer (separate from UI updater)
            _statsTimer = new System.Windows.Forms.Timer { Interval = 250 };
            _statsTimer.Tick += (s, e) => UpdateStatsDisplay();
            _statsTimer.Start();

            _startTime = DateTime.Now;
        }

        private void InitializeUIUpdater() {
            // Create own ThrottledUIUpdater at 30 FPS
            _uiUpdater = new ThrottledUIUpdater(this, 30);
            _uiUpdater.OnUIUpdate += OnThrottledUIUpdate;
            _uiUpdater.OnLog += msg => OnLog?.Invoke(msg);
            _uiUpdater.Start();
        }

        public void SetParser(FastBitParser parser, CompiledSchema schema) {
            _parser = parser;
            BuildTree(schema);
        }

        private void BuildTree(CompiledSchema schema) {
            _rootNodes.Clear();
            _nodesByKey.Clear();

            int fieldId = 0;
            foreach (var word in schema.Words) {
                if (!word.IsVisible) continue;

                var wn = new BitNode {
                    Key = $"W{word.Offset}",
                    Name = word.Name,
                    Children = new List<BitNode>()
                };
                _nodesByKey[wn.Key] = wn;
                _rootNodes.Add(wn);

                foreach (var field in word.Fields) {
                    if (field.IsReserved || !field.IsVisible) continue;

                    var fn = new BitNode {
                        Key = $"F{fieldId}",
                        FieldId = fieldId++,
                        Name = field.Name,
                        IsBool = field.BitCount == 1
                    };
                    _nodesByKey[fn.Key] = fn;
                    wn.Children.Add(fn);
                }
            }

            _tree.Roots = _rootNodes;
            _tree.ExpandAll();
        }

        /// <summary>
        /// Called when data is received from the transport (legacy - for backward compatibility).
        /// Submits to ThrottledUIUpdater (non-blocking).
        /// </summary>
        public void OnDataReceived(PipeProtocol.MessageHeader header, byte[] data) {
            _framesReceived++;

            // Track dropped frames
            if (_lastSequence > 0 && header.SequenceNumber > _lastSequence + 1) {
                _droppedFrames += header.SequenceNumber - _lastSequence - 1;
            }
            _lastSequence = header.SequenceNumber;

            // Measure latency
            uint headerTs = header.Timestamp;
            uint nowTs = (uint)Environment.TickCount;
            double latency = (nowTs - headerTs);
            if (latency > 0 && latency < 10000) {
                _avgLatencyMs = (_avgLatencyMs * _latencySamples + latency) / (_latencySamples + 1);
                _latencySamples++;
            }

            // Parse and submit to UI updater (non-blocking)
            if (_parser != null && header.MessageType == PipeProtocol.MSG_DATA_FRAME) {
                var raw = PipeProtocol.DeserializeDataFrame(data, header.ValueCount);
                var result = _parser.Parse(raw, true);
                
                // Submit to throttled updater - will be displayed at next UI tick
                _uiUpdater?.SubmitData(result, header.SequenceNumber);
            }
        }

        /// <summary>
        /// Direct submission of ParseResult (preferred method - zero overhead).
        /// Called directly from DataLoop without any serialization.
        /// Thread-safe and non-blocking.
        /// </summary>
        public void SubmitParsedData(ParseResult result) {
            _framesReceived++;

            // Direct submission to throttled updater (non-blocking)
            _uiUpdater?.SubmitData(result, (uint)_framesReceived);
        }

        /// <summary>
        /// Called by ThrottledUIUpdater at 30 FPS max.
        /// Updates the tree with the latest data.
        /// </summary>
        private void OnThrottledUIUpdate(ParsedValue[] values, int count, int totalErrors, int newErrors) {
            foreach (var n in _nodesByKey.Values) n.HasChanged = false;

            for (int i = 0; i < count; i++) {
                ref ParsedValue v = ref values[i];
                string key = $"F{v.FieldIndex}";
                if (_nodesByKey.TryGetValue(key, out var node)) {
                    node.Value = v.Value;
                    node.RawValue = v.RawValue;
                    node.HasChanged = v.HasChanged;
                    node.Status = v.Status;
                    node.IsError = v.IsError;
                }
            }

            _tree.BuildList(true);
        }

        private void UpdateStatsDisplay() {
            double fps = FPS;
            double uiFps = ActualUIFps;
            long skipped = FramesSkipped;
            
            _lblStats.Text = $"Frames: {_framesReceived:N0} | Data FPS: {fps:F1} | UI FPS: {uiFps:F1}\n" +
                           $"Latency: {_avgLatencyMs:F2}ms | Dropped: {_droppedFrames:N0} | UI Skipped: {skipped:N0}";
            
            if (_droppedFrames > 100) {
                _lblStats.ForeColor = Color.DarkRed;
            } else if (fps > 10) {
                _lblStats.ForeColor = Color.DarkGreen;
            } else {
                _lblStats.ForeColor = Color.DarkOrange;
            }
        }

        public void Reset() {
            _framesReceived = 0;
            _lastSequence = 0;
            _droppedFrames = 0;
            _avgLatencyMs = 0;
            _latencySamples = 0;
            _startTime = DateTime.Now;
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                _statsTimer?.Dispose();
                _uiUpdater?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    public class BitNode {
        public string Key { get; set; }
        public int FieldId { get; set; }
        public string Name { get; set; }
        public float Value { get; set; }
        public uint RawValue { get; set; }
        public bool HasChanged { get; set; }
        public bool IsBool { get; set; }
        public ValidationStatus Status { get; set; }
        public bool IsError { get; set; }
        public List<BitNode> Children { get; set; }

        public string DisplayValue => IsBool ? (RawValue != 0 ? "TRUE" : "FALSE") : $"{Value:F2}";
        public string RawHex => $"0x{RawValue:X}";
        public string StatusText => IsError ? "ERR" : (Status == ValidationStatus.Valid ? "OK" : "-");
    }
}
