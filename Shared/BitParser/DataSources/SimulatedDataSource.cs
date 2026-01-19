using System;
using System.Threading;
using System.Threading.Tasks;

namespace BitParser.DataSources {
    /// <summary>
    /// Simulated data source for testing.
    /// Generates realistic test data based on schema.
    /// This is a simple version that generates random bytes - for full schema support,
    /// use the real data generation in MainApp.
    /// </summary>
    public class SimulatedDataSource : IDataSource {
        private readonly int _frameSize;
        private readonly int _intervalMs;
        private readonly Random _random;
        
        private bool _isConnected;
        private long _frameCount;
        private long _byteCount;
        private DateTime _startTime;
        private Timer _statsTimer;
        private double _currentFps;
        
        public bool IsConnected => _isConnected;
        public string SourceName => "Simulation";
        public DataSourceType SourceType => DataSourceType.Simulation;
        
        public event Action<string> OnLog;
        public event Action OnConnected;
        public event Action OnDisconnected;
#pragma warning disable 67
        public event Action<Exception> OnError;
#pragma warning restore 67
        
        public SimulatedDataSource(CompiledSchema schema, int intervalMs = 50, double errorRate = 0.01) {
            _frameSize = schema?.TotalBytes ?? 1536;
            _intervalMs = intervalMs;
            _random = new Random();
        }
        
        public Task ConnectAsync(CancellationToken cancellationToken = default) {
            if (_isConnected) return Task.CompletedTask;
            
            _isConnected = true;
            _startTime = DateTime.Now;
            _frameCount = 0;
            
            _statsTimer = new Timer(UpdateStats, null, 1000, 1000);
            
            Log("Simulation started");
            OnConnected?.Invoke();
            
            return Task.CompletedTask;
        }
        
        public void Disconnect() {
            if (!_isConnected) return;
            
            _isConnected = false;
            _statsTimer?.Dispose();
            _statsTimer = null;
            
            Log("Simulation stopped");
            OnDisconnected?.Invoke();
        }
        
        public async Task<byte[]> ReadFrameAsync(CancellationToken cancellationToken = default) {
            if (!_isConnected) {
                return null;
            }
            
            // Simulate interval
            await Task.Delay(_intervalMs, cancellationToken);
            
            // Generate data
            byte[] data = new byte[_frameSize];
            _random.NextBytes(data);

            // Apply fixed headers (simulate valid FPGA)
            if (data.Length >= 12) {
                // Ver: 1.5.16
                data[0] = 0x01; data[1] = 0x05; data[2] = 0x10; 
                // Status: OK (0x0F)
                data[4] = 0x0F; data[5] = 0x00; data[6] = 0x00; data[7] = 0x00;
                // Error: 0
                data[8] = 0x00; data[9] = 0x00; data[10] = 0x00; data[11] = 0x00;
            }
            
            _frameCount++;
            _byteCount += data.Length;
            
            return data;
        }
        
        private long _lastFrameCount;
        
        private void UpdateStats(object state) {
            long currentFrames = _frameCount;
            _currentFps = currentFrames - _lastFrameCount;
            _lastFrameCount = currentFrames;
        }
        
        public DataSourceStats GetStats() {
            return new DataSourceStats {
                FramesRead = _frameCount,
                BytesRead = _byteCount,
                Errors = 0,
                Uptime = _isConnected ? DateTime.Now - _startTime : TimeSpan.Zero,
                FramesPerSecond = _currentFps
            };
        }
        
        private void Log(string message) {
            OnLog?.Invoke($"[Simulation] {message}");
        }
        
        public void Dispose() {
            Disconnect();
        }
    }
}
