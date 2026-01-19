using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace BitParser.DataSources {
    /// <summary>
    /// UDP data source - receives data from network.
    /// Useful for FPGA/embedded systems with Ethernet.
    /// </summary>
    public class UdpDataSource : IDataSource {
        private readonly int _port;
        private readonly int _expectedFrameSize;
        
        private UdpClient _udpClient;
        private bool _isConnected;
        private long _frameCount;
        private long _byteCount;
        private long _errorCount;
        private DateTime _startTime;
        private Timer _statsTimer;
        private double _currentFps;
        
        public bool IsConnected => _isConnected;
        public string SourceName => $"UDP Port {_port}";
        public DataSourceType SourceType => DataSourceType.Udp;
        
        public event Action<string> OnLog;
        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<Exception> OnError;
        
        public UdpDataSource(int port, int expectedFrameSize = 1536) {
            _port = port;
            _expectedFrameSize = expectedFrameSize;
        }
        
        public Task ConnectAsync(CancellationToken cancellationToken = default) {
            if (_isConnected) return Task.CompletedTask;
            
            try {
                _udpClient = new UdpClient(_port);
                _udpClient.Client.ReceiveBufferSize = 1024 * 1024;  // 1MB buffer
                
                _isConnected = true;
                _startTime = DateTime.Now;
                _frameCount = 0;
                
                _statsTimer = new Timer(UpdateStats, null, 1000, 1000);
                
                Log($"Listening on UDP port {_port}");
                OnConnected?.Invoke();
                
                return Task.CompletedTask;
            } catch (Exception ex) {
                Log($"Failed to open UDP port: {ex.Message}");
                OnError?.Invoke(ex);
                throw;
            }
        }
        
        public void Disconnect() {
            if (!_isConnected) return;
            
            _isConnected = false;
            _statsTimer?.Dispose();
            _statsTimer = null;
            
            try {
                _udpClient?.Close();
                _udpClient?.Dispose();
            } catch { }
            _udpClient = null;
            
            Log("UDP connection closed");
            OnDisconnected?.Invoke();
        }
        
        public async Task<byte[]> ReadFrameAsync(CancellationToken cancellationToken = default) {
            if (!_isConnected || _udpClient == null) {
                return null;
            }
            
            try {
                // Receive with timeout
                var receiveTask = _udpClient.ReceiveAsync();
                var timeoutTask = Task.Delay(5000, cancellationToken);
                
                var completedTask = await Task.WhenAny(receiveTask, timeoutTask);
                
                if (completedTask == timeoutTask) {
                    // Timeout - not necessarily an error, just no data
                    return null;
                }
                
                var result = await receiveTask;
                byte[] data = result.Buffer;
                
                _frameCount++;
                _byteCount += data.Length;
                
                // Validate frame size
                if (data.Length != _expectedFrameSize) {
                    _errorCount++;
                    Log($"WARNING: Received {data.Length} bytes, expected {_expectedFrameSize}");
                }
                
                return data;
                
            } catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut) {
                // Normal timeout
                return null;
            } catch (Exception ex) {
                _errorCount++;
                Log($"Receive error: {ex.Message}");
                OnError?.Invoke(ex);
                return null;
            }
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
                Errors = _errorCount,
                Uptime = _isConnected ? DateTime.Now - _startTime : TimeSpan.Zero,
                FramesPerSecond = _currentFps
            };
        }
        
        private void Log(string message) {
            OnLog?.Invoke($"[UDP] {message}");
        }
        
        public void Dispose() {
            Disconnect();
        }
    }
}
