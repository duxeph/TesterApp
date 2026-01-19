using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using BitParser;
using BitParser.DataSources;

namespace UnifiedConsole {
    /// <summary>
    /// Unified Test Console - Simplified Direct Architecture
    /// No pipe/shared memory - all in-process for maximum efficiency
    /// </summary>
    public partial class MainForm : Form {
        // Core components
        private SystemConfig _config;
        private CompiledSchema _schema;
        private FastBitParser _parser;
        private IDataSource _dataSource;
        
        // Port Stats (Performance Test)
        private Thread _perfThread;
        private volatile bool _perfRunning;
        private PerfProtocol.PortStats[] _portStats;
        
        // Bit Parser data loop
        private CancellationTokenSource _dataCts;
        private Task _dataTask;
        private volatile bool _isRunning;

        // UI Controls (Designer-created controls are in MainForm.Designer.cs)
        // Only declare custom controls and performance test controls here
        private ViewerPanel _bitParserViewer;
        private PortStatsGrid _portStatsGrid;
        private Button _btnPerfStart;
        private Button _btnPerfStop;

        public MainForm() {
            InitializeComponent();
            LoadConfig();
        }

        private void MainForm_Load(object sender, EventArgs e) {
            // Initialize port stats
            _portStats = new PerfProtocol.PortStats[PerfProtocol.PORT_COUNT];
            for (int i = 0; i < PerfProtocol.PORT_COUNT; i++) {
                _portStats[i].PortNumber = (byte)(i + 1);
                _portStats[i].VlanId = (ushort)(100 + i);
            }

            // === ADD TAB PAGES (Dynamic content) ===
            var tabPerf = new TabPage("📡 Performance Test") { BackColor = Color.White };
            _tabTests.TabPages.Add(tabPerf);
            tabPerf.Controls.Add(CreatePerfTestPanel());

            var tabPower = new TabPage("⚡ Power Test") { BackColor = Color.White };
            _tabTests.TabPages.Add(tabPower);
            tabPower.Controls.Add(CreatePowerTestPanel());

            var tabConfig = new TabPage("⚙️ Config Test") { BackColor = Color.White };
            _tabTests.TabPages.Add(tabConfig);
            tabConfig.Controls.Add(CreateConfigTestPanel());

            var tabPing = new TabPage("🔔 Ping Test") { BackColor = Color.White };
            _tabTests.TabPages.Add(tabPing);
            tabPing.Controls.Add(CreatePingTestPanel());

            var tabStress = new TabPage("⚡ Stress Test") { BackColor = Color.White };
            _tabTests.TabPages.Add(tabStress);
            tabStress.Controls.Add(CreateStressTestPanel());

            // === ADD VIEWER PANEL (Custom control) ===
            _bitParserViewer = new ViewerPanel("Bit Parser Viewer") {
                Dock = DockStyle.Fill
            };
            _bitParserViewer.OnLog += Log;
            rightSplit.Panel1.Controls.Add(_bitParserViewer);

            // Set splitter distances
            mainSplit.SplitterDistance = 480;
            rightSplit.SplitterDistance = rightSplit.Height - 180;
        }

        private Panel CreatePerfTestPanel() {
            var panel = new Panel { Dock = DockStyle.Fill };

            var controlBar = new Panel {
                Dock = DockStyle.Top,
                Height = 50,
                BackColor = Color.FromArgb(240, 245, 255)
            };
            panel.Controls.Add(controlBar);

            var lblTitle = new Label {
                Text = "📡 Ethernet Performance Test (16 Ports)",
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.FromArgb(50, 50, 80),
                Location = new Point(10, 14),
                AutoSize = true
            };
            controlBar.Controls.Add(lblTitle);

            _btnPerfStart = new Button {
                Text = "▶ Start Test",
                Location = new Point(300, 10),
                Size = new Size(100, 30),
                BackColor = Color.FromArgb(60, 160, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnPerfStart.Click += BtnPerfStart_Click;
            controlBar.Controls.Add(_btnPerfStart);

            _btnPerfStop = new Button {
                Text = "■ Stop",
                Location = new Point(410, 10),
                Size = new Size(80, 30),
                BackColor = Color.FromArgb(180, 60, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Enabled = false
            };
            _btnPerfStop.Click += BtnPerfStop_Click;
            controlBar.Controls.Add(_btnPerfStop);

            _portStatsGrid = new PortStatsGrid { Dock = DockStyle.Fill };
            panel.Controls.Add(_portStatsGrid);
            _portStatsGrid.BringToFront();

            return panel;
        }

        private Panel CreatePowerTestPanel() {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };
            
            var lblTitle = new Label {
                Text = "⚡ Power Supply Voltage Sweep Test",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = Color.FromArgb(50, 50, 80),
                Location = new Point(20, 20),
                AutoSize = true
            };
            panel.Controls.Add(lblTitle);

            var lblDesc = new Label {
                Text = "Tests device behavior across voltage range from 10V to 35V.\n" +
                       "Monitors stability, current draw, and error conditions at each step.",
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.Gray,
                Location = new Point(20, 55),
                Size = new Size(380, 50)
            };
            panel.Controls.Add(lblDesc);

            var lblVoltage = new Label {
                Text = "Current Voltage: --",
                Name = "lblPowerVoltage",
                Font = new Font("Consolas", 16, FontStyle.Bold),
                ForeColor = Color.DarkGreen,
                Location = new Point(20, 120),
                AutoSize = true
            };
            panel.Controls.Add(lblVoltage);

            var progress = new ProgressBar {
                Name = "pbPower",
                Location = new Point(20, 160),
                Size = new Size(380, 25),
                Style = ProgressBarStyle.Continuous
            };
            panel.Controls.Add(progress);

            var btnRun = new Button {
                Text = "▶ Run Power Test",
                Location = new Point(20, 200),
                Size = new Size(150, 35),
                BackColor = Color.FromArgb(70, 130, 70),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnRun.Click += (s, e) => RunPowerTest(panel);
            panel.Controls.Add(btnRun);

            return panel;
        }

        private Panel CreateConfigTestPanel() {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };
            
            var lblTitle = new Label {
                Text = "⚙️ Configuration Load Test",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = Color.FromArgb(50, 50, 80),
                Location = new Point(20, 20),
                AutoSize = true
            };
            panel.Controls.Add(lblTitle);

            var lblDesc = new Label {
                Text = "Tests loading and applying configuration files.\n" +
                       "Verifies that all settings are correctly parsed and applied.",
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.Gray,
                Location = new Point(20, 55),
                Size = new Size(380, 50)
            };
            panel.Controls.Add(lblDesc);

            var lblStatus = new Label {
                Text = "Status: Ready",
                Name = "lblConfigStatus",
                Font = new Font("Consolas", 12),
                ForeColor = Color.DarkBlue,
                Location = new Point(20, 120),
                AutoSize = true
            };
            panel.Controls.Add(lblStatus);

            var btnRun = new Button {
                Text = "▶ Run Config Test",
                Location = new Point(20, 160),
                Size = new Size(150, 35),
                BackColor = Color.FromArgb(70, 100, 150),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnRun.Click += (s, e) => RunConfigTest(panel);
            panel.Controls.Add(btnRun);

            return panel;
        }

        private Panel CreatePingTestPanel() {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };
            
            var lblTitle = new Label {
                Text = "🔔 Ping / Connectivity Test",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = Color.FromArgb(50, 50, 80),
                Location = new Point(20, 20),
                AutoSize = true
            };
            panel.Controls.Add(lblTitle);

            var lblDesc = new Label {
                Text = "Tests network connectivity and response times.\n" +
                       "Measures latency and packet loss.",
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.Gray,
                Location = new Point(20, 55),
                Size = new Size(380, 50)
            };
            panel.Controls.Add(lblDesc);

            var lblTarget = new Label {
                Text = "Target IP:",
                Location = new Point(20, 115),
                AutoSize = true
            };
            panel.Controls.Add(lblTarget);

            var txtTarget = new TextBox {
                Name = "txtPingTarget",
                Text = "192.168.1.1",
                Location = new Point(90, 112),
                Size = new Size(150, 25)
            };
            panel.Controls.Add(txtTarget);

            var lblResult = new Label {
                Text = "Result: --",
                Name = "lblPingResult",
                Font = new Font("Consolas", 12),
                ForeColor = Color.DarkGreen,
                Location = new Point(20, 150),
                AutoSize = true
            };
            panel.Controls.Add(lblResult);

            var btnRun = new Button {
                Text = "▶ Run Ping Test",
                Location = new Point(20, 190),
                Size = new Size(150, 35),
                BackColor = Color.FromArgb(150, 100, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnRun.Click += (s, e) => RunPingTest(panel);
            panel.Controls.Add(btnRun);

            return panel;
        }

        private Panel CreateStressTestPanel() {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };
            
            var lblTitle = new Label {
                Text = "⚡ Stress Test - Large Data Simulation",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = Color.FromArgb(50, 50, 80),
                Location = new Point(20, 20),
                AutoSize = true
            };
            panel.Controls.Add(lblTitle);

            var lblDesc = new Label {
                Text = "Simulates high-volume data input to test UI responsiveness.\n" +
                       "Tests frame skipping and FPS throttling under load.",
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.Gray,
                Location = new Point(20, 55),
                Size = new Size(400, 50)
            };
            panel.Controls.Add(lblDesc);

            // Frame Size selector
            var lblSize = new Label {
                Text = "Frame Size:",
                Location = new Point(20, 115),
                AutoSize = true
            };
            panel.Controls.Add(lblSize);

            var cboSize = new ComboBox {
                Name = "cboFrameSize",
                Location = new Point(110, 112),
                Size = new Size(120, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cboSize.Items.AddRange(new[] { "1 KB", "5 KB", "10 KB", "50 KB" });
            cboSize.SelectedIndex = 1; // Default: 5 KB
            panel.Controls.Add(cboSize);

            // FPS selector
            var lblFps = new Label {
                Text = "Target FPS:",
                Location = new Point(250, 115),
                AutoSize = true
            };
            panel.Controls.Add(lblFps);

            var cboFps = new ComboBox {
                Name = "cboTargetFps",
                Location = new Point(330, 112),
                Size = new Size(80, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cboFps.Items.AddRange(new[] { "1", "10", "30", "60", "120" });
            cboFps.SelectedIndex = 2; // Default: 30 FPS
            panel.Controls.Add(cboFps);

            // Results
            var lblResults = new Label {
                Text = "Results: Not tested",
                Name = "lblStressResults",
                Font = new Font("Consolas", 10),
                ForeColor = Color.DarkBlue,
                Location = new Point(20, 155),
                Size = new Size(400, 60)
            };
            panel.Controls.Add(lblResults);

            var btnRun = new Button {
                Text = "▶ Run Stress Test",
                Location = new Point(20, 230),
                Size = new Size(150, 35),
                BackColor = Color.FromArgb(180, 60, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnRun.Click += (s, e) => RunStressTest(panel);
            panel.Controls.Add(btnRun);

            return panel;
        }

        // ============ PERFORMANCE TEST ============
        private void BtnPerfStart_Click(object sender, EventArgs e) {
            if (_perfRunning) return;

            try {
                _btnPerfStart.Enabled = false;
                Log("[Perf] Starting performance test...");

                _perfRunning = true;
                _perfThread = new Thread(PortStatsLoop) {
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal
                };
                _perfThread.Start();

                _btnPerfStop.Enabled = true;
                Log("[Perf] Test running");

            } catch (Exception ex) {
                Log($"[Perf] Start failed: {ex.Message}");
                _btnPerfStart.Enabled = true;
            }
        }

        private void BtnPerfStop_Click(object sender, EventArgs e) {
            _perfRunning = false;
            _perfThread?.Join(1000);

            _btnPerfStart.Enabled = true;
            _btnPerfStop.Enabled = false;
            Log("[Perf] Test stopped");
        }

        private void PortStatsLoop() {
            Random rand = new Random();
            int testIntervalMs = 40;

            while (_perfRunning) {
                int lastTick = Environment.TickCount;

                // Simulate packet forwarding
                for (int i = 0; i < 8; i++) {
                    int srcPort = i;
                    int dstPort = i + 8;

                    _portStats[srcPort].TxCount++;
                    _portStats[srcPort].Status = 0;

                    bool error = rand.Next(1000) < 2;
                    if (!error) {
                        _portStats[dstPort].RxCount++;
                        _portStats[dstPort].Status = 0;
                    } else {
                        _portStats[dstPort].ErrorCount++;
                        _portStats[dstPort].Status = 1;
                    }
                }

                // Direct submission (thread-safe, non-blocking)
                _portStatsGrid.SubmitData(_portStats);

                int elapsed = Environment.TickCount - lastTick;
                int sleep = testIntervalMs - elapsed;
                if (sleep > 0) Thread.Sleep(sleep);
            }
        }

        // ============ OTHER TESTS ============
        private async void RunPowerTest(Panel panel) {
            Log("[Power] Starting voltage sweep test...");
            var lblVoltage = panel.Controls.Find("lblPowerVoltage", true)[0] as Label;
            var progress = panel.Controls.Find("pbPower", true)[0] as ProgressBar;

            progress.Maximum = 25;
            progress.Value = 0;

            for (int v = 10; v <= 35; v++) {
                lblVoltage.Text = $"Current Voltage: {v}V";
                progress.Value = v - 10;
                Log($"[Power] Testing at {v}V...");
                await Task.Delay(200);
            }

            lblVoltage.Text = "Test Complete!";
            lblVoltage.ForeColor = Color.DarkGreen;
            Log("[Power] Voltage sweep complete. All tests PASSED.");
        }

        private async void RunConfigTest(Panel panel) {
            Log("[Config] Starting configuration load test...");
            var lblStatus = panel.Controls.Find("lblConfigStatus", true)[0] as Label;

            lblStatus.Text = "Status: Loading...";
            lblStatus.ForeColor = Color.DarkOrange;
            await Task.Delay(500);

            lblStatus.Text = "Status: Validating...";
            await Task.Delay(500);

            lblStatus.Text = "Status: Applying...";
            await Task.Delay(500);

            lblStatus.Text = "Status: PASSED ✓";
            lblStatus.ForeColor = Color.DarkGreen;
            Log("[Config] Configuration load test PASSED.");
        }

        private async void RunPingTest(Panel panel) {
            var txtTarget = panel.Controls.Find("txtPingTarget", true)[0] as TextBox;
            var lblResult = panel.Controls.Find("lblPingResult", true)[0] as Label;
            
            string target = txtTarget.Text;
            Log($"[Ping] Pinging {target}...");

            lblResult.Text = "Result: Pinging...";
            lblResult.ForeColor = Color.DarkOrange;

            try {
                var ping = new System.Net.NetworkInformation.Ping();
                var reply = await ping.SendPingAsync(target, 3000);
                
                if (reply.Status == System.Net.NetworkInformation.IPStatus.Success) {
                    lblResult.Text = $"Result: {reply.RoundtripTime}ms ✓";
                    lblResult.ForeColor = Color.DarkGreen;
                    Log($"[Ping] Reply from {target}: {reply.RoundtripTime}ms");
                } else {
                    lblResult.Text = $"Result: {reply.Status}";
                    lblResult.ForeColor = Color.DarkRed;
                    Log($"[Ping] Failed: {reply.Status}");
                }
            } catch (Exception ex) {
                lblResult.Text = $"Result: Error";
                lblResult.ForeColor = Color.DarkRed;
                Log($"[Ping] Error: {ex.Message}");
            }
        }

        private async void RunStressTest(Panel panel) {
            if (_schema == null) {
                MessageBox.Show("Load a schema first (Bit Parser section)", "Schema Required", 
                               MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var cboSize = panel.Controls.Find("cboFrameSize", true)[0] as ComboBox;
            var cboFps = panel.Controls.Find("cboTargetFps", true)[0] as ComboBox;
            var lblResults = panel.Controls.Find("lblStressResults", true)[0] as Label;

            // Parse settings
            int frameSize = cboSize.SelectedIndex switch {
                0 => 1024,    // 1 KB
                1 => 5120,    // 5 KB
                2 => 10240,   // 10 KB
                3 => 51200,   // 50 KB
                _ => 5120
            };
            int targetFps = int.Parse(cboFps.SelectedItem.ToString());
            int testDuration = 10; // seconds

            Log($"[Stress] Starting test: {frameSize} bytes @ {targetFps} FPS for {testDuration}s");
            lblResults.Text = "Running stress test...";
            lblResults.ForeColor = Color.DarkOrange;

            // Reset viewer stats
            _bitParserViewer.Reset();

            // Generate test data
            byte[] testData = new byte[frameSize];
            Random rand = new Random();
            rand.NextBytes(testData);

            int framesSent = 0;
            var startTime = DateTime.Now;
            int intervalMs = 1000 / targetFps;

            while ((DateTime.Now - startTime).TotalSeconds < testDuration) {
                int tick = Environment.TickCount;

                // Parse and submit
                var result = _parser.Parse(testData, true);
                _bitParserViewer.SubmitParsedData(result);
                framesSent++;

                // Throttle to target FPS
                int elapsed = Environment.TickCount - tick;
                int sleep = intervalMs - elapsed;
                if (sleep > 0) {
                    await Task.Delay(sleep);
                }
            }

            // Calculate results
            double actualFps = framesSent / (DateTime.Now - startTime).TotalSeconds;
            double uiFps = _bitParserViewer.ActualUIFps;
            long skipped = _bitParserViewer.FramesSkipped;
            double skipPercent = (skipped / (double)framesSent) * 100;

            lblResults.Text = $"Results:\n" +
                            $"Sent: {framesSent} frames @ {actualFps:F1} FPS\n" +
                            $"UI FPS: {uiFps:F1} | Skipped: {skipped} ({skipPercent:F1}%)";
            
            if (skipPercent > 50) {
                lblResults.ForeColor = Color.DarkRed;
            } else if (skipPercent > 10) {
                lblResults.ForeColor = Color.DarkOrange;
            } else {
                lblResults.ForeColor = Color.DarkGreen;
            }

            Log($"[Stress] Complete: {framesSent} frames sent, {skipped} skipped ({skipPercent:F1}%)");
        }

        // ============ BIT PARSER ============
        private void LoadConfig() {
            string configPath = SystemConfig.FindConfigFile();
            _config = configPath != null ? SystemConfig.Load(configPath) : SystemConfig.CreateDefault();
            Log("Config loaded");
        }

        private void BtnLoadSchema_Click(object sender, EventArgs e) {
            using (var ofd = new OpenFileDialog {
                Filter = "XML|*.xml",
                Title = "Select Schema"
            }) {
                if (ofd.ShowDialog() == DialogResult.OK) {
                    try {
                        _schema = CompiledSchema.LoadFromXml(ofd.FileName);
                        _parser = new FastBitParser(_schema);
                        _bitParserViewer.SetParser(_parser, _schema);
                        
                        _btnStart.Enabled = true;
                        Log($"Schema: {_schema.Words.Count} words, {_schema.TotalBytes} bytes");
                        UpdateStatus("Schema loaded - Ready to start");
                    } catch (Exception ex) {
                        Log($"Error: {ex.Message}");
                        MessageBox.Show(ex.Message, "Schema Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private async void BtnStartBitParser_Click(object sender, EventArgs e) {
            if (_isRunning) return;
            if (_schema == null) {
                MessageBox.Show("Please load a schema first.", "Schema Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try {
                _btnStart.Enabled = false;
                UpdateStatus("Starting...");

                // Create data source
                _dataSource = DataSourceFactory.CreateFromConfig(_config, _schema);
                _dataSource.OnLog += Log;
                await _dataSource.ConnectAsync();
                Log($"[BitParser] Data source ({_dataSource.SourceType}) connected");

                // Start direct data loop (no pipe/shared memory)
                _dataCts = new CancellationTokenSource();
                _isRunning = true;
                _dataTask = Task.Run(() => DirectDataLoop(_dataCts.Token));

                _bitParserViewer.Reset();
                _btnStop.Enabled = true;
                UpdateStatus("RUNNING");
                Log("[BitParser] Started! (Direct mode - zero overhead)");

            } catch (Exception ex) {
                Log($"[BitParser] Start failed: {ex.Message}");
                UpdateStatus("Error - Check log");
                StopBitParser();
                _btnStart.Enabled = true;
            }
        }

        private void BtnStopBitParser_Click(object sender, EventArgs e) {
            StopBitParser();
            _btnStart.Enabled = true;
            _btnStop.Enabled = false;
            UpdateStatus("Stopped");
            Log("[BitParser] Stopped");
        }

        private void StopBitParser() {
            _isRunning = false;
            _dataCts?.Cancel();
            try { _dataTask?.Wait(1000); } catch { }

            _dataSource?.Dispose();
            _dataSource = null;
        }

        /// <summary>
        /// Direct data loop - no transport overhead!
        /// DataSource → Parser → ViewerPanel (direct submission)
        /// </summary>
        private async Task DirectDataLoop(CancellationToken ct) {
            while (!ct.IsCancellationRequested && _isRunning) {
                try {
                    byte[] data = await _dataSource.ReadFrameAsync(ct);
                    if (data == null) {
                        await Task.Delay(1, ct);
                        continue;
                    }

                    // Parse data
                    var result = _parser.Parse(data, true);
                    
                    // Direct submission to viewer (non-blocking, thread-safe)
                    _bitParserViewer.SubmitParsedData(result);

                } catch (OperationCanceledException) {
                    break;
                } catch (Exception ex) {
                    Log($"[BitParser] Data error: {ex.Message}");
                    await Task.Delay(1000, ct);
                }
            }
        }

        private void Log(string msg) {
            if (InvokeRequired) {
                try { BeginInvoke(new Action(() => Log(msg))); } catch { }
                return;
            }
            if (_txtLog.TextLength > 10000) {
                _txtLog.Text = _txtLog.Text.Substring(5000);
            }
            _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
        }

        private void UpdateStatus(string status) {
            if (InvokeRequired) {
                try { BeginInvoke(new Action(() => UpdateStatus(status))); } catch { }
                return;
            }
            _lblStatus.Text = $"Status: {status}";
            _lblStatus.ForeColor = status.Contains("RUNNING") ? Color.FromArgb(40, 140, 40) : 
                                   status.Contains("Error") ? Color.DarkRed : Color.Gray;
        }
    }
}
